using DCSBackend.Model;
using DCSBackend.Models;   // change "DCSApi" to your project's name
using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DCSBackend.Controllers
{
    [RoutePrefix("api/auth")]
    public class AuthController : BaseController
    {
        // POST api/auth/login   body: { "username": "...", "password": "..." }
        // The frontend keeps the returned userId and sends it with every request.
        [HttpPost, Route("login")]
        public HttpResponseMessage Login(LoginRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
                    return Fail("Username and password are required.");

                string username = request.Username.Trim();

                var user = db.Users
                    .Where(u => u.Username == username && u.IsActive)
                    .Select(u => new
                    {
                        u.UserId,
                        u.PasswordHash,
                        u.FullName,
                        u.RegNo,
                        RoleName = u.Role.RoleName
                    })
                    .FirstOrDefault();

                // DEV: passwords are plain text in the PasswordHash column.
                // LATER (when you hash them): replace this comparison with a hash check.
                if (user == null || user.PasswordHash != request.Password)
                    return Request.CreateResponse(HttpStatusCode.Unauthorized,
                        new { message = "Invalid username or password." });

                var response = new LoginResponse
                {
                    UserId = user.UserId,
                    Role = user.RoleName,
                    FullName = user.FullName,
                    RegNo = user.RegNo
                };

                return Success(response);
            }
            catch (Exception ex)
            {
                return ServerError(ex);
            }
        }
    }
}