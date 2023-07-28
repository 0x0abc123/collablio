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
            services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
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

}