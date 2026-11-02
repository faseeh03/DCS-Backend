using DCSBackend.Model;
using DCSBackend.Models;   // change "DCSApi" to your project's name
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    /// <summary>
    /// Stationery: sees applications waiting for it, then approves or rejects (reason required).
    /// The workflow code is in BaseController. To add a Stationery-only endpoint, add a method here.
    /// </summary>
    [RoutePrefix("api/stationery")]
    public class StationeryController : BaseController
    {
        private const string RoleName = "Stationery";

        // GET api/stationery/pending/4
        [HttpGet, Route("pending/{userId:int}")]
        public HttpResponseMessage Pending(int userId)
        {
            return PendingList(userId, RoleName);
        }

        // POST api/stationery/approve/4/12
        [HttpPost, Route("approve/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Approve(int userId, int applicationId)
        {
            return ApproveStep(userId, applicationId, RoleName);
        }

        // POST api/stationery/reject/4/12     body: { "reason": "..." }
        [HttpPost, Route("reject/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Reject(int userId, int applicationId, RejectRequest request)
        {
            return RejectStep(userId, applicationId, RoleName,
                request == null ? null : request.Reason);
        }
    }
}