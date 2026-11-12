
using DCSBackend.Model;
using DCSBackend.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    [RoutePrefix("api/student")]
    public class StudentController : BaseController
    {
        private const decimal MinCgpa = 2.50m;

        private static readonly string[] DepartmentRoles =
        {
            "Datacell",
            "Cafeteria",
            "Stationery",
            "Accounts",
            "Librarian",
            "Supervisor"
        };

        // -----------------------------------------
        // GET api/student/me/{userId}
        // -----------------------------------------

        [HttpGet, Route("me/{userId:int}")]
        public HttpResponseMessage Me(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                var user = db.UserLogins
                    .FirstOrDefault(u => u.LoginId == userId);

                if (user == null || string.IsNullOrWhiteSpace(user.Reg_no))
                    return NotFoundMessage(
                        "No registration number on this account.");

                var student = db.STMTRs
                    .FirstOrDefault(s => s.Reg_No == user.Reg_no);

                if (student == null)
                    return NotFoundMessage(
                        "Student not found in the university database.");

                var response = new StudentInfoResponse
                {
                    RegNo = user.Reg_no,
                    FullName = JoinName(
                        student.St_firstname,
                        student.St_middlename,
                        student.St_lastname),
                    Degree = student.Final_course
                };

                return Success(response);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // -----------------------------------------
        // Eligibility Helper
        // -----------------------------------------


        private EligibilityResponse GetEligibility(string regNo)
        {
            var result = new EligibilityResponse
            {
                MinCgpa = MinCgpa
            };

            // 1. Check latest semester
            var semesterRows = db.Accgpas
                .Where(a => a.REG_NO == regNo)
                .Select(a => a.SemC)
                .ToList();

            int? latestSemester = semesterRows.Count > 0
                ? (int?)semesterRows.Max()
                : null;

            result.LatestSemester = latestSemester;

            bool semesterOk = latestSemester.HasValue &&
                              latestSemester.Value >= 8;

            if (!semesterOk)
            {
                result.Problems.Add(
                    latestSemester.HasValue
                        ? "You are currently in " + latestSemester.Value +
                          "th semester. You cannot apply for degree clearance before completing the 8th semester."
                        : "Semester record not found."
                );
            }

            // 2. Calculate OVERALL CGPA using all studied courses
            decimal totalQualityPoints = 0;
            decimal totalCreditHours = 0;

            // Get all courses studied by this student
            var studentCourses = db.Crsdtls
                .Where(c => c.REG_NO == regNo)
                .ToList();

            // Get matching course information from CRSMTR
            var courseInformation = db.CRSMTRs.ToList();

            foreach (var course in studentCourses)
            {
                var courseInfo = courseInformation.FirstOrDefault(m =>
                    m.Course_no == course.Course_no &&
                    m.Discipline.Trim() == course.DISCIPLINE.Trim() &&
                    m.SOS.Trim() == course.SOS.Trim()
                );

                if (courseInfo == null ||
                    string.IsNullOrWhiteSpace(courseInfo.Credit_hrs))
                {
                    continue;
                }

                // Extract credit hours from values like 3(2-2)
                string creditText = courseInfo.Credit_hrs.Trim();

                int index = 0;

                while (index < creditText.Length &&
                       char.IsDigit(creditText[index]))
                {
                    index++;
                }

                if (index == 0)
                    continue;

                int creditHours;

                if (!int.TryParse(
                    creditText.Substring(0, index),
                    out creditHours))
                {
                    continue;
                }

                // Add quality points and credit hours
                totalQualityPoints += Convert.ToDecimal(course.Q_points);
                totalCreditHours += creditHours;
            }

            // Calculate overall CGPA
            decimal? cgpa = totalCreditHours > 0
                ? Math.Round(totalQualityPoints / totalCreditHours, 2)
                : (decimal?)null;

            result.Cgpa = cgpa;

            result.CgpaOk = cgpa.HasValue &&
                            cgpa.Value >= MinCgpa;

            if (!cgpa.HasValue)
            {
                result.Problems.Add(
                    "Overall CGPA could not be calculated."
                );
            }
            else if (cgpa.Value < MinCgpa)
            {
                result.Problems.Add(
                    "Your overall CGPA must be at least 2.50."
                );
            }

            // 3. Get student degree and SOS from STMTR
            var student = db.STMTRs.FirstOrDefault(s =>
                s.Reg_No == regNo
            );

            bool studentFound = student != null;

            if (!studentFound)
            {
                result.Problems.Add(
                    "Student academic record not found."
                );
            }

            // 4. Get required courses from CRSMTR
            var requiredCourses = new List<CRSMTR>();

            if (studentFound)
            {
                string degree = student.Final_course;
                string sos = student.SOS == null
                    ? ""
                    : student.SOS.Trim();

                requiredCourses = db.CRSMTRs
                    .Where(c => c.Discipline == degree)
                    .ToList()
                    .Where(c => c.SOS != null &&
                                c.SOS.Trim() == sos)
                    .ToList();
            }

            result.CourseCount = requiredCourses.Count;

            // 5. Check student's course attempts
            var studentAttempts = db.Crsdtls
                .Where(c => c.REG_NO == regNo)
                .ToList();

            foreach (var requiredCourse in requiredCourses)
            {
                var attempts = studentAttempts
                    .Where(c =>
                        c.Course_no == requiredCourse.Course_no &&
                        c.DISCIPLINE == requiredCourse.Discipline &&
                        c.SOS == requiredCourse.SOS
                    )
                    .ToList();

                // A course is passed if at least one attempt
                // has a passing grade.
                bool isPassed = attempts.Any(a =>
                    !string.IsNullOrWhiteSpace(a.Grade) &&
                    !a.Grade.Trim().Equals(
                        "F",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                result.Courses.Add(new CourseResult
                {
                    CourseNo = requiredCourse.Course_no,
                    Grade = attempts
                        .OrderByDescending(a => a.SEMESTER_NO)
                        .Select(a => a.Grade)
                        .FirstOrDefault(),
                    IsPassed = isPassed
                });

                if (!isPassed)
                {
                    result.NotPassedCount++;
                }
            }

            // 6. Final course eligibility
            result.CoursesOk = result.NotPassedCount == 0;

            if (!result.CoursesOk)
            {
                result.Problems.Add(
                    result.NotPassedCount +
                    " required course(s) have not been passed."
                );
            }

            // 7. Overall eligibility
            result.IsEligible =
                semesterOk &&
                result.CgpaOk &&
                result.CoursesOk &&
                studentFound;

            return result;
        }


        // -----------------------------------------
        // GET api/student/eligibility/{userId}
        // Dashboard eligibility check
        // -----------------------------------------

        [HttpGet, Route("eligibility/{userId:int}")]
        public HttpResponseMessage Eligibility(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                var user = db.UserLogins
                    .FirstOrDefault(u => u.LoginId == userId);

                if (user == null ||
                    string.IsNullOrWhiteSpace(user.Reg_no))
                    return NotFoundMessage(
                        "Student registration number not found.");

                var result = GetEligibility(user.Reg_no);

                return Success(result);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // -----------------------------------------
        // POST api/student/apply/{userId}
        // -----------------------------------------

        [HttpPost, Route("apply/{userId:int}")]
        public HttpResponseMessage Apply(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                var user = db.UserLogins
                    .FirstOrDefault(u => u.LoginId == userId);

                if (user == null ||
                    string.IsNullOrWhiteSpace(user.Reg_no))
                    return NotFoundMessage(
                        "Student registration number not found.");

                // Backend eligibility verification
                var eligibility = GetEligibility(user.Reg_no);

                if (!eligibility.IsEligible)
                {
                    return Fail(
                        "You are not eligible for clearance. " +
                        string.Join(" ", eligibility.Problems));
                }

                if (db.Clearance_Application.Any(
                    a => a.LoginId == userId))
                {
                    return Fail("You have already applied.");
                }

                using (var tx = db.Database.BeginTransaction())
                {
                    var roles = db.tbl_User_Roles
                        .Where(r =>
                            DepartmentRoles.Contains(r.role_name))
                        .ToList();

                    if (roles.Count != 6)
                        return Fail(
                            "All six department roles must exist in the database.");

                    var application = new Clearance_Application
                    {
                        LoginId = userId,
                        application_date = DateTime.Now,
                        application_status = "Pending"
                    };

                    db.Clearance_Application.Add(application);
                    db.SaveChanges();

                    // Create all six approvals simultaneously
                    var approvals = roles.Select(role =>
                        new Clearance_Approvals
                        {
                            application_id = application.application_id,
                            role_id = role.role_id,
                            approval_status = "Pending"
                        }).ToList();

                    db.Clearance_Approvals.AddRange(approvals);
                    db.SaveChanges();

                    // Keep submission history for each department.
                    foreach (var approval in approvals)
                    {
                        db.Clearance_Approval_History.Add(
                            new Clearance_Approval_History
                            {
                                approval_id = approval.approval_id,
                                previous_status = null,
                                new_status = "Pending",
                                remarks = "Application submitted",
                                action_by = userId,
                                action_date = DateTime.Now
                            });
                    }

                    db.SaveChanges();
                    tx.Commit();

                    return Success(new
                    {
                        applicationId = application.application_id,
                        message = "Application submitted."
                    });
                }
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // -----------------------------------------
        // GET api/student/application/{userId}
        // -----------------------------------------

        [HttpGet, Route("application/{userId:int}")]
        public HttpResponseMessage MyApplication(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                var app = db.Clearance_Application
                    .FirstOrDefault(a => a.LoginId == userId);

                if (app == null)
                    return Success(new { applied = false });

                var steps = db.Clearance_Approvals
                    .Where(p => p.application_id == app.application_id)
                    .Select(p => new StepStatusResponse
                    {
                        Department = p.tbl_User_Roles.role_name,
                        Decision = p.approval_status,
                        RejectionReason = p.rejection_reason,
                        ActionAt = p.action_date
                    })
                    .ToList();

                var response = new MyApplicationResponse
                {
                    ApplicationId = app.application_id,
                    Status = app.application_status,
                    SubmittedAt = app.application_date,

                    // Kept for existing Flutter response compatibility.
                    CurrentStep = "Multiple Departments",

                    CanResubmit = steps.Any(
                        s => s.Decision == "Rejected"),

                    Steps = steps
                };

                return Success(response);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // -----------------------------------------
        // POST api/student/resubmit/{userId}
        // Resets rejected departments only
        // -----------------------------------------

        [HttpPost, Route("resubmit/{userId:int}")]
        public HttpResponseMessage Resubmit(int userId)
        {
            try
            {
                if (!HasRole(userId, "Student"))
                    return NotAllowed();

                using (var tx = db.Database.BeginTransaction())
                {
                    var app = db.Clearance_Application
                        .FirstOrDefault(a => a.LoginId == userId);

                    if (app == null)
                        return NotFoundMessage(
                            "Application not found.");

                    var rejectedApprovals = db.Clearance_Approvals
                        .Where(p =>
                            p.application_id == app.application_id &&
                            p.approval_status == "Rejected")
                        .ToList();

                    if (rejectedApprovals.Count == 0)
                        return Fail(
                            "There are no rejected departments to resubmit.");

                    DateTime now = DateTime.Now;

                    foreach (var approval in rejectedApprovals)
                    {
                        // Keep previous rejection in history.
                        db.Clearance_Approval_History.Add(
                            new Clearance_Approval_History
                            {
                                approval_id = approval.approval_id,
                                previous_status = "Rejected",
                                new_status = "Pending",
                                remarks = "Student resubmitted",
                                action_by = userId,
                                action_date = now
                            });

                        approval.approval_status = "Pending";
                        approval.rejection_reason = null;
                        approval.action_by = null;
                        approval.action_date = null;
                    }

                    app.application_status = "Pending";

                    db.SaveChanges();
                    tx.Commit();
                }

                return Success(new
                {
                    message = "Rejected departments resubmitted successfully."
                });
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }
    }
}
