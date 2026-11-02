using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DCSBackend.Model
{
    // dashboard info (comes from the university DB)
    public class StudentInfoResponse
    {
        public string RegNo { get; set; }
        public string FullName { get; set; }
        public string Degree { get; set; }
    }

    // one department's decision on an application
    public class StepStatusResponse
    {
        public string Department { get; set; }
        public string Decision { get; set; }
        public string RejectionReason { get; set; }
        public DateTime? ActionAt { get; set; }
    }

    // student's own application with all steps
    public class MyApplicationResponse
    {
        public int ApplicationId { get; set; }
        public string Status { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string CurrentStep { get; set; }
        public bool CanResubmit { get; set; }
        public List<StepStatusResponse> Steps { get; set; }
    }
}