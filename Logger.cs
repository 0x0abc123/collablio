using System;
using collablio.Models;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace collablio
{
	public enum LOGLEVEL {
		DEBUG = 1,
		INFO = 2,
		WARN = 3,
		ERROR = 4
	}
	
    public class LogService
    {
		private static ConfigManager confmgr = ConfigManager.Instance();
		static int loglevelconfig = Int32.Parse(confmgr.GetValue("loglevel"));
		
		public static void Log(LOGLEVEL level, string message)
		{
			if(loglevelconfig > (int)level)
				return;

			List<string> lookup = new List<string> {
			"debug",
			"info",
			"warning",
			"error"
			};
			Console.WriteLine("\n\n[{0}] - {1}\n*************************\n{2}\n",
				lookup[(int)level], DateTime.Now.ToString(), message);
		}
	}
}
