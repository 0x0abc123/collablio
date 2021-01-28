using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace collablio.Models
{
    public class AppTask
    {
		public readonly static string PARENTID = "parentid";
		public readonly static string ROOTID = "rootid";
		public readonly static string TYPE_FILE = "File";
		public readonly static string TYPE_IMAGE = "Image";
		public readonly static string TYPE_TEXT = "Text";
		public readonly static string TYPE_ANNOT = "Annotation";
		public readonly static string TYPE_CAT = "Folder";
		public readonly static string TYPE_EDITABLE = "Note";
		public readonly static string TYPE_HOST = "Host";
		public readonly static string TYPE_PORT = "Port";

		public string id { get; set; }
		public string type { get; set; }
		public bool synchronous {get; set;}
		public Dictionary<string,string> param { get; set; } // custom parameters
		public DateTime created { get; set; } // timestamp
		public DateTime finished { get; set; } // timestamp
		public string status { get; set; } // task status
		
		//this is a hack to get param dictionary sent via multipart/form-data
		//_p should be a serialised JSON object
		public string _p { get; set; } // 

		[JsonIgnore] //to prevent storage in database and/or sending back to client
		public IFormFile filedata { get; set; }
		//IFormFile properties/methods:
		//	ContentType
		//	FileName
		//	Length
		//  public System.IO.Stream OpenReadStream ();
		
		[JsonIgnore] //need to copy filedata into this, because it has been disposed once the apptask thread accesses it
		//**also need to deal with closing/disposing of the stream
		public MemoryStream fileMemStream { get; set; }
		
		[JsonIgnore]
		public MessageQueue msgQueue {get;}
		
		public AppTask()
		{
			msgQueue = new MessageQueue();
			synchronous = false;
		}
		
		public async Task<string> WaitForComplete()
		{
			Message m = await msgQueue.DequeueAsync();
			return m.content;
		}
		
		public void NotifyComplete(string message = "done")
		{
			msgQueue.Enqueue(message);
		}

	}
}
