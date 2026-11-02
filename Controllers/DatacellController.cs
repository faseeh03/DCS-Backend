using DCSBackend.Model;
using DCSBackend.Models;   // change "DCSApi" to your project's name
using System;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.UI.WebControls;

namespace DCSBackend.Controllers
{
    /// <summary>
    /// Datacell: pending list, view (CGPA + courses), approve, reject.
    /// Datacell's decision is final: if the university data says "not eligible",
    /// Approve first returns a warning; calling it again with ?force=true approves anyway.
    /// </summary>
    [RoutePrefix("api/datacell")]
    public class DatacellController : BaseController
    {
        private const string RoleName = "Datacell";

        // ---- rules (change here) ----
        private const decimal MinCgpa = 2.5m;                          // CGPA must be >= 2.5
        private static readonly string[] FailGrades = { "F" };         // add "I", "W" ... if needed

        // GET api/datacell/pending/3
        [HttpGet, Route("pending/{userId:int}")]
        public HttpResponseMessage Pending(int userId)
        {
            return PendingList(userId, RoleName);
        }

        // GET api/datacell/view/3/12   ->  student info + CGPA + courses
        [HttpGet, Route("view/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage View(int userId, int applicationId)
        {
            try
            {
                if (!HasRole(userId, RoleName))
                    return NotAllowed();

                var app = GetApplication(applicationId);
                if (app == null)
                    return NotFoundMessage("Application not found.");

                var student = uniDb.STMTRs
                    .Where(s => s.Reg_No == app.RegNo)
                    .Select(s => new { s.St_firstname, s.St_middlename, s.St_lastname, s.Final_course })
                    .FirstOrDefault();

                var response = new DatacellViewResponse
                {
                    ApplicationId = app.ApplicationId,
                    Status = app.Status,
                    SubmittedAt = app.SubmittedAt,
                    RegNo = app.RegNo,
                    FullName = student == null ? app.FullName
                               : JoinName(student.St_firstname, student.St_middlename, student.St_lastname),
                    Degree = student == null ? null : student.Final_course,
                    Eligibility = CheckEligibility(app.RegNo)
                };

                return Success(response);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // POST api/datacell/approve/3/12            -> approves if eligible, otherwise warning
        // POST api/datacell/approve/3/12?force=true -> approves even if not eligible
        [HttpPost, Route("approve/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Approve(int userId, int applicationId, bool force = false)
        {
            try
            {
                if (!HasRole(userId, RoleName))
                    return NotAllowed();

                var app = GetApplication(applicationId);
                if (app == null)
                    return NotFoundMessage("Application not found.");

                var check = CheckEligibility(app.RegNo);

                if (!check.IsEligible && !force)
                {
                    // nothing is saved; frontend shows a popup and may call again with force=true
                    return Success(new
                    {
                        approved = false,
                        needsConfirmation = true,
                        message = "Student does not meet the requirements. Force approve?",
                        cgpa = check.Cgpa,
                        minCgpa = check.MinCgpa,
                        problems = check.Problems
                    });
                }

                // history shows "ForceApproved" only when we really overrode a failed check
                bool wasOverridden = force && !check.IsEligible;
                return ApproveStep(userId, applicationId, RoleName, wasOverridden);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // POST api/datacell/reject/3/12     body: { "reason": "..." }
        [HttpPost, Route("reject/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Reject(int userId, int applicationId, RejectRequest request)
        {
            return RejectStep(userId, applicationId, RoleName,
                request == null ? null : request.Reason);
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        private class ApplicationInfo
        {
            public int ApplicationId { get; set; }
            public string Status { get; set; }
            public DateTime SubmittedAt { get; set; }
            public string RegNo { get; set; }
            public string FullName { get; set; }
        }

        private ApplicationInfo GetApplication(int applicationId)
        {
            return db.ClearanceApplications
                .Where(a => a.ApplicationId == applicationId)
                .Select(a => new ApplicationInfo
                {
                    ApplicationId = a.ApplicationId,
                    Status = a.Status,
                    SubmittedAt = a.SubmittedAt,
                    RegNo = a.User.RegNo,
                    FullName = a.User.FullName
                })
                .FirstOrDefault();
        }

        // Reads CGPA (8th semester) and courses from the university DB
        private EligibilityResponse CheckEligibility(string regNo)
        {
            // CGPA: only the 8th semester row
            // (if SemC is a string column in your DB, write  a.SemC == "8")
            var cgpaRow = uniDb.Accgpas
                .Where(a => a.REG_NO == regNo && a.SemC == 8)
                .Select(a => new { a.CGPA })
                .FirstOrDefault();

            decimal? cgpa = cgpaRow == null ? null : ToDecimal(cgpaRow.CGPA);

            // Courses: a course counts as passed if ANY attempt passed (retakes)
            var attempts = uniDb.Crsdtls
                .Where(c => c.REG_NO == regNo)
                .Select(c => new { c.Course_no, c.Grade })
                .ToList();

            var courses = attempts
                .GroupBy(a => Convert.ToString(a.Course_no))
                .Select(g =>
                {
                    var best = g.FirstOrDefault(x => IsPassingGrade(Convert.ToString(x.Grade))) ?? g.First();
                    return new CourseResult
                    {
                        CourseNo = g.Key,
                        Grade = (Convert.ToString(best.Grade) ?? "").Trim(),
                        IsPassed = g.Any(x => IsPassingGrade(Convert.ToString(x.Grade)))
                    };
                })
                .OrderBy(c => c.IsPassed)          // problems first
                .ThenBy(c => c.CourseNo)
                .ToList();

            var result = new EligibilityResponse { MinCgpa = MinCgpa, Cgpa = cgpa, Courses = courses };
            result.CgpaOk = cgpa.HasValue && cgpa.Value >= MinCgpa;
            result.CourseCount = courses.Count;
            result.NotPassedCount = courses.Count(c => !c.IsPassed);
            result.CoursesOk = courses.Count > 0 && result.NotPassedCount == 0;
            result.IsEligible = result.CgpaOk && result.CoursesOk;

            if (!cgpa.HasValue)
                result.Problems.Add("No 8th semester CGPA record found.");
            else if (!result.CgpaOk)
                result.Problems.Add("CGPA is " + cgpa.Value.ToString("0.00") + " (minimum " + MinCgpa.ToString("0.00") + ").");

            if (courses.Count == 0)
                result.Problems.Add("No course records found.");
            else if (result.NotPassedCount > 0)
                result.Problems.Add(result.NotPassedCount + " course(s) not passed: "
                    + string.Join(", ", courses.Where(c => !c.IsPassed).Select(c => c.CourseNo)) + ".");

            return result;
        }

        // empty grade = not graded yet = not passed
        private static bool IsPassingGrade(string grade)
        {
            string g = (grade ?? "").Trim().ToUpper();
            return g != "" && !FailGrades.Contains(g);
        }

        // works whether the CGPA column is decimal, float or text
        private static decimal? ToDecimal(object value)
        {
            if (value == null) return null;
            decimal d;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out d)
                ? (decimal?)d : null;
        }
    }
}