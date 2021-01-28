using System;
using collablio.Models;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace collablio
{
	public enum LOGLEVEL {
		DEBUG = 0,
		INFO = 1,
		WARN = 2,
		ERROR = 3
	}
	
    public class LogService
    {
		public static void Log(LOGLEVEL level, string message)
		{
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
