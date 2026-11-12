
using DCSBackend.Model;
using DCSBackend.Models;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    [RoutePrefix("api/auth")]
    public class AuthController : BaseController
    {
   

        [HttpPost, Route("login")]
        public HttpResponseMessage Login(LoginRequest request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.Username) ||
                    string.IsNullOrEmpty(request.Password))
                {
                    return Fail("Username and password are required.");
                }

                string username = request.Username.Trim();

                var user = db.UserLogins
                    .Where(u => u.Username == username && u.IsActive)
                    .Select(u => new
                    {
                        u.LoginId,
                        u.PasswordHash,
                        u.FirstName,
                        u.LastName,
                        u.Reg_no,
                        RoleName = u.tbl_User_Roles.role_name
                    })
                    .FirstOrDefault();

                // DEV: Passwords are currently stored as plain text.
                // Replace this comparison when password hashing is implemented.

                if (user == null || user.PasswordHash != request.Password)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.Unauthorized,
                        new { message = "Invalid username or password." }
                    );
                }

                var response = new LoginResponse
                {
                    UserId = user.LoginId,
                    Role = user.RoleName,
                    FullName = user.FirstName ,
                    //+ user.LastName,
                    RegNo = user.Reg_no
                };

                return Success(response);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }

        // GET api/auth/ping
        [HttpGet, Route("ping")]
        public HttpResponseMessage Ping()
        {
            return Request.CreateResponse(HttpStatusCode.OK, "alive");
        }
    }
}
