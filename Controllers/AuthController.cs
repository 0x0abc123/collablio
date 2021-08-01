using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
//using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Linq;
using System.Collections.Generic;
using collablio.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;    
using Microsoft.IdentityModel.Tokens;    
using System.IdentityModel.Tokens.Jwt;    
//using Microsoft.IdentityModel.Tokens.Jwt;    
using System.Security.Claims;    
   
namespace collablio.Controllers    
{
    [ApiController]
    public class AuthController : Controller
    {
        //private IConfiguration _config;

		public static readonly string JWT_AUDIENCE = "collablio";
		public static readonly string JWT_ISSUER = "collablio";
		public static readonly int SESSION_TIMEOUT = 120;
		
		private static string JWT_SECRET_KEY = Helpers.GetUniqueKey(20);
		//private readonly IHttpContextAccessor _httpContextAccessor;

		private static DatabaseManager dbmgr = DatabaseManager.Instance();
		private static AntiBruteForceManager abfmgr = new AntiBruteForceManager();
		
        //public AuthController(IHttpContextAccessor httpContextAccessor)
        public AuthController()
        {
            //_config = config;
			//JWT_SECRET_KEY = Helpers.GetUniqueKey(20);
			//_httpContextAccessor = httpContextAccessor;
        }

		public class UserLoginData
		{
			public string username {get; set;}
			public string password {get; set;}
		}

		public static string GetJWTSecretKey() {
			return JWT_SECRET_KEY;
		}
		
        [AllowAnonymous]
        [HttpPost]
		[Route("login")]
        public async Task<IActionResult> Login(UserLoginData userData)
        {
			return await _Login(userData);
        }


        private async Task<IActionResult> _Login(UserLoginData userData)
        {
            IActionResult response = Unauthorized();

			LogService.Log(LOGLEVEL.DEBUG,"AuthController: URI - "+Request.Host+", port="+Request.HttpContext.Connection.LocalPort); //Request.Host can be spoofed
			
            var user = await AuthenticateUser(userData.username, userData.password);

            if (user != null)
            {
                var tokenString = GenerateJSONWebToken(user, SESSION_TIMEOUT);
                response = Ok(new { token = tokenString });
            }

            return response;
        }


        [AllowAnonymous]
        [HttpPost]
		[Route("service/{actionstr}")]
        //public async Task<IActionResult> LocalServiceEndpoint(string actionstr)
		public IActionResult LocalServiceEndpoint(string actionstr)
        {
            IActionResult response = Unauthorized();
			LogService.Log(LOGLEVEL.DEBUG,"AuthController: LocalServiceEndpoint "+actionstr+", port="+Request.HttpContext.Connection.LocalPort); 

			if (actionstr == "gettemptoken")
			{
				var localport = Request.HttpContext.Connection.LocalPort.ToString(); //Request.Host header can be spoofed
				if (localport == "5001")
				{
					//var user = await AuthenticateUser(userData.username, userData.password);
					//if (user != null)
					//{
					var tokenString = GenerateJSONWebToken("TODO_CHANGE_THIS", 1);
					response = Ok(new { token = tokenString });
					//}
				}
			}
            return response;
        }

        [Authorize]
		[Route("downloadtoken/{attachmentUid}")]
        public IActionResult IssueTemporaryAuthToken(string attachmentUid)
        {
            IActionResult response = Unauthorized();
			/*
			var token = await HttpContext.GetTokenAsync("access_token");
			var handler = new JwtSecurityTokenHandler();
			var jsonToken = handler.ReadToken(token);
			var tokenS = jsonToken as JwtSecurityToken;
			var username = tokenS.Claims.FirstOrDefault(claim => claim.Type == "username").Value;
			
			
			//This was the best option
			//using System.Linq;
			var username = "";
			var claims = HttpContext.User.Claims;
			var userclaim = claims.FirstOrDefault( c => c.Type == "username");
			username = userclaim.Value;
			*/
			/*
			foreach (Claim c in claims)
				if (c.Type == "username")
					username = c.Value;
			*/
			//var username = HttpContext.User.Claims.First( c => c.Type == "username").Value;
			
			//var tokenString = GenerateJSONWebToken(username,1);

			var exp = (Helpers.DateTimeToUnixEpoch(DateTime.Now.ToUniversalTime().AddMinutes(1))).ToString("F0");
			var non = Helpers.GetUniqueKey(16);
			var signatureString = Helpers.GetB64EncodedHS256FromString($"{attachmentUid}_{exp}_{non}");
			response = Ok(new { sig = signatureString, uid = attachmentUid, exp = exp, non = non });
            return response;
        }
    
        private string GenerateJSONWebToken(string user, int timeout)
        {    
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetJWTSecretKey()));    
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var claims = new List<Claim> { new Claim("username",user) };
			var token = new JwtSecurityToken(JWT_ISSUER,    
              JWT_AUDIENCE,    
              claims,    
              expires: DateTime.Now.AddMinutes(timeout),    
              signingCredentials: credentials);    
    
            return new JwtSecurityTokenHandler().WriteToken(token);    
        }    
    
        private async Task<string> AuthenticateUser(string username, string password)    
        {    
            //UserModel user = null;    
			string user = null;

			if(abfmgr.IsBlocked(username))
			{
				LogService.Log(LOGLEVEL.DEBUG,"AuthController: AuthenticateUser "+username+" is blocked from login"); 
			}
			else
			{
				User dbuser = await dbmgr.QueryUserAsync(username);
				if (dbuser != null)
				{
					//login success
					if(PasswordHashing.Check(dbuser.password, password))
					{    
						user = dbuser.username;
						abfmgr.HandleLoginSuccess(user);
					}
					//login failed
					else
					{
						abfmgr.HandleLoginFailed(username);
						LogService.Log(LOGLEVEL.DEBUG,"AuthController: AuthenticateUser "+username+" login failed"); 
					}
				}
			}
            return user;    
        }

		private class AntiBruteForceData {
			public int failedlogincount {get; set;} = 0;
			public DateTime lastloginattempt {get; set;}
			public int GetBackoffMinutes() { return -5 + failedlogincount; } 
			public bool IsBlocked() {
				LogService.Log(LOGLEVEL.DEBUG,"AuthController: AuthenticateUser block until "+lastloginattempt.AddMinutes(GetBackoffMinutes()).ToString());
				return (DateTime.Compare(lastloginattempt.AddMinutes(GetBackoffMinutes()), DateTime.Now.ToUniversalTime()) > 0 ); }
		}

		private class AntiBruteForceManager {

			private Dictionary<string,AntiBruteForceData> _failedlogindata = new Dictionary<string,AntiBruteForceData>();
			
			public bool IsBlocked(string username)
			{
				return (_failedlogindata.ContainsKey(username) && _failedlogindata[username].IsBlocked());
			}

			public void HandleLoginSuccess(string username)
			{
				if (_failedlogindata.ContainsKey(username))
					_failedlogindata.Remove(username);
			}

			public void HandleLoginFailed(string username)
			{
				AntiBruteForceData userfailedlogindata = null; 
				if (_failedlogindata.ContainsKey(username))
					userfailedlogindata = _failedlogindata[username];
				else
				{
					LogService.Log(LOGLEVEL.DEBUG,$"AuthController: AuthenticateUser handleLoginFailed create new userfailedlogindata for {username}");
					userfailedlogindata = new AntiBruteForceData();
					_failedlogindata[username] = userfailedlogindata;
				}
				userfailedlogindata.failedlogincount += 1;
				userfailedlogindata.lastloginattempt = DateTime.Now.ToUniversalTime();
				LogService.Log(LOGLEVEL.DEBUG,$"AuthController: AuthenticateUser handleLoginFailed {userfailedlogindata.lastloginattempt} {userfailedlogindata.failedlogincount}");
			}
		}
    }    
} 