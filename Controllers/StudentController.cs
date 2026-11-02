using DCSBackend.Model;
using DCSBackend.Models;   // change "DCSApi" to your project's name
using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.UI.WebControls;

namespace DCSBackend.Controllers
{
    /// <summary>
    /// Everything a student can do. The frontend sends the logged-in student's
    /// userId in the URL. To add a student endpoint: just add a method here.
    /// </summary>
    [RoutePrefix("api/student")]
    public class StudentController : BaseController
    {
        // GET api/student/me/5   ->  name, RegNo, degree
        [HttpGet, Route("me/{userId:int}")]
        public HttpResponseMessage Me(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                string regNo = db.Users
                    .Where(u => u.UserId == userId)
                    .Select(u => u.RegNo)
                    .FirstOrDefault();

                if (regNo == null)
                    return NotFoundMessage("No registration number on this account.");

                // university DB (BIITDBNew)
                var student = uniDb.STMTRs
                    .Where(s => s.Reg_No == regNo)
                    .Select(s => new { s.St_firstname, s.St_middlename, s.St_lastname, s.Final_course })
                    .FirstOrDefault();

                if (student == null)
                    return NotFoundMessage("Student not found in the university database.");

                var response = new StudentInfoResponse
                {
                    RegNo = regNo,
                    FullName = JoinName(student.St_firstname, student.St_middlename, student.St_lastname),
                    Degree = student.Final_course
                };

                return Success(response);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // POST api/student/apply/5   ->  "Apply for degree clearance" button
        [HttpPost, Route("apply/{userId:int}")]
        public HttpResponseMessage Apply(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                if (db.ClearanceApplications.Any(a => a.StudentUserId == userId))
                    return Fail("You have already applied.");

                // workflow steps = roles that have a WorkflowOrder (Datacell ... Supervisor)
                var steps = db.Roles
                    .Where(r => r.WorkflowOrder != null)
                    .OrderBy(r => r.WorkflowOrder)
                    .ToList();

                var application = new ClearanceApplication
                {
                    StudentUserId = userId,
                    Status = "InProgress",
                    SubmittedAt = DateTime.Now,
                    CurrentStepRoleId = steps.First().RoleId      // Datacell
                };

                foreach (var step in steps)
                {
                    application.ClearanceApprovals.Add(new ClearanceApproval
                    {
                        DepartmentRoleId = step.RoleId,
                        Decision = "Pending"
                    });
                }

                db.ClearanceApplications.Add(application);
                db.ApplicationHistories.Add(new ApplicationHistory
                {
                    ClearanceApplication = application,
                    ActionByUserId = userId,
                    ActionType = "Submitted",
                    CreatedAt = DateTime.Now
                });

                try
                {
                    db.SaveChanges();     // saves everything in one transaction
                }
                catch (DbUpdateException)
                {
                    // UNIQUE(StudentUserId): double click / second request
                    return Fail("You have already applied.");
                }

                return Success(new { applicationId = application.ApplicationId, message = "Application submitted." });
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // GET api/student/application/5   ->  status of every step (+ rejection reason)
        [HttpGet, Route("application/{userId:int}")]
        public HttpResponseMessage MyApplication(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                // a.Role = the department the application is currently with
                var app = db.ClearanceApplications
                    .Where(a => a.StudentUserId == userId)
                    .Select(a => new
                    {
                        a.ApplicationId,
                        a.Status,
                        a.SubmittedAt,
                        CurrentStep = a.Role.RoleName
                    })
                    .FirstOrDefault();

                if (app == null)
                    return Success(new { applied = false });

                // p.Role = the department of that approval row
                var steps = db.ClearanceApprovals
                    .Where(p => p.ApplicationId == app.ApplicationId)
                    .OrderBy(p => p.Role.WorkflowOrder)
                    .Select(p => new StepStatusResponse
                    {
                        Department = p.Role.RoleName,
                        Decision = p.Decision,
                        RejectionReason = p.RejectionReason,
                        ActionAt = p.ActionAt
                    })
                    .ToList();

                var response = new MyApplicationResponse
                {
                    ApplicationId = app.ApplicationId,
                    Status = app.Status,
                    SubmittedAt = app.SubmittedAt,
                    CurrentStep = app.CurrentStep,
                    CanResubmit = app.Status == "Rejected",
                    Steps = steps
                };

                return Success(response);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // POST api/student/resubmit/5   ->  resubmit icon on the rejected step
        // Same application goes back to the department that rejected it.
        [HttpPost, Route("resubmit/{userId:int}")]
        public HttpResponseMessage Resubmit(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                using (var tx = db.Database.BeginTransaction())
                {
                    // guard: only works if the application is Rejected right now
                    int changed = db.Database.ExecuteSqlCommand(
                        @"UPDATE ClearanceApplications SET Status = 'InProgress'
                          WHERE StudentUserId = {0} AND Status = 'Rejected'", userId);

                    if (changed == 0)
                        return Fail("There is no rejected application to resubmit.");   // tx rolls back

                    var app = db.ClearanceApplications
                        .AsNoTracking()
                        .First(a => a.StudentUserId == userId);

                    // the rejected step becomes Pending again (old reason stays in history)
                    db.Database.ExecuteSqlCommand(
                        @"UPDATE ClearanceApprovals
                          SET Decision = 'Pending', RejectionReason = NULL,
                              ActionByUserId = NULL, ActionAt = NULL
                          WHERE ApplicationId = {0} AND DepartmentRoleId = {1}
                            AND Decision = 'Rejected'",
                        app.ApplicationId, app.CurrentStepRoleId);

                    db.ApplicationHistories.Add(new ApplicationHistory
                    {
                        ApplicationId = app.ApplicationId,
                        ActionByUserId = userId,
                        DepartmentRoleId = app.CurrentStepRoleId,
                        ActionType = "Resubmitted",
                        CreatedAt = DateTime.Now
                    });
                    db.SaveChanges();

                    tx.Commit();
                }

                return Success(new { message = "Application resubmitted." });
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }
    }
}