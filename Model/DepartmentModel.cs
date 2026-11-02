using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DCSBackend.Model
{
    // body of every reject request:  { "reason": "..." }
    public class RejectRequest
    {
        public string Reason { get; set; }
    }

    // one row in a department's pending list
    public class PendingApplicationResponse
    {
        public int ApplicationId { get; set; }
        public string RegNo { get; set; }
        public string StudentName { get; set; }
        public DateTime SubmittedAt { get; set; }
        public List<StepStatusResponse> Steps { get; set; }
    }
}