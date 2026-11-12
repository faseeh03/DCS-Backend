using DCSBackend.Model;
using DCSBackend.Models;  
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    
    [RoutePrefix("api/supervisor")]
    public class SupervisorController : BaseController
    {
        private const string RoleName = "Supervisor";

        // GET api/supervisor/pending/
        [HttpGet, Route("pending/{userId:int}")]
        public HttpResponseMessage Pending(int userId)
        {
            return PendingList(userId, RoleName);
        }

        // POST api/supervisor/approve/
        [HttpPost, Route("approve/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Approve(int userId, int applicationId)
        {
            return ApproveStep(userId, applicationId, RoleName);
        }

        // POST api/supervisor/reject/
        [HttpPost, Route("reject/{userId:int}/{applicationId:int}")]
        public HttpResponseMessage Reject(int userId, int applicationId, RejectRequest request)
        {
            return RejectStep(userId, applicationId, RoleName,
                request == null ? null : request.Reason);
        }
    }
}