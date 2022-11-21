using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using collablio.Models;
using collablio.Controllers;

namespace collablio
{
	
    public class Startup
    {

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
			//services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
			services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)    
			.AddJwtBearer(options =>
			{
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = true,
					ValidIssuer = AuthController.JWT_ISSUER,//Configuration["Jwt:Issuer"],
					ValidAudience = AuthController.JWT_AUDIENCE,//Configuration["Jwt:Issuer"],
					IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthController.GetJWTSecretKey()))
				};
				//options.SecurityTokenValidators.Add(new CustomJwtSecurityTokenHandler());
			});
			services.AddMvc()
				.AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = new NodeJsonNamingPolicy());
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
			app.UseStaticFiles();
			app.UseAuthentication();
			app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }


/*
public class CustomJwtSecurityTokenHandler : ISecurityTokenValidator
{
    private int _maxTokenSizeInBytes = TokenValidationParameters.DefaultMaximumTokenSizeInBytes;
    private JwtSecurityTokenHandler _tokenHandler;

	private readonly IHttpContextAccessor _httpContextAccessor;

	public CustomJwtSecurityTokenHandler(IHttpContextAccessor httpContextAccessor)
	{
		_httpContextAccessor = httpContextAccessor;
		_tokenHandler = new JwtSecurityTokenHandler();
	}
    
    public bool CanValidateToken
    {
        get
        {
            return true;
        }
    }

    public int MaximumTokenSizeInBytes
    {
        get
        {
            return _maxTokenSizeInBytes;
        }

        set
        {
            _maxTokenSizeInBytes = value;
        }
    }

    public bool CanReadToken(string securityToken)
    {
        return _tokenHandler.CanReadToken(securityToken);            
    }

    public ClaimsPrincipal ValidateToken(string securityToken, TokenValidationParameters validationParameters, out SecurityToken validatedToken)
    {
        //How to access HttpContext/IP address from here?  httpContextAccessor
		var httpContext = _httpContextAccessor.HttpContext;

        var principal = _tokenHandler.ValidateToken(securityToken, validationParameters, out validatedToken);

        return principal;
    }
}
*/



}