
using DCSBackend.Model;
using DCSBackend.Models;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    public abstract class BaseController : ApiController
    {
       
        protected BIITDBNewEntities db = new BIITDBNewEntities();

        

        protected HttpResponseMessage Success(object data)
        {
            return Request.CreateResponse(
                HttpStatusCode.OK, data);
        }

        protected HttpResponseMessage Fail(string message)
        {
            return Request.CreateResponse(
                HttpStatusCode.BadRequest,
                new { message = message });
        }

        protected HttpResponseMessage NotAllowed()
        {
            return Request.CreateResponse(
                HttpStatusCode.Forbidden,
                new
                {
                    message = "You are not allowed to do this."
                });
        }

        protected HttpResponseMessage NotFoundMessage(string message)
        {
            return Request.CreateResponse(
                HttpStatusCode.NotFound,
                new { message = message });
        }

        protected HttpResponseMessage ServerError(Exception ex)
        {
            return Request.CreateResponse(
                HttpStatusCode.InternalServerError,
                new { message = ex.Message });
        }

        // -----------------------------------------
        // COMMON HELPERS
        // -----------------------------------------

        // Check user role from database
        protected bool HasRole(int userId, string roleName)
        {
            return db.UserLogins.Any(u =>
                u.LoginId == userId &&
                u.IsActive &&
                u.tbl_User_Roles.role_name == roleName
            );
        }

        // Join first, middle and last name
        protected static string JoinName(
            string first,
            string middle,
            string last)
        {
            var parts = new[] { first, middle, last }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim());

            return string.Join(" ", parts);
        }

        // -----------------------------------------
        // UPDATE OVERALL APPLICATION STATUS
        // -----------------------------------------

        protected void UpdateApplicationStatus(
            Clearance_Application application)
        {
            var approvals = db.Clearance_Approvals
                .Where(p =>
                    p.application_id == application.application_id)
                .ToList();

            // All six departments must have approval records
            if (approvals.Count != 6)
            {
                application.application_status = "Pending";
                return;
            }

            // All six departments approved
            if (approvals.All(p =>
                p.approval_status == "Approved"))
            {
                application.application_status = "Completed";
                return;
            }

            // All departments have decided, but at least one rejected
            bool allDecided = approvals.All(p =>
                p.approval_status == "Approved" ||
                p.approval_status == "Rejected");

            bool anyRejected = approvals.Any(p =>
                p.approval_status == "Rejected");

            if (allDecided && anyRejected)
            {
                application.application_status = "Rejected";
                return;
            }

            // Some departments are still pending
            application.application_status = "Pending";
        }

        // -----------------------------------------
        // PENDING APPLICATIONS
        // -----------------------------------------

        // GET pending applications for a department
        protected HttpResponseMessage PendingList(
            int userId,
            string roleName)
        {
            try
            {
                if (!HasRole(userId, roleName))
                    return NotAllowed();

                var role = db.tbl_User_Roles
                    .FirstOrDefault(r =>
                        r.role_name == roleName);

                if (role == null)
                    return NotFoundMessage(
                        "Department role not found.");

                // Get applications where this department
                // has its own Pending approval.
                //
                // Other departments' Rejected decisions
                // do not block this department.

                var apps = db.Clearance_Approvals
                    .Where(p =>
                        p.role_id == role.role_id &&
                        p.approval_status == "Pending" &&
                        p.Clearance_Application.application_status == "Pending")
                    .Select(p => new
                    {
                        p.application_id,
                        p.Clearance_Application.application_date,
                        p.Clearance_Application.UserLogin.Reg_no,
                        p.Clearance_Application.UserLogin.FirstName,
                        p.Clearance_Application.UserLogin.LastName
                    })
                    .OrderBy(a => a.application_date)
                    .ToList();

                var applicationIds = apps
                    .Select(a => a.application_id)
                    .Distinct()
                    .ToList();

                // Get all department decisions
                var steps = db.Clearance_Approvals
                    .Where(p =>
                        applicationIds.Contains(p.application_id))
                    .Select(p => new
                    {
                        p.application_id,
                        Department = p.tbl_User_Roles.role_name,
                        Decision = p.approval_status,
                        p.rejection_reason,
                        p.action_date,
                        p.role_id
                    })
                    .OrderBy(p => p.role_id)
                    .ToList();

                var result = apps
                    .GroupBy(a => a.application_id)
                    .Select(group =>
                    {
                        var a = group.First();

                        return new PendingApplicationResponse
                        {
                            ApplicationId = a.application_id,
                            RegNo = a.Reg_no,

                            StudentName = JoinName(
                                a.FirstName,
                                null,
                                a.LastName),

                            SubmittedAt = a.application_date,

                            Steps = steps
                                .Where(s =>
                                    s.application_id == a.application_id)
                                .Select(s => new StepStatusResponse
                                {
                                    Department = s.Department,
                                    Decision = s.Decision,
                                    RejectionReason = s.rejection_reason,
                                    ActionAt = s.action_date
                                })
                                .ToList()
                        };
                    })
                    .ToList();

                return Success(result);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // -----------------------------------------
        // APPROVE APPLICATION
        // -----------------------------------------

        // Each department approves its own record independently
        protected HttpResponseMessage ApproveStep(
            int userId,
            int applicationId,
            string roleName,
            bool isForce = false)
        {
            try
            {
                if (!HasRole(userId, roleName))
                    return NotAllowed();

                var role = db.tbl_User_Roles
                    .FirstOrDefault(r =>
                        r.role_name == roleName);

                if (role == null)
                    return NotFoundMessage(
                        "Department role not found.");

                using (var tx = db.Database.BeginTransaction())
                {
                    var application = db.Clearance_Application
                        .FirstOrDefault(a =>
                            a.application_id == applicationId);

                    if (application == null)
                        return NotFoundMessage(
                            "Application not found.");

                    // Completed or fully rejected applications
                    // cannot receive new department decisions.
                    if (application.application_status != "Pending")
                    {
                        return Fail(
                            "This application is not available for approval.");
                    }

                    // Find only this department's pending approval
                    var approval = db.Clearance_Approvals
                        .FirstOrDefault(p =>
                            p.application_id == applicationId &&
                            p.role_id == role.role_id &&
                            p.approval_status == "Pending");

                    if (approval == null)
                    {
                        return Fail(
                            "This application is not waiting for your approval.");
                    }

                    DateTime now = DateTime.Now;

                    approval.approval_status = "Approved";
                    approval.action_by = userId;
                    approval.action_date = now;
                    approval.rejection_reason = null;

                    // Save history
                    db.Clearance_Approval_History.Add(
                        new Clearance_Approval_History
                        {
                            approval_id = approval.approval_id,
                            previous_status = "Pending",
                            new_status = "Approved",
                            remarks = isForce
                                ? "Force approved"
                                : "Approved",
                            action_by = userId,
                            action_date = now
                        });

                    db.SaveChanges();

                    // Recalculate overall application status
                    UpdateApplicationStatus(application);

                    db.SaveChanges();

                    tx.Commit();
                }

                return Success(new
                {
                    approved = true,
                    message = "Application approved."
                });
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // -----------------------------------------
        // REJECT APPLICATION
        // -----------------------------------------

        // Each department rejects its own record independently
        protected HttpResponseMessage RejectStep(
            int userId,
            int applicationId,
            string roleName,
            string reason)
        {
            try
            {
                reason = (reason ?? "").Trim();

                if (string.IsNullOrWhiteSpace(reason))
                    return Fail(
                        "Rejection reason is required.");

                if (reason.Length > 1000)
                    return Fail(
                        "Rejection reason is too long (max 1000 characters).");

                if (!HasRole(userId, roleName))
                    return NotAllowed();

                var role = db.tbl_User_Roles
                    .FirstOrDefault(r =>
                        r.role_name == roleName);

                if (role == null)
                    return NotFoundMessage(
                        "Department role not found.");

                using (var tx = db.Database.BeginTransaction())
                {
                    var application = db.Clearance_Application
                        .FirstOrDefault(a =>
                            a.application_id == applicationId);

                    if (application == null)
                        return NotFoundMessage(
                            "Application not found.");

                    if (application.application_status != "Pending")
                    {
                        return Fail(
                            "This application is not available for rejection.");
                    }

                    // Find only this department's pending approval
                    var approval = db.Clearance_Approvals
                        .FirstOrDefault(p =>
                            p.application_id == applicationId &&
                            p.role_id == role.role_id &&
                            p.approval_status == "Pending");

                    if (approval == null)
                    {
                        return Fail(
                            "This application is not waiting for your decision.");
                    }

                    DateTime now = DateTime.Now;

                    approval.approval_status = "Rejected";
                    approval.rejection_reason = reason;
                    approval.action_by = userId;
                    approval.action_date = now;

                    // Save rejection history
                    db.Clearance_Approval_History.Add(
                        new Clearance_Approval_History
                        {
                            approval_id = approval.approval_id,
                            previous_status = "Pending",
                            new_status = "Rejected",
                            remarks = reason,
                            action_by = userId,
                            action_date = now
                        });

                    db.SaveChanges();

                    // Important:
                    // Other departments remain Pending and can
                    // still approve or reject their own records.
                    UpdateApplicationStatus(application);

                    db.SaveChanges();

                    tx.Commit();
                }

                return Success(new
                {
                    rejected = true,
                    message = "Application rejected."
                });
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // -----------------------------------------
        // DISPOSE DATABASE
        // -----------------------------------------

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
