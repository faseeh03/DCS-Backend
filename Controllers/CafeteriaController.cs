using DCSBackend.Model;
using DCSBackend.Models;   
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    /// <summary>
    /// Cafeteria: sees applications waiting for it, then approves or rejects (reason required).
    /// The workflow code is in BaseController. To add a Cafeteria-only endpoint, add a method here.
    /// </summary>
    [RoutePrefix("api/cafeteria")]
    public class CafeteriaController : BaseController
    {
        private const string RoleName = "Cafeteria";

        // GET api/cafeteria/pending/4
        [HttpGet, Route("pending/{userId:int}")]
        public HttpResponseMessage Pending(int userId)
        {
            return PendingList(userId, RoleName);
        }

        // POST api/cafeteria/approve/4/12
        [HttpPost, Route("approve/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Approve(int userId, int applicationId)
        {
            return ApproveStep(userId, applicationId, RoleName);
        }

        // POST api/cafeteria/reject/4/12     body: { "reason": "..." }
        [HttpPost, Route("reject/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Reject(int userId, int applicationId, RejectRequest request)
        {
            return RejectStep(userId, applicationId, RoleName,
                request == null ? null : request.Reason);
        }
    }
}