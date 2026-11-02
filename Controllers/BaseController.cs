using DCSBackend.Model;
using DCSBackend.Models;  
using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    /// <summary>
    /// Every controller inherits this. It gives you:
    ///   db, uniDb              -> DCSDB and BIITDBNew (separate EF contexts)
    ///   Success/Fail/...       -> responses (they use Request.CreateResponse inside)
    ///   HasRole()         -> is this userId an active user with this role?
    ///   PendingList / ApproveStep / RejectStep
    ///                          -> the shared workflow used by all department controllers
    /// </summary>
    public abstract class BaseController : ApiController
    {
        protected DCSDBEntities db = new DCSDBEntities();
        protected BIITDBNewEntities uniDb = new BIITDBNewEntities();

        
        // Responses
        
        protected HttpResponseMessage Success(object data)
        {
            return Request.CreateResponse(HttpStatusCode.OK, data);
        }

        protected HttpResponseMessage Fail(string message)
        {
            return Request.CreateResponse(HttpStatusCode.BadRequest, new { message = message });
        }

        protected HttpResponseMessage NotAllowed()
        {
            return Request.CreateResponse(HttpStatusCode.Forbidden,
                new { message = "You are not allowed to do this." });
        }

        protected HttpResponseMessage NotFoundMessage(string message)
        {
            return Request.CreateResponse(HttpStatusCode.NotFound, new { message = message });
        }

        protected HttpResponseMessage ServerError(Exception ex)
        {
            return Request.CreateResponse(HttpStatusCode.InternalServerError, new { message = ex.Message });
        }

        
        // Small helpers
        

        // role is checked in the DATABASE (the frontend only sends the userId)
        protected bool HasRole(int userId, string roleName)
        {
            return db.Users.Any(u => u.UserId == userId
                                             && u.IsActive
                                             && u.Role.RoleName == roleName);
        }

        protected static string JoinName(string first, string middle, string last)
        {
            var parts = new[] { first, middle, last }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim());
            return string.Join(" ", parts);
        }

        
        // Department workflow (same for every department)
        

        // Applications that are waiting for this department right now
        protected HttpResponseMessage PendingList(int userId, string roleName)
        {
            try
            {
                if (!HasRole(userId, roleName))
                    return NotAllowed();

                // a.Role = the department the application is currently with
                var apps = db.ClearanceApplications
                    .Where(a => a.Status == "InProgress" && a.Role.RoleName == roleName)
                    .OrderBy(a => a.SubmittedAt)
                    .Select(a => new
                    {
                        a.ApplicationId,
                        a.SubmittedAt,
                        RegNo = a.User.RegNo,
                        StudentName = a.User.FullName
                    })
                    .ToList();

                var ids = apps.Select(a => a.ApplicationId).ToList();

                var steps = db.ClearanceApprovals
                    .Where(p => ids.Contains(p.ApplicationId))
                    .Select(p => new
                    {
                        p.ApplicationId,
                        Department = p.Role.RoleName,
                        p.Decision,
                        p.Role.WorkflowOrder
                    })
                    .ToList();

                var result = apps.Select(a => new PendingApplicationResponse
                {
                    ApplicationId = a.ApplicationId,
                    RegNo = a.RegNo,
                    StudentName = a.StudentName,
                    SubmittedAt = a.SubmittedAt,
                    Steps = steps
                        .Where(s => s.ApplicationId == a.ApplicationId)
                        .OrderBy(s => s.WorkflowOrder)
                        .Select(s => new StepStatusResponse
                        {
                            Department = s.Department,
                            Decision = s.Decision
                        })
                        .ToList()
                }).ToList();

                return Success(result);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // Approve: works only if the application is at this department's step.
        // Then it moves to the next department (after Supervisor -> Completed).
        protected HttpResponseMessage ApproveStep(
            int userId, int applicationId, string roleName, bool isForce = false)
        {
            try
            {
                if (!HasRole(userId, roleName))
                    return NotAllowed();

                // workflow order comes from Roles.WorkflowOrder
                var steps = db.Roles
                    .Where(r => r.WorkflowOrder != null)
                    .OrderBy(r => r.WorkflowOrder)
                    .ToList();

                var myStep = steps.First(r => r.RoleName == roleName);
                var nextStep = steps.FirstOrDefault(r => r.WorkflowOrder > myStep.WorkflowOrder);   // null after Supervisor

                using (var tx = db.Database.BeginTransaction())
                {
                    // guard: only succeeds if it is really this department's turn
                    int changed = db.Database.ExecuteSqlCommand(
                        @"UPDATE ClearanceApprovals
                          SET Decision = 'Approved', ActionByUserId = {0}, ActionAt = SYSDATETIME()
                          WHERE ApplicationId = {1} AND DepartmentRoleId = {2} AND Decision = 'Pending'
                            AND EXISTS (SELECT 1 FROM ClearanceApplications a
                                        WHERE a.ApplicationId = {1} AND a.Status = 'InProgress'
                                          AND a.CurrentStepRoleId = {2})",
                        userId, applicationId, myStep.RoleId);

                    if (changed == 0)
                        return Fail("This application is not waiting for your approval.");   // tx rolls back

                    if (nextStep == null)
                    {
                        db.Database.ExecuteSqlCommand(
                            @"UPDATE ClearanceApplications
                              SET Status = 'Completed', CurrentStepRoleId = NULL
                              WHERE ApplicationId = {0}", applicationId);
                    }
                    else
                    {
                        db.Database.ExecuteSqlCommand(
                            @"UPDATE ClearanceApplications SET CurrentStepRoleId = {1}
                              WHERE ApplicationId = {0}", applicationId, nextStep.RoleId);
                    }

                    db.ApplicationHistories.Add(new ApplicationHistory
                    {
                        ApplicationId = applicationId,
                        ActionByUserId = userId,
                        DepartmentRoleId = myStep.RoleId,
                        ActionType = isForce ? "ForceApproved" : "Approved",
                        CreatedAt = DateTime.Now
                    });
                    db.SaveChanges();

                    tx.Commit();
                }

                return Success(new { approved = true, message = "Application approved." });
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // Reject: reason is mandatory. Application stays with this department
        // (Status = Rejected) until the student resubmits it.
        protected HttpResponseMessage RejectStep(
            int userId, int applicationId, string roleName, string reason)
        {
            try
            {
                reason = (reason ?? "").Trim();
                if (reason == "")
                    return Fail("Rejection reason is required.");
                if (reason.Length > 1000)
                    return Fail("Rejection reason is too long (max 1000 characters).");

                if (!HasRole(userId, roleName))
                    return NotAllowed();

                var myRole = db.Roles.First(r => r.RoleName == roleName);

                using (var tx = db.Database.BeginTransaction())
                {
                    int changed = db.Database.ExecuteSqlCommand(
                        @"UPDATE ClearanceApprovals
                          SET Decision = 'Rejected', RejectionReason = {0},
                              ActionByUserId = {1}, ActionAt = SYSDATETIME()
                          WHERE ApplicationId = {2} AND DepartmentRoleId = {3} AND Decision = 'Pending'
                            AND EXISTS (SELECT 1 FROM ClearanceApplications a
                                        WHERE a.ApplicationId = {2} AND a.Status = 'InProgress'
                                          AND a.CurrentStepRoleId = {3})",
                        reason, userId, applicationId, myRole.RoleId);

                    if (changed == 0)
                        return Fail("This application is not waiting for your decision.");   // tx rolls back

                    // CurrentStepRoleId is NOT changed: it stays on the rejecting department
                    db.Database.ExecuteSqlCommand(
                        @"UPDATE ClearanceApplications SET Status = 'Rejected'
                          WHERE ApplicationId = {0}", applicationId);

                    db.ApplicationHistories.Add(new ApplicationHistory
                    {
                        ApplicationId = applicationId,
                        ActionByUserId = userId,
                        DepartmentRoleId = myRole.RoleId,
                        ActionType = "Rejected",
                        Notes = reason,
                        CreatedAt = DateTime.Now
                    });
                    db.SaveChanges();

                    tx.Commit();
                }

                return Success(new { rejected = true, message = "Application rejected." });
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
                uniDb.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}