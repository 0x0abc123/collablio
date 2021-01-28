using System;
using System.Collections.Generic;
using collablio.Models;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace collablio.AppTasks
{
	//curl -v  -F 'filedata=@/mnt/d/Archive/vmshare/proj/python/autorecon-working/testfiles/nmap-svsc-test2.xml' -F 'id=test1234' -F 'type=file_upload' -F '_p={"itemid":"0xNN"}' http://10.3.3.60:5000/apptask
	class FileUpload
	{		
		//private static string NMAP = "nmap";
		
		public static async Task Run(AppTask apptask)
		{
			//check if there's actually a file and it's not empty:
			if(apptask.fileMemStream == null || apptask.fileMemStream.Length < 1)
			{
				LogService.Log(LOGLEVEL.ERROR,"FileUpload: file length is 0");
				return;
			}
			//**also check what the parent ID is, we need it to complete the task
			if(apptask.param == null || !apptask.param.ContainsKey(AppTask.PARENTID) || apptask.param[AppTask.PARENTID].Length < 1)
			{
				LogService.Log(LOGLEVEL.ERROR,"FileUpload: no PARENTID was provided");
				return;
			}
			
			DatabaseManager dbmgr = DatabaseManager.Instance();

			string parentID = apptask.param[AppTask.PARENTID];
			
			//attachment ID is optional -> if supplied then this operation is an upsert
			string attachID = Helpers.GetValueOrBlank("attachid",apptask.param);
			
			if (apptask.fileMemStream.Position > 0)
				apptask.fileMemStream.Position = 0;

			Node a = new Node();
			a.UID = attachID;
			a.Label = apptask.filedata.FileName; //filename|title
			a.Detail = apptask.filedata.ContentType.ToLower();
			a.Type = a.Detail.StartsWith("image/") ? AppTask.TYPE_IMAGE : AppTask.TYPE_FILE; //tool
			a.AddParent(parentID);

			bool error = false;
			string message = "";
			try
			{
				//check incoming mime-type
				if(!Helpers.IsTextContentType(a.Detail))
				{
					//convert to base64
					a.SetBase64FromBytes(apptask.fileMemStream.ToArray());
				}
				else
				{	// using reader???
					StreamReader reader = new StreamReader( apptask.fileMemStream );
					a.TextData = reader.ReadToEnd();
				}
				//create node
				List<string> uidsInserted = await dbmgr.UpsertNodesAsync(new List<Node> { a });
				error = !(uidsInserted?.Count > 0);
				message = (error) ? "upsert failed" : uidsInserted[0];
			}
			catch (Exception e)
			{
				error = true;
				message = "upsert failed";
				LogService.Log(LOGLEVEL.ERROR,"FileUpload: error reading/converting file upload - "+e.ToString());
			}

			apptask.NotifyComplete(message);
			return;
		}
	}
}