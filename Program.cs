using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace collablio
{
    class Program
    {
		private static ConfigManager confmgr = ConfigManager.Instance();
        static void Main(string[] args)
        {			
			// start up the Kestrel web service
			string webRoot = Path.Combine(AppContext.BaseDirectory, "../../../wwwroot");
			LogService.Log(LOGLEVEL.DEBUG,"webroot: "+webRoot);

			var host = new WebHostBuilder()
                .UseKestrel()
				.ConfigureLogging(logging =>
				{
					logging.ClearProviders();
					logging.AddConsole();
				})
				.UseWebRoot(webRoot)
                .UseUrls(confmgr.GetValue("listenurl"))
                .UseStartup<Startup>()
                .Build();

            host.Run();			
        }
    }
}
