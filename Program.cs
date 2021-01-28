using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;

namespace collablio
{
    class Program
    {
        static void Main(string[] args)
        {
			Action actionConsoleRead = () =>
			{
				string input = "";
				do
				{
					input = Console.ReadLine();
					string formattedInput = String.Format(
						"Input={0}, Task={1}, Thread={2}",
						input, Task.CurrentId, Thread.CurrentThread.ManagedThreadId
						);
					LogService.Log(LOGLEVEL.DEBUG,formattedInput);
				}
				while (input != "stop");

				LogService.Log(LOGLEVEL.DEBUG,"stopping...");
			};
			Task t2 = Task.Factory.StartNew(actionConsoleRead);

			//keep this for posterity
			/*
			Action actionMainLoopAsync = async () =>
			{
				while (true)
				{
					Message dequeuedMessage = await mainThreadMessageQueue.DequeueAsync();

					//debug stuff:
					string displayedMessage = String.Format("Got Message: {0}", dequeuedMessage.content);
					LogService.Log(LOGLEVEL.DEBUG,displayedMessage);

					//new way (main loop determines what should happen with threading/tasks)
					Task t;
					if(dequeuedMessage is ModelMessage)
						t = Task.Factory.StartNew(() => modelManager.HandleRequest((ModelMessage)dequeuedMessage));

					// the original way (let *Manager create and manage its own threads):
					//if(dequeuedMessage.content == "statemanager")
					//	stateManager.HandleRequest(dequeuedMessage);
				}
			};
			Task t1 = Task.Factory.StartNew(actionMainLoopAsync);
			*/
			
			// start up the Kestrel web service
			string webRoot = Path.Combine(AppContext.BaseDirectory, "../../../wwwroot");
			LogService.Log(LOGLEVEL.DEBUG,"webroot: "+webRoot);

			var host = new WebHostBuilder()
                .UseKestrel()
				.UseWebRoot(webRoot)
                .UseUrls("http://*:5000")
                .UseStartup<Startup>()
                .Build();

            host.Run();

			/*
			//if we want to shutdown in a controlled manner...
			if (t2.Status >= TaskStatus.RanToCompletion)
				break;
			*/
			
        }
    }
}
