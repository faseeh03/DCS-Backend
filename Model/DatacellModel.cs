using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DCSBackend.Model
{
    public class CourseResult
    {
        public string CourseNo { get; set; }
        public string Grade { get; set; }
        public bool IsPassed { get; set; }
    }

    // result of the CGPA + courses check (read from the university DB)
    public class EligibilityResponse
    {
        public decimal MinCgpa { get; set; }
        public decimal? Cgpa { get; set; }          // null = no 8th semester record
        public bool CgpaOk { get; set; }
        public int CourseCount { get; set; }
        public int NotPassedCount { get; set; }
        public bool CoursesOk { get; set; }
        public bool IsEligible { get; set; }
        public List<string> Problems { get; set; } = new List<string>();
        public List<CourseResult> Courses { get; set; } = new List<CourseResult>();
    }

    // Datacell "View application" screen
    public class DatacellViewResponse
    {
        public int ApplicationId { get; set; }
        public string Status { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string RegNo { get; set; }
        public string FullName { get; set; }
        public string Degree { get; set; }
        public EligibilityResponse Eligibility { get; set; }
    }
}