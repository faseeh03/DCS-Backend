
using DCSBackend.Model;
using DCSBackend.Models;
using System;
using System.Linq;
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    [RoutePrefix("api/datacell")]
    public class DatacellController : BaseController
    {
        private const string RoleName = "Datacell";

        // GET api/datacell/pending/{userId}
        [HttpGet, Route("pending/{userId:int}")]
        public HttpResponseMessage Pending(int userId)
        {
            return PendingList(userId, RoleName);
        }

        // GET api/datacell/view/{userId}/{applicationId}
        [HttpGet, Route("view/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage View(int userId, int applicationId)
        {
            try
            {
                if (!HasRole(userId, RoleName))
                    return NotAllowed();

                var app = db.Clearance_Application
                    .Where(a => a.application_id == applicationId)
                    .Select(a => new
                    {
                        a.application_id,
                        a.application_status,
                        a.application_date,
                        a.UserLogin.Reg_no,
                        a.UserLogin.FirstName,
                        a.UserLogin.LastName
                    })
                    .FirstOrDefault();

                if (app == null)
                    return NotFoundMessage("Application not found.");

                var student = db.STMTRs
                    .Where(s => s.Reg_No == app.Reg_no)
                    .Select(s => new
                    {
                        s.St_firstname,
                        s.St_middlename,
                        s.St_lastname,
                        s.Final_course
                    })
                    .FirstOrDefault();

                var response = new
                {
                    ApplicationId = app.application_id,
                    Status = app.application_status,
                    SubmittedAt = app.application_date,
                    RegNo = app.Reg_no,

                    FullName = student == null
                        ? JoinName(app.FirstName, null, app.LastName)
                        : JoinName(
                            student.St_firstname,
                            student.St_middlename,
                            student.St_lastname
                        ),

                    Degree = student == null
                        ? null
                        : student.Final_course,

                    Eligibility = "Okay"
                };

                return Success(response);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // POST api/datacell/approve/{userId}/{applicationId}
        [HttpPost, Route("approve/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Approve(
            int userId,
            int applicationId)
        {
            try
            {
                if (!HasRole(userId, RoleName))
                    return NotAllowed();

                var app = db.Clearance_Application
                    .FirstOrDefault(a =>
                        a.application_id == applicationId);

                if (app == null)
                    return NotFoundMessage("Application not found.");

                // Check Datacell's pending approval
                var role = db.tbl_User_Roles
                    .FirstOrDefault(r =>
                        r.role_name == RoleName);

                if (role == null)
                    return Fail("Datacell role was not found.");

                bool hasPendingApproval = db.Clearance_Approvals
                    .Any(a =>
                        a.application_id == applicationId &&
                        a.role_id == role.role_id &&
                        a.approval_status == "Pending");

                if (app.application_status != "Pending" ||
                    !hasPendingApproval)
                {
                    return Fail(
                        "This application is no longer pending for Datacell."
                    );
                }

                // Datacell only approves its own department.
                // Student eligibility is checked before application submission.
                return ApproveStep(
                    userId,
                    applicationId,
                    RoleName
                );
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // POST api/datacell/reject/{userId}/{applicationId}
        // Body: { "reason": "..." }
        [HttpPost, Route("reject/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Reject(
            int userId,
            int applicationId,
            RejectRequest request)
        {
            return RejectStep(
                userId,
                applicationId,
                RoleName,
                request == null ? null : request.Reason
            );
        }
    }
}
