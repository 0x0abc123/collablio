using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using collablio.Models;

namespace collablio
{
	
    public class Startup
    {

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
			services.AddMvc()
				.AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = new NodeJsonNamingPolicy());
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
			app.UseStaticFiles();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}