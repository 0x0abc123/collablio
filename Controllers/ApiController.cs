using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using collablio.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;    

namespace collablio.Controllers
{
	
	[ApiController]
    public class ApiController : ControllerBase
    {		
		private static DatabaseManager dbmgr = DatabaseManager.Instance();
		private static AppTaskManager atmgr = AppTaskManager.Instance();
		
		private static JsonSerializerOptions JsonOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = new NodeJsonNamingPolicy(),
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			IgnoreReadOnlyProperties = false
		};
		
		private static string JsonSerialize(object obj)
		{
			return JsonSerializer.Serialize(obj,JsonOptions);
		}
		
        [Route("isalive")]
        public IActionResult CheckIsAlive()
        {
            return Ok("true");
        }

		[Authorize]
		[HttpGet]
        [Route("checkauth")]
        public IActionResult CheckIsAuthenticated()
        {
            return Ok("true");
        }

        private async Task<string> _QueryNodesAsync(List<string>? uids, string? field, string? op, string? val, int depth, string? type, bool includeBody = false)
        {
			ServerResponse r = new ServerResponse();
			try {
				r = await dbmgr.QueryAsync(uids, field, op, val, depth, type, includeBody);
			} catch (Exception e) {
				LogService.Log(LOGLEVEL.ERROR,e.ToString());
			}
			return JsonSerialize(r);
        }


		[Authorize]
		[HttpGet]
        [Route("nodes")]
        public async Task<IActionResult> QueryNodesGet(string? uid = null, string? field=null, string? op=null, string? val=null, int depth = 0, string? type = null, bool body = false)
        {
			List<string>? uids = (uid != null) ? new List<string> {uid} : null;
			return Ok(await _QueryNodesAsync(uids, field, op, val, depth, type, body));
        }

		[Authorize]
		[HttpGet]
        [Route("attachment/{attachmentUID}/{timestamp}")]
        public async Task<IActionResult> QueryAttachmentGet(string attachmentUID, long timestamp)
        {
			List<string> uids = new List<string> {attachmentUID};
			return Ok(await _QueryNodesAsync(uids, PropsJson.LastModTime, DatabaseManager.OP_GT, $"{timestamp}", 1, null, true));
        }

		public class QueryNodesPostData
		{
			public List<string>? uids {get; set;} = new List<string>();
			public string? field {get; set;} = null;
			public string? op {get; set;} = null;
			public string? val {get; set;} = null;
			public int depth {get; set;} = 0;
			public string? type {get; set;} = null;
			public bool body {get; set;} = false;
		}
		
		//curl "http://10.3.3.60:5000/nodes" -H 'Content-Type: application/json' -d '{"uids":["0x2","0x3"],"depth":1}'
        [Authorize]
		[HttpPost]
        [Route("nodes")]
        public async Task<IActionResult> QueryNodesPost(QueryNodesPostData postData)
        {
			return Ok(await _QueryNodesAsync(postData.uids, postData.field, postData.op, postData.val, postData.depth, postData.type, postData.body));
        }

        [Authorize]
		[HttpPost]
        [Route("query")]
        public async Task<IActionResult> QueryWithOptsNodesPost(QueryOptions queryData)
        {
			ServerResponse r = new ServerResponse();
			try {
				r = await dbmgr.QueryWithOptionsAsync(queryData);
			} catch (Exception e) {
				LogService.Log(LOGLEVEL.ERROR,e.ToString());
			}
			return Ok(JsonSerialize(r));
        }

		//upsertNode
		//curl "http://10.3.3.60:5000/upsert" -H 'Content-Type: application/json' -d '[{"l":"Test1","d":"klf jlkj","x":"customdata a","ty":"blah","b":"blah","in":[{"uid":"0x2"}]}]'
        [Authorize]
		[HttpPost]
        [Route("upsert")]
        public async Task<IActionResult> UpsertNodesPost(List<Node> nodeList)
        {
			//the upsertNode route should nullify the B64Data field (it should only be set via the file upload route PostUploadFile
			foreach (Node n in nodeList)
				n.B64Data = null;
			List<string> uids = await dbmgr.UpsertNodesAsync(nodeList);
			return Ok(JsonSerialize(uids));
        }

        [Authorize]
		[HttpPost]
        [Route("upsert/dcg")]
        public async Task<IActionResult> UpsertNodesPostDCG(List<Node> nodeList)
        {
			//the upsertNode route should nullify the B64Data field (it should only be set via the file upload route PostUploadFile
			foreach (Node n in nodeList)
				n.B64Data = null;
			List<string> uids = await dbmgr.UpsertNodesAsync(nodeList, true);
			return Ok(JsonSerialize(uids));
        }

		public class LinkNodesPostData
		{
			public List<string>? nodes {get; set;}
			public List<string>? incoming {get; set;}
			public List<string>? outgoing {get; set;}
		}

        [Authorize]
		[HttpPost]
        [Route("link")]
        public async Task<IActionResult> LinkNodesPost(LinkNodesPostData postData)
        {
			var response = await dbmgr.AddLinkRelationsAsync(postData.nodes, postData.incoming, postData.outgoing);
			return Ok(JsonSerialize(response));
        }

        [Authorize]
		[HttpPost]
        [Route("unlink")]
        public async Task<IActionResult> UnlinkNodesPost(LinkNodesPostData postData)
        {
			var response = await dbmgr.RemoveLinkRelationsAsync(postData.nodes, postData.incoming, postData.outgoing);
			return Ok(JsonSerialize(response));
        }

		public class MoveNodesPostData
		{
			public List<string>? nodes {get; set;}
			public List<string>? parents {get; set;}
			public List<string>? children {get; set;}
			public string newparent {get; set;}
		}


		//delete should be implemented by creating an apptask to move nodes to recyclebin folder
        [Authorize]
		[HttpPost]
        [Route("move")]
        public async Task<IActionResult> MoveNodesPost(MoveNodesPostData postData)
        {
			var response = await dbmgr.RemoveParentChildRelationsAsync(postData.nodes, postData.parents, postData.children);
			if(!response.error)
				response = await dbmgr.AddParentChildRelationsAsync(postData.nodes, new List<string> {postData.newparent});

			return Ok(JsonSerialize(response));				
        }
		
		
		
		// GetAttachmentAsDownload
		// verify signed download URL by checking parameters generated by AuthController.IssueTemporaryAuthToken()
        [AllowAnonymous]
		[HttpGet]
        [Route("download/{attachmentUid}")]
        public async Task<IActionResult> GetAttachmentDownload(string attachmentUid, string sig, string exp, string non)
        {
			var signatureString = Helpers.GetB64EncodedHS256FromString($"{attachmentUid}_{exp}_{non}");
			if (signatureString != sig || DateTime.Compare(Helpers.UnixEpochToDateTime(Double.Parse(exp)), DateTime.Now.ToUniversalTime()) < 0 ) {
				var curTime = Helpers.DateTimeToUnixEpoch(DateTime.Now.ToUniversalTime());
				var dbginfo = $"sig={sig}, signatureStr={signatureString}, exp={exp}, curTime={curTime}, non={non}";
				LogService.Log(LOGLEVEL.DEBUG,"ApiController: download attachment - "+dbginfo);
				return Unauthorized();
			}
				
			return await GetDownloadWithAuth(attachmentUid);
        }		
		

		// GetDownloadWithAuth
		// if attachment mimetype is not plaintext and DataField is base64, then decode it
		//    the check is just if the DataField startswith "b64:"
		// byte[] newBytes = Convert.FromBase64String(s);
		// otherwise send as is
		// set content type for response
		// set headers:  attachment
		//https://stackoverflow.com/questions/42460198/return-file-in-asp-net-core-web-api
        [Authorize]
		[HttpGet]
        [Route("authddownload/{attachmentUid}")]
        public async Task<IActionResult> GetDownloadWithAuth(string attachmentUid)
        {
			try
			{
				ServerResponse r = await dbmgr.QueryAsync(
					uidsOfParentNodes: new List<string> {attachmentUid}, 
					includeBody: true
					);
				Node n = r.nodes[0];
			
				if((n.B64Data == null || n.B64Data == "") && (n.TextData == null || n.TextData == ""))
					return NotFound();

				string contentType = n.Detail;
				LogService.Log(LOGLEVEL.DEBUG,"ApiController: download: "+n.Label+" contenttype: "+contentType);						
				
				bool isTextFile = Helpers.IsTextContentType(contentType);
				
				Byte[] bytes = isTextFile ? Encoding.ASCII.GetBytes(n.TextData) : n.GetBase64AsBytes();
				
				System.Net.Mime.ContentDisposition cd = new System.Net.Mime.ContentDisposition
				{
					  FileName = n.Label,
					  Inline = (isTextFile || Helpers.IsImageContentType(contentType))  
					  // false = prompt the user for downloading;  true = browser to try to show the file inline
				};
				Response.Headers.Add("Content-Disposition", cd.ToString());
				Response.Headers.Add("X-Content-Type-Options", "nosniff");
				
				return File(new MemoryStream(bytes), contentType);
			}
			catch (Exception e)
			{
				LogService.Log(LOGLEVEL.DEBUG,"ApiController: could not create file stream - "+e.ToString());
				return NotFound();
			}
        }		
		

		private readonly int UPLOAD_MAX_SIZE = 15728640; //15MB, need to set this in a config instead of hardcoding it

        private async Task<string> HandleAppTask(AppTask task)
        {
			string result = "";
			AppTask task2 = new AppTask();
			if(task.filedata.Length > 0)
			{
				if(task.filedata.Length < UPLOAD_MAX_SIZE)
				{
					task2.fileMemStream = new MemoryStream();
					task.filedata.CopyTo(task2.fileMemStream);
					task2.fileMemStream.Position = 0;
					task2.filedata = task.filedata;
				}
				else
					return String.Format("File length {0} is not allowed (max length is {1})",task.filedata.Length,UPLOAD_MAX_SIZE);
			}
			task2.id = task.id;
			task2.type = task.type;
			task2.synchronous = task.synchronous;
			task2.param = JsonSerializer.Deserialize<Dictionary<string,string>>(task._p);
			result = atmgr.RunAppTask(task2);

			if(task2.synchronous)
				return await task2.WaitForComplete();

            return result;
        }

        [Authorize]
		[HttpPost]
        [Route("apptask")]
        public async Task<IActionResult> PostAppTask([FromForm] AppTask task)
        {
			return Ok(await HandleAppTask(task));
        }

        [Authorize]
		[HttpPost]
        [Route("upload")]
        public async Task<IActionResult> PostUploadFile([FromForm] AppTask task)
        {
			task.synchronous = true;
			return Ok(await HandleAppTask(task));
        }

    }
}