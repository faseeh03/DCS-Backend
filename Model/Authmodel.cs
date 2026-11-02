using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DCSBackend.Model
{
    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class LoginResponse
    {
        public int UserId { get; set; }
        public string Role { get; set; }
        public string FullName { get; set; }
        public string RegNo { get; set; }
    }
}