using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Dgraph;
using Dgraph.Transactions;
using FluentResults;
using Grpc.Core;
using Grpc.Net.Client;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using collablio.Models;

namespace collablio
{

	public class QueryResultNodes
	{
		public List<Node> qr { get; set; }
	}

	//////////////////////////////////////////////////////
	//  to protect the root node from being modified
	//  we put the uniqueKey in the "ro" field that is in the "N" schema but not serialised/deserialised using the Node Class
	//////////////////////////////////////////////////////

	// allows accessing the ro field that stores the root node unique label
	class RootNode : Node
	{
		public string ro { get; set; }
	}

	class QueryResultRootNodes
	{
		public List<RootNode> qr { get; set; }
	}
	
    class DatabaseManager
    {
		public static readonly int MAX_RECURSE_DEPTH = 10;
		private string ROOT_NODE_LABEL = "Fe9yqjNp0wWEmhW260qA";
		private string ROOTNODE_UID = "";

		public static readonly string OP_EQ = "eq";
		public static readonly string OP_GT = "gt";
		public static readonly string OP_LT = "lt";
		public static readonly string OP_GTE = "gte";
		public static readonly string OP_LTE = "lte";
		public static readonly string OP_TEXTSEARCH = "allofterms";
		
		private static DatabaseManager _singleton = null;
		public static DatabaseManager Instance()
		{
			if(_singleton == null)
			{
				_singleton = new DatabaseManager();
				Task.Run(() => _singleton.Initialise()).Wait();
			}
			return _singleton;			
		}
		
		private DgraphClient _dbclient;

		//ensure that schemasetup.sh has already been run to setup the database

		private static JsonSerializerOptions jsonSerialiseOptions;
		private static JsonSerializerOptions jsonSerialiseIgnoreNull;
		private static JsonSerializerOptions jsonDeserialiseOptions;

		public DatabaseManager()
		{
			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
			_dbclient = new DgraphClient(GrpcChannel.ForAddress("http://127.0.0.1:9080"));
	
			jsonSerialiseOptions = new JsonSerializerOptions
				{
					PropertyNamingPolicy = new NodeJsonNamingPolicy(),
					IgnoreNullValues = false,
					IgnoreReadOnlyProperties = false
				};
				
			jsonSerialiseIgnoreNull = new JsonSerializerOptions
					{
						PropertyNamingPolicy = new NodeJsonNamingPolicy(),
						IgnoreNullValues = true,
						IgnoreReadOnlyProperties = false
					};

			jsonDeserialiseOptions = new JsonSerializerOptions
					{
						PropertyNamingPolicy = new NodeJsonNamingPolicy(),
						IgnoreNullValues = true,
						IgnoreReadOnlyProperties = false
					};
		}

		private async Task Initialise()
		{
			string query = $"{{ qr(func: eq(ro,\"{ROOT_NODE_LABEL}\")){{uid}} }}";
			LogService.Log(LOGLEVEL.DEBUG,$"DBManager constructor Query {query}");

			var res = await _dbclient.NewReadOnlyTransaction().Query(query);

			if (res.IsFailed){
				LogService.Log(LOGLEVEL.DEBUG,$"DBManager constructor Query result isFailed res={res}");
				return;
			}

			LogService.Log(LOGLEVEL.DEBUG,$"DBManager constructor Query result {res}, {res.Value}, {res.Value.Json}, {jsonDeserialiseOptions.PropertyNamingPolicy}");
			QueryResultRootNodes queryResult = JsonSerializer.Deserialize<QueryResultRootNodes>(res.Value.Json, jsonDeserialiseOptions);

			if(queryResult.qr?.Count > 0)
			{
				ulong lowestRootnodeUID = Int64.MaxValue;
				foreach (RootNode rn in queryResult.qr)
				{
					ulong rnUID = Helpers.UIDToUlong(rn.UID);
					if(rnUID < lowestRootnodeUID)
					{
						ROOTNODE_UID = rn.UID;
						lowestRootnodeUID = rnUID;
					}
				}
				LogService.Log(LOGLEVEL.DEBUG,"DBManager Constructor result: found ROOTNODE_UID="+ROOTNODE_UID+" JSON: "+JsonSerializer.Serialize(queryResult,jsonSerialiseOptions));
			}
			else
			{
				RootNode rn = new RootNode {UID = "_:tmpkey", Type = "__ROOT__", ro = ROOT_NODE_LABEL };
				var result = await DoTransaction(TX_SET, rn, jsonSerialiseIgnoreNull);
				if(result.IsFailed)
				{
					LogService.Log(LOGLEVEL.DEBUG,$"DBManager DoTx failed, result: {result}");
				}
				else
				{
					ROOTNODE_UID = result.Value.Uids[rn.UID.Substring(2)];
					LogService.Log(LOGLEVEL.DEBUG,"DBManager Constructor result: ROOTNODE_UID="+ROOTNODE_UID+", "+result.ToString());
				}
			}
		}

		public async Task<ServerResponse> QueryAsync(
			List<string> uidsOfParentNodes = null, 
			string field = null, 
			string op = null, 
			string val = null, 
			int recurseDepth = 0,  //in dgraph recurse level 0 means unlimited but here we interpret it as no recurse
			string nodeType = null,
			bool includeBody = false,
//-----
			bool upwardsRecurse = false
//-----
			)
		{
			
			field = field ?? PropsJson.LastModTime;
			op = op ?? OP_GT;
			val = val ?? "0";
			recurseDepth = (recurseDepth > MAX_RECURSE_DEPTH) ? MAX_RECURSE_DEPTH : ((recurseDepth < 0) ? 0 : recurseDepth);
			
			//dgraph expects a string of uids like this: "[0x1, 0x2, 0x3]"
			//JsonSerializer.Serialize(uidsOfParentNodes) will quote each so the string is "[\"0x1\", \"0x2\", \"0x3\"]" 
			//this causes a parse error in dgraphQL
			// it seems to do its own validation but to be safe...
			uidsOfParentNodes = (uidsOfParentNodes?.Count > 0) ?  uidsOfParentNodes : new List<string>{ROOTNODE_UID};
			List<string> sanitisedParentNodeList = new List<string>();
			foreach (string s in uidsOfParentNodes)
				sanitisedParentNodeList.Add(Helpers.SanitiseUID(s));
			string serialisedParentNodeList = "["+String.Join(",",sanitisedParentNodeList)+"]";

			// ensure op is an allowed value
			op = op.ToLower();
			HashSet<string> allowedOps = new HashSet<string> {
				OP_EQ,OP_GT,OP_LT,OP_LTE,OP_GTE,OP_TEXTSEARCH
			};
			if(!allowedOps.Contains(op))
				op = OP_TEXTSEARCH;

			field = field.ToLower();
			HashSet<string> allowedFields = new HashSet<string> {
				PropsJson.LastModTime,
				PropsJson.EventTimestamp,
				PropsJson.WhoEditing,
				PropsJson.Label,
				PropsJson.Detail,
				PropsJson.CustomData,
				PropsJson.TextData,
				PropsJson.B64Data
			};
			if(!allowedFields.Contains(field))
				field = PropsJson.Label;
			//field = (op == "allofterms") ? "" : field + ",";

			//the timestamp values we received from the client should be unix epoch format, so convert to this format: 2006-01-02T15:04:05
			if(field == PropsJson.LastModTime || field == PropsJson.EventTimestamp)
				val = (Helpers.UnixEpochToDateTime(Convert.ToDouble(val))).ToString("o");

			string type1 = "";
			string type2 = "";

			var vars = new Dictionary<string,string> { 
				{ "$ids" , serialisedParentNodeList }, 
				{ "$val" , val } 
				};

			if((nodeType != null) && (nodeType != ""))
			{
				vars["$type"] = nodeType;
				type1 = ", $type: string";
				type2 = "and eq(ty,$type)";
			}

			var query = "";

			if(recurseDepth > 0)
			{
				vars["$rdepth"] = recurseDepth.ToString();
				query = @"query q($ids: string, $val: string, $rdepth: int __TYPE1__) {
					var(func: uid($ids)) @recurse(depth: $rdepth) 
					{
					  NID as uid
					  __DIRECTION__
					}
				  
					qr(func: uid(NID))";
			}
			else{
				query = @"query q($ids: string, $val: string __TYPE1__) {
					qr(func: uid($ids))";
			}
			query += @"  @filter(__OP__(__FIELD__,$val)__TYPE2__)
					{
						uid ty l d c m e g t __B64__ out {uid} in: ~out {uid}
					}
				}";

			query = query.Replace("__B64__",(includeBody ? "b x" : ""))
//-----
					.Replace("__DIRECTION__",(upwardsRecurse ? "~out" : "out"))
//-----
					.Replace("__OP__",op)
					.Replace("__FIELD__",field)
					.Replace("__TYPE1__",type1)
					.Replace("__TYPE2__",type2);
			

			var res = await _dbclient.NewReadOnlyTransaction().QueryWithVars(query, vars);
			LogService.Log(LOGLEVEL.DEBUG,$"DBManager QueryAsync result:\nQuery: {query}\nvars: ids='{serialisedParentNodeList}' val='{val}'\nRes: {res}");

			if (res.IsFailed){
				//LogService.Log(LOGLEVEL.DEBUG,"result isFailed");
				return null;
			}
			
			Console.Write(res.Value.Json);

			QueryResultNodes queryResult = JsonSerializer.Deserialize<QueryResultNodes>(res.Value.Json, jsonDeserialiseOptions);

			return CreateResponse(queryResult.qr);
		}


		private ServerResponse CreateResponse(List<Node> listOfNodesFromQueryResult)
		{
			ServerResponse returnVal = new ServerResponse();
			if (listOfNodesFromQueryResult?.Count > 0)
			{
				returnVal.nodes = listOfNodesFromQueryResult;
				returnVal.setTimestamp();
			}
			return returnVal;	
		}

		private static int TX_DELETE = 0;
		private static int TX_SET = 1;
		
		private async Task<Result<Response>> DoTransaction(int SetOrDelete, object ObjToSerialise, JsonSerializerOptions options)
		{
			using(var txn = _dbclient.NewTransaction()) {
				var json =  
					JsonSerializer.Serialize(ObjToSerialise, options);
				LogService.Log(LOGLEVEL.DEBUG,"DBManager DoTX Sending: "+json);
				var txnResult = (SetOrDelete == TX_DELETE) ? await txn.Mutate(deleteJson : json) : await txn.Mutate(setJson : json);

				if(!txnResult.IsFailed)
					await txn.Commit();
				return txnResult;
			}
		}


/*
according to this https://github.com/dgraph-io/dgraph.net/tree/7c12db6034a67d78edffa563c19f217936c4375d#running-a-mutation
you can run an upsert consisting of a query + mutation:

var query = @"
  query {
    user as var(func: eq(email, \"wrong_email@dgraph.io\"))
  }";

var mutation = new MutationBuilder{ SetNquads = "`uid(user) <email> \"correct_email@dgraph.io\" ." };

var request = new RequestBuilder{ Query = query, CommitNow = true }.withMutation(mutation);

// Upsert: If wrong_email found, update the existing data
// or else perform a new mutation.
await txn.Mutate(request);
*/
		private string MakeTempKeyFromString(string s, Dictionary<string,string> _tmpkeyToGuidMap)
		{
			string safeID = "";
			if(_tmpkeyToGuidMap.ContainsKey(s))
				safeID = _tmpkeyToGuidMap[s];
			else
			{
				safeID = Guid.NewGuid().ToString();
				_tmpkeyToGuidMap[s] = safeID;
			}

			return $"_:{safeID}";
		}
		
		//create nodes and optionally link to parent or children
		public async Task<List<string>> UpsertNodesAsync(List<Node> nodeList)//, List<string> parentUids = null, List<string> childUids = null) 
		{
			/*
			{ "set":[
			  {"uid":"_:tmp","t":"newtmp","d":"this is new","children":[{"uid":"0x124"},{"uid":"0x125"}]},
			  {"uid":"0x121","children":[{"uid":"_:tmp"}]},
			  {"uid":"0x123","children":[{"uid":"_:tmp"}]}
			  ] }	

			  because "set" operations ignore null values, can use
			  List<Node> with just uid and children->uid set and the rest null
			  **or can use JsonSerializerOptions with IgnoreNull set to true (jsonSerialiseIgnoreNull)
			*/
			//set tmpkeys for each node in list			
			//the nodes each contain their children (in "out") and parents (in "in")
			// leave "out" as is
			// turn "in"[] into edge objects, and then set "in" to null (so it is ignored during the upsert)
			//   create the "parent -> node edge" objs using empty Node (just set UID), 
			
			List<string> returnNewUIDs = new List<string>();

			Regex rgx = new Regex(@"^0x[0-9a-f]+$");
			
			//guid lookup table
			Dictionary<string,string> tmpkeyToGuidMap = new Dictionary<string,string>();
			
			int count = 0;
			foreach (Node n in nodeList)
			{
				string id = n.UID?.Trim();
				if("" == (id ?? "")) 
				{
					n.UID = MakeTempKeyFromString($"u{count}",tmpkeyToGuidMap);
					count++;
				}
				else 
				{
					//regex match 0xNN and if not, then prepend _:id
					//this will allow client to do a node tree upsert
					//some user supplied tmpkeys are causing parsing errors, so replace all with guids
					if (!rgx.IsMatch(id.ToLower()))
					{
						n.UID = MakeTempKeyFromString(id,tmpkeyToGuidMap);
					}
				}
			}
			
			HashSet<string> nodesBeingUpserted = new HashSet<string>();
			HashSet<string> nodesThatHaveParents = new HashSet<string>();
			
			List<Node> nodesPlusEdges = new List<Node>(nodeList);
			foreach (Node n in nodeList)
			{
				if(n.Parents != null && n.Parents.Count > 0)
				{
					foreach (var parent in n.Parents)
					{
						//the parentUID must be 0xNN or a valid tempkey
						string id = parent.UID?.Trim();
						if (!rgx.IsMatch(id.ToLower()))
							parent.UID = MakeTempKeyFromString(id,tmpkeyToGuidMap);

						nodesPlusEdges.Add(new Node { UID = parent.UID, Children = new List<NodeWithUidAndChildren> { new NodeWithUidAndChildren { UID = n.UID } } });
					}
					nodesThatHaveParents.Add(n.UID);
				}
				//child edges will be retained in the node during upsert, so dont need to create them
				//need to make their tmpkeys safe and record that the children are linkedTo for the later check that parents unlinked nodes to the root node
				if(n.Children != null && n.Children.Count > 0)
				{
					foreach (var child in n.Children)
					{
						//the parentUID must be 0xNN or a valid tempkey
						string id = child.UID?.Trim();
						if (!rgx.IsMatch(id.ToLower()))
							child.UID = MakeTempKeyFromString(id,tmpkeyToGuidMap);

						nodesThatHaveParents.Add(child.UID);
					}
				}

				//ensure that the "in" property is null before upserting the node into the DB
				// because the Parents property is an alias for ~out when the Node is retrieved from the DB
				//and update the lastModTime
				n.Parents = null;
				n.SetLastModTimeToNow();
				nodesBeingUpserted.Add(n.UID);
				
			}

			// substract the set of nodes that have specified a parent to link to 
			nodesBeingUpserted.ExceptWith(nodesThatHaveParents);
			foreach (var n in nodesBeingUpserted)
				if(n.StartsWith("_:"))//set the parentUID to the ROOTNODE_UID !!except if it *is* the root node (the root node will never start with _:)
				{
					nodesPlusEdges.Add(new Node { UID = ROOTNODE_UID, Children = new List<NodeWithUidAndChildren> { new NodeWithUidAndChildren { UID = n } } });
				}
			
			var transactionResult = await DoTransaction(TX_SET, nodesPlusEdges, jsonSerialiseIgnoreNull);
			LogService.Log(LOGLEVEL.DEBUG,"DBManager UpsertNodesAsync Result: "+transactionResult.ToString());
			foreach (var n in nodeList)
			{
				if(n.UID.StartsWith("_:")) 
					n.UID = transactionResult.Value.Uids[n.UID.Substring(2)];
				returnNewUIDs.Add(n.UID);
			}
			
			return returnNewUIDs;
		}		
		
		//TODO:
		//Delete should maybe only be executed by the backend.
		// instead of permanent delete requests from client, just link them to the recyclebin??
		//delete nodes (recursivedelete = true)
		//1 query first to get node's parents
		//2 unlink node from its parents
		//3 recursive query to get uids of node's children
		//4 delete nodes
		
		//link node
		/*

		assuming these nodes already exist:

		0x121 0x123
		    \  /
		    0x122
			/  \  
		0x124 0x125

		{ "set":[
		  {"uid":"0x121","children":[{"uid":"0x122"}]},
		  {"uid":"0x123","children":[{"uid":"0x122"}]},
		  {"uid":"0x122","children":[{"uid":"0x124"}]},
		  {"uid":"0x122","children":[{"uid":"0x125"}]}
		  ] }

		List<NodeWithUidAndChildren>
		*/
		
		private void AddParentToNodeEdges(List<string> nodeUids, List<string> parentUids, List<NodeWithUidAndChildren> mutateList, bool updateLastModTime = true)
		{
			if(parentUids != null && parentUids.Count > 0)
				foreach (var nodeUid in nodeUids)
				{
					foreach (var parentUid in parentUids)
						mutateList.Add(new NodeWithUidAndChildren ( 
							parentUid, 
							new List<NodeWithUid> { new NodeWithUid(nodeUid, updateLastModTime) },
							updateLastModTime
							)
						);
				}
		}

		private void AddNodeToChildEdges(List<string> nodeUids, List<string> childUids, List<NodeWithUidAndChildren> mutateList, bool updateLastModTime = true)
		{
			if(childUids != null && childUids.Count > 0)
				foreach (var nodeUid in nodeUids)
				{
					foreach (var childUid in childUids)
						mutateList.Add(new NodeWithUidAndChildren ( 
							nodeUid, 
							new List<NodeWithUid> { new NodeWithUid(childUid, updateLastModTime) },
							updateLastModTime
							) 
						);
				}
		}
		
		public async Task<ServerResponse> LinkNodesAsync(List<string> nodeUids, List<string> parentUids = null, List<string> childUids = null)
		{
			List<NodeWithUidAndChildren> setList = new List<NodeWithUidAndChildren>();

			AddParentToNodeEdges(nodeUids, parentUids, setList);
			AddNodeToChildEdges(nodeUids, childUids, setList);

			var transactionResult = await DoTransaction(TX_SET, setList, jsonSerialiseIgnoreNull);
			LogService.Log(LOGLEVEL.DEBUG,"DBManager LinkNodesAsync Result: "+transactionResult.ToString());
			return new ServerResponse { message = ((transactionResult.IsFailed) ? "An Error Occurred" : "Update Success") , error = transactionResult.IsFailed};
		}
		
		
		
		//unlink node
		/*

		assuming these nodes already exist:

		0x121 0x123
		    \  /
		    0x122
			/  \  
		0x124 0x125

		{ "delete":[
		  {"uid":"0x121","children":[{"uid":"0x122"}]},
		  {"uid":"0x123","children":[{"uid":"0x122"}]},
		  {"uid":"0x122","children":[{"uid":"0x124"}]},
		  {"uid":"0x122","children":[{"uid":"0x125"}]}
		  ] }

		List<NodeWithUidAndChildren>
		deleteJson : json
		*/
		
		
		public async Task<ServerResponse> UnlinkNodesAsync(List<string> nodeUids, List<string> parentUids = null, List<string> childUids = null)
		{
			List<NodeWithUidAndChildren> deleteList = new List<NodeWithUidAndChildren>();
			AddParentToNodeEdges(nodeUids, parentUids, deleteList, false);
			AddNodeToChildEdges(nodeUids, childUids, deleteList, false);

			var transactionResult = await DoTransaction(TX_DELETE, deleteList, jsonSerialiseIgnoreNull);//lastmodtime was null, jsonSerialiseOptions);
			LogService.Log(LOGLEVEL.DEBUG,"DBManager UnLinkNodesAsync (delete) Result: "+transactionResult.ToString());

			// delete doesn't update any fields, so have to do another mutate TX to update the lastModTime for each of the nodes
			List<NodeWithUidAndChildren> setList = new List<NodeWithUidAndChildren>();
			foreach (var _uid in nodeUids)
				setList.Add(new NodeWithUidAndChildren(uid: _uid, initialiseTime: true) );
			if(parentUids != null && parentUids.Count > 0)
				foreach (var _uid in parentUids)
					setList.Add(new NodeWithUidAndChildren(uid: _uid, initialiseTime: true) );
			if(childUids != null && childUids.Count > 0)
				foreach (var _uid in childUids)
					setList.Add(new NodeWithUidAndChildren(uid: _uid, initialiseTime: true) );

			transactionResult = await DoTransaction(TX_SET, setList, jsonSerialiseIgnoreNull);
			LogService.Log(LOGLEVEL.DEBUG,"DBManager UnLinkNodesAsync (set) Result: "+transactionResult.ToString());
			return new ServerResponse { message = ((transactionResult.IsFailed) ? "An Error Occurred" : "Update Success") , error = transactionResult.IsFailed};
		}


    }
}
