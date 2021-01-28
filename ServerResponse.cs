using System;
using System.Collections.Generic;
using collablio.Models;

namespace collablio
{
	public class ServerResponse
	{
		public List<Node> nodes {get; set;}
		public List<string> delete { get; set; }
		public double timestamp { get; set; }		
		public string message {get; set;}
		public bool error {get; set;} = false;
		
		public void setTimestamp() { timestamp = Helpers.DateTimeToUnixEpoch(DateTime.UtcNow); }
	}
}