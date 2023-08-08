using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dgraph;
using Dgraph.Transactions;
using FluentResults;
using Grpc.Net.Client;
using System.Text.Json;
using System.Text.RegularExpressions;
using collablio.Models;
using System.Text.Json.Serialization;
//using Grpc.Core;
//using System.Text;
//using System.Linq;
//using System.Security.Cryptography;
//using System.Threading;

namespace collablio
{

	public class QueryResultNodes
	{
		public List<Node> qr { get; set; }
	}

	public class QueryResultUser
	{
		public List<User> qr { get; set; }
	}

	public class QueryOptionsClause
	{
		public List<QueryOptionsClause>? and { get; set; } = null;
		public List<QueryOptionsClause>? or { get; set; } = null;
		public string? field { get; set; } = null;
		public string? op { get; set; } = null;
		public string? val { get; set; } = null;
	}

	public class QueryOptions
	{
		public List<string>? rootIds { get; set; }
		public QueryOptionsClause? rootQuery { get; set; } //if present, then ignore rootIds, recurse and depth
		public string recurse { get; set; } = "out";
		public uint depth { get; set; } = 0;
		public QueryOptionsClause? filters { get; set; }
		public List<string>? select { get; set; }
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

		private enum EdgeType {
			Parent,
			Link
		}

		private	Regex rgx_uid = new Regex(@"^0x[0-9a-f]+$");

		private enum UpdateType {
			Add,
			Delete
		}

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

		//ensure that dgraph/setup.sh has already been run to setup the database

		private static JsonSerializerOptions jsonSerialiseOptions;
		private static JsonSerializerOptions jsonSerialiseIgnoreNull;
		private static JsonSerializerOptions jsonDeserialiseOptions;

		private DatabaseManager()
		{
			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
			_dbclient = new DgraphClient(GrpcChannel.ForAddress("http://127.0.0.1:9080", new GrpcChannelOptions { MaxReceiveMessageSize = 100*1024*1024 }));
	
			jsonSerialiseOptions = new JsonSerializerOptions
				{
					PropertyNamingPolicy = new NodeJsonNamingPolicy(),
					IgnoreNullValues = false,
					IgnoreReadOnlyProperties = false
				};
				
			jsonSerialiseIgnoreNull = new JsonSerializerOptions
					{
						PropertyNamingPolicy = new NodeJsonNamingPolicy(),
						//IgnoreNullValues = true,
						DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
						IgnoreReadOnlyProperties = false
					};

			jsonDeserialiseOptions = new JsonSerializerOptions
					{
						PropertyNamingPolicy = new NodeJsonNamingPolicy(),
						//IgnoreNullValues = true,
						DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
				var result = await DoTransaction(UpdateType.Add, rn, jsonSerialiseIgnoreNull);
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

		private string escapeAllNonAlphanumOrSpaceChars(string strVal) {
			string retString = "";
			Regex rgxAlphanumOrSpace = new Regex("[A-Za-z0-9 ,]");
			foreach (char c in strVal) {
				string cstr = c.ToString();
				retString += (rgxAlphanumOrSpace.IsMatch(cstr) ? cstr  : '\\'+cstr);
			}
			return retString;
		}

		private string createRegex(string strVal) {
			return '/' + escapeAllNonAlphanumOrSpaceChars(strVal) + "/i";
		}
		
		public async Task<ServerResponse> QueryAsync(
			List<string>? uidsOfParentNodes = null, 
			string? field = null, 
			string? op = null, 
			string? val = null, 
			int recurseDepth = 0,  //in dgraph recurse level 0 means unlimited but here we interpret it as no recurse
			string? nodeType = null,
			bool includeBody = false,
			bool upwardsRecurse = false
			)
		{
			QueryOptions qo = new QueryOptions();
			qo.rootIds = uidsOfParentNodes;
			qo.depth = (uint)recurseDepth;
			qo.recurse = (upwardsRecurse) ? "outinv" : "out";
			qo.select = new List<string> {"l","d","c","m","in","out","ty"};
			if(includeBody)
				qo.select.Add("body");
			
			QueryOptionsClause tmpCl = new QueryOptionsClause();
			tmpCl.field = field ?? PropsJson.LastModTime;
			tmpCl.op = op ?? OP_GT;
			tmpCl.val = val ?? "0";
			if(nodeType == null || nodeType.Length < 1){
				qo.filters = tmpCl;
			}
			else {
				QueryOptionsClause typCl = new QueryOptionsClause();
				typCl.field = PropsJson.Type;
				typCl.op = OP_EQ;
				typCl.val = nodeType;
				qo.filters = new QueryOptionsClause();
				qo.filters.and = new List<QueryOptionsClause>() {tmpCl, typCl};
			}
			return await QueryWithOptionsAsync(qo);
		}

        private string renderOp(string? op) { 
			op = op ?? OP_GT; 
			op = op.ToLower();
			HashSet<string> allowedOps = new HashSet<string> {
				OP_EQ,OP_GT,OP_LT,OP_LTE,OP_GTE,OP_TEXTSEARCH
			};
			if(!allowedOps.Contains(op))
				op = OP_TEXTSEARCH;
			return op; 
		}
        private string renderField(string? field) { 
			field = field ?? PropsJson.LastModTime;
			field = field.ToLower();
			HashSet<string> allowedFields = new HashSet<string> {
				PropsJson.LastModTime,
				PropsJson.EventTimestamp,
				PropsJson.WhoEditing,
				PropsJson.Label,
				PropsJson.Detail,
				PropsJson.CustomData,
				PropsJson.TextData,
				PropsJson.Type,
				PropsJson.B64Data
			};
			if(!allowedFields.Contains(field))
				field = PropsJson.Label;
			return field; 
		}

        private string renderDirection(string? dirstr) { 
			dirstr = dirstr ?? "out";
			var directions = new Dictionary<string,string> { 
				{ "out" , "out" },
				{ "outinv" , "~out" },
				{ "lnk" , "lnk" },
				{ "lnkinv" , "~lnk" }
			};
			if (directions.ContainsKey(dirstr))
				return directions[dirstr];
			else
				return "out";
		}


        private string constructQueryStringAndAddVars(QueryOptionsClause clause, ref Dictionary<string,string> queryvars) {
            string retval = "";
            if (clause.and != null || clause.or != null ) {
                List<QueryOptionsClause> subclauses = clause.and != null ? clause.and : clause.or ;
                string opstr = clause.and != null ? " and " : " or ";
                List<string> clstrs = new List<string>();
                foreach (var subclause in subclauses) {
                    clstrs.Add(constructQueryStringAndAddVars(subclause, ref queryvars));
                }
                retval = "(" + string.Join(opstr, clstrs) + ")";
            } else {
				string clVal = clause.val ?? "0";
				string valStr = String.Format("$vv{0}",Helpers.GetMD5HashOfString(clVal).Substring(20));
				string op = renderOp(clause.op);
				string field = renderField(clause.field);
				
				//the timestamp values we received from the client should be unix epoch format, so convert to this format: 2006-01-02T15:04:05
				if(field == PropsJson.LastModTime || field == PropsJson.EventTimestamp)
					clVal = (Helpers.UnixEpochToDateTime(Convert.ToDouble(clVal))).ToString("o");

				if(op == OP_TEXTSEARCH) {
					clVal = clVal.Trim();
					if(clVal.Length > 2) {
						op = "regexp";
						clVal = createRegex(clVal);
					}
				}

				queryvars[valStr] = clVal;

                retval += op +"("+field+","+valStr+")";
            }
            return retval;
        }

		private static string renderQueryVarsString(Dictionary<string,string> vars) {
			List<string> tmpV = new List<string>{};
			Dictionary<string, string>.KeyCollection keyColl = vars.Keys;
			foreach (var key in keyColl) {
				if(key.StartsWith("$vv"))
					tmpV.Add(key);
			}
			string retval = "";
            if(tmpV.Count > 0) {
                retval = tmpV.Count > 1 ? String.Join(": string, ",tmpV) : tmpV[0];
                retval += ": string";                
            }
			return retval;
		}

		private static Dictionary<string, string> allowedFields = new Dictionary<string, string> { 
			 {"ty","ty"},
			 {"l","l"},
			 {"d","d"},
			 {"c","c"},
			 {"m","m"},
			 {"e","e"},
			 {"t","t"},
			 {"body","b x"},
			 {"in","in: ~out {uid}"},
			 {"out","out {uid}"},
			 {"inl","inl: ~lnk {uid}"},
			 {"lnk","lnk {uid}"}
			};

		private static string renderFields(List<string> fields) {
			List<string> tmpV = new List<string>{};
			foreach(var field in fields){
				if(allowedFields.ContainsKey(field)){
					tmpV.Add(allowedFields[field]);
				}
			}
			string retval = "";
            if(tmpV.Count > 0) {
                retval = tmpV.Count > 1 ? String.Join(" ",tmpV) : tmpV[0];
            }
			return retval;
		}


		public async Task<ServerResponse> QueryWithOptionsAsync(QueryOptions opts)
		{			
			var query = "";
			var vars = new Dictionary<string,string>();
			bool isRootQuery = (opts.rootQuery != null);
			
			if(isRootQuery)
			{
				query = @"query q(__QVARS__) {
					qr(func: __ROOTQUERY__)";
				query = query.Replace("__ROOTQUERY__",constructQueryStringAndAddVars(opts.rootQuery, ref vars));
			}
			else
			{
				int recurseDepth = (int) opts.depth;
				recurseDepth = (recurseDepth > MAX_RECURSE_DEPTH) ? MAX_RECURSE_DEPTH : ((recurseDepth < 0) ? 0 : recurseDepth);
				
				//dgraph expects a string of uids like this: "[0x1, 0x2, 0x3]"
				//JsonSerializer.Serialize(uidsOfParentNodes) will quote each so the string is "[\"0x1\", \"0x2\", \"0x3\"]" 
				//this causes a parse error in dgraphQL
				// it seems to do its own validation but to be safe...
				List<string>? uidsOfParentNodes = opts.rootIds;
				uidsOfParentNodes = (uidsOfParentNodes?.Count > 0) ?  uidsOfParentNodes : new List<string>{ROOTNODE_UID};
				List<string> sanitisedParentNodeList = new List<string>();
				foreach (string s in uidsOfParentNodes)
				{
					string sanitisedUid = Helpers.SanitiseUID(s);
					sanitisedUid = (sanitisedUid != "0x0") ? sanitisedUid : ROOTNODE_UID; //dgraph throws an exception now when trying to fetch 0x0
					sanitisedParentNodeList.Add(sanitisedUid);
				}
				/*!!!!! recent versions of Dgraph don't want any square brackets around the UIDs when calling uid()
				so it should be like this: uid(0x11,0x12,0x13)*/
				string serialisedParentNodeList = "["+String.Join(",",sanitisedParentNodeList)+"]";

				vars["$ids"] = serialisedParentNodeList;

				if(recurseDepth > 0)
				{
					vars["$rdepth"] = recurseDepth.ToString();
					query = @"query q($ids: string, $rdepth: int, __QVARS__) {
						var(func: uid($ids)) @recurse(depth: $rdepth) 
						{
						  NID as uid
						  __DIRECTION__
						}
					  
						qr(func: uid(NID))";
				}
				else{
					query = @"query q($ids: string, __QVARS__) {
						qr(func: uid($ids))";
				}
			}
			query += @"  @filter(__FILTERS__)
					{
						uid __FIELDS__
					}
				}";

			query = query.Replace("__FIELDS__",(opts.select?.Count > 0 ? renderFields(opts.select) : ""))
					.Replace("__DIRECTION__",renderDirection(opts.recurse))
					.Replace("__FILTERS__",constructQueryStringAndAddVars(opts.filters, ref vars))
					.Replace("__QVARS__",renderQueryVarsString(vars));
			

			var res = await _dbclient.NewReadOnlyTransaction().QueryWithVars(query, vars);
			LogService.Log(LOGLEVEL.DEBUG,$"DBManager QueryAsync result:\nQuery: {query}\nvars: '{JsonSerializer.Serialize(vars)}'\nRes: {res}");

			if (res.IsFailed){
				//LogService.Log(LOGLEVEL.DEBUG,"result isFailed");
				return null;
			}

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
		
		private async Task<Result<Response>> DoTransaction(UpdateType SetOrDelete, object ObjToSerialise, JsonSerializerOptions options)
		{
			using(var txn = _dbclient.NewTransaction()) {
				var json =  
					JsonSerializer.Serialize(ObjToSerialise, options);
				LogService.Log(LOGLEVEL.DEBUG,$"DBManager DoTX {SetOrDelete} Sending: "+json);
				var txnResult = (SetOrDelete == UpdateType.Delete) ? await txn.Mutate(deleteJson : json) : await txn.Mutate(setJson : json);

				if(!txnResult.IsFailed)
					await txn.Commit();
				return txnResult;
			}
		}


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
		

		private void storeNodeOutgoingEdgeData(
			ref Dictionary<string,Node> oed, 
			string srcNodeUID, 
			string destNodeUID,
			EdgeType etype = EdgeType.Parent) {

			JustUidAndOutgoingEdges destNode = new JustUidAndOutgoingEdges(destNodeUID);

			if (oed.ContainsKey(srcNodeUID)) {
				var sn = oed[srcNodeUID];
				switch(etype) {
					case EdgeType.Parent:
						if(sn?.Children == null)
							sn.Children = new List<JustUidAndOutgoingEdges>();
						sn?.Children?.Add(destNode);
						break;
					case EdgeType.Link:
						if(sn?.OutLinks == null)
							sn.OutLinks = new List<JustUidAndOutgoingEdges>();
						sn?.OutLinks?.Add(destNode);
						break;
					default:
						break;
				}
			}
			else {
				Node srcNode = new Node { 
					UID = srcNodeUID 
				};

				List<JustUidAndOutgoingEdges> ldst = new List<JustUidAndOutgoingEdges> { destNode };
				switch(etype) {
					case EdgeType.Parent:
						srcNode.Children = ldst;
						break;
					case EdgeType.Link:
						srcNode.OutLinks = ldst;
						break;
					default:
						break;
				}

				oed[srcNodeUID] = srcNode;
			}
		}

		private void maybeFixNodeTmpUID(JustUidAndOutgoingEdges n, ref Dictionary<string,string> tmpkeyToGuidMap){
			//the node UID must be 0xNN or a valid tempkey
			//regex match 0xNN and if not, then prepend _:id
			//this will allow a collablio client to do a node tree upsert
			//some user supplied tmpkeys are causing parsing errors, so replace all with guids
			string id = n.UID?.Trim();
			if (!rgx_uid.IsMatch(id.ToLower()))
				n.UID = MakeTempKeyFromString(id,tmpkeyToGuidMap);

		}

		//create nodes and optionally link to parent or children
		public async Task<List<string>> UpsertNodesAsync(List<Node> nodeList, bool forDCG = false)//, List<string> parentUids = null, List<string> childUids = null) 
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
			
			//guid lookup table
			Dictionary<string,string> tmpkeyToGuidMap = new Dictionary<string,string>();
			
			int count = 0;
			foreach (Node n in nodeList)
			{
				if("" == (n.UID?.Trim() ?? "")) 
				{
					n.UID = MakeTempKeyFromString($"u{count}",tmpkeyToGuidMap);
					count++;
				}
				else 
				{
					maybeFixNodeTmpUID(n, ref tmpkeyToGuidMap);
				}
			}
			
			HashSet<string> nodesBeingUpserted = new HashSet<string>();
			HashSet<string> nodesThatHaveParents = new HashSet<string>();
			Dictionary<string,Node> outgoingEdgeData = new Dictionary<string,Node>();

			foreach (Node n in nodeList)
			{
				if(n.Parents?.Count > 0)
				{
					foreach (var parent in n.Parents)
					{
						maybeFixNodeTmpUID(parent, ref tmpkeyToGuidMap);
						storeNodeOutgoingEdgeData(ref outgoingEdgeData, parent.UID, n.UID);
					}
					nodesThatHaveParents.Add(n.UID);
				}
				//children edges will be retained in the node during upsert, so dont need to create them
				//need to make their tmpkeys safe and record that the children are linkedTo for the later check that parents unlinked nodes to the root node
				if(n.Children?.Count > 0)
				{
					foreach (var child in n.Children)
					{
						maybeFixNodeTmpUID(child, ref tmpkeyToGuidMap);
						nodesThatHaveParents.Add(child.UID);
					}
				}

				if(n.InLinks?.Count > 0)
				{
					foreach (var inlink in n.InLinks)
					{
						maybeFixNodeTmpUID(inlink, ref tmpkeyToGuidMap);
						storeNodeOutgoingEdgeData(ref outgoingEdgeData, inlink.UID, n.UID);
					}
					nodesThatHaveParents.Add(n.UID);
				}
				//outlinks edges will be retained in the node during upsert, so dont need to create them
				//need to make their tmpkeys safe and record that the children are linkedTo for the later check that parents unlinked nodes to the root node
				if(n.OutLinks?.Count > 0)
				{
					foreach (var outlink in n.OutLinks)
					{
						maybeFixNodeTmpUID(outlink, ref tmpkeyToGuidMap);
						nodesThatHaveParents.Add(outlink.UID);
					}
				}

				//ensure that the "in" property is null before upserting the node into the DB
				// because the Parents property is an alias for ~out when the Node is retrieved from the DB
				//and update the lastModTime
				n.Parents = null;
				n.InLinks = null;
				n.SetLastModTimeToNow();
				nodesBeingUpserted.Add(n.UID);
				
			}

			// substract the set of nodes that have specified a parent to link to 
			nodesBeingUpserted.ExceptWith(nodesThatHaveParents);
			foreach (var n in nodesBeingUpserted)
				if(n.StartsWith("_:"))//set the parentUID to the ROOTNODE_UID !!except if it *is* the root node (the root node will never start with _:)
				{
					EdgeType etype = (forDCG) ? EdgeType.Link : EdgeType.Parent;
					storeNodeOutgoingEdgeData(ref outgoingEdgeData, ROOTNODE_UID, n, etype);
				}
			
			List<Node> nodesPlusEdges = new List<Node>(nodeList);
			foreach(var kvpair in outgoingEdgeData)
    			nodesPlusEdges.Add(kvpair.Value);
			
			var transactionResult = await DoTransaction(UpdateType.Add, nodesPlusEdges, jsonSerialiseIgnoreNull);
			LogService.Log(LOGLEVEL.DEBUG,"DBManager UpsertNodesAsync Result: "+transactionResult.ToString());
			foreach (var n in nodeList)
			{
				if(n.UID.StartsWith("_:")) 
					n.UID = transactionResult.Value.Uids[n.UID.Substring(2)];
				returnNewUIDs.Add(n.UID);
			}
			
			return returnNewUIDs;
		}		
		
		
		private void AddIncomingEdges(
			List<string> nodeUids, 
			List<string> incomingUids, 
			List<JustUidAndOutgoingEdges> mutateList, 
			EdgeType etype = EdgeType.Parent,
			bool updateLastModTime = true)
		{
			if(incomingUids != null && incomingUids.Count > 0)
			{
				List<JustUidAndOutgoingEdges> cList = new List<JustUidAndOutgoingEdges>();
				foreach (var nodeUid in nodeUids)
					cList.Add(new JustUidAndOutgoingEdges(uid: nodeUid, initialiseTime: updateLastModTime));

				foreach (var parentUid in incomingUids)
				{
					JustUidAndOutgoingEdges tmpN = new JustUidAndOutgoingEdges ( 
						uid: parentUid, 
						initialiseTime: updateLastModTime
						);
					switch(etype){
						case EdgeType.Parent:
							tmpN.Children = cList;
							break;
						case EdgeType.Link:
							tmpN.OutLinks = cList;
							break;
						default:
							break;
					}
					mutateList.Add(tmpN);						
				}
			}	
		}

		private void AddOutgoingEdges(
			List<string> nodeUids, 
			List<string> outgoingUids, 
			List<JustUidAndOutgoingEdges> mutateList, 
			EdgeType etype = EdgeType.Parent,
			bool updateLastModTime = true)
		{
			if(outgoingUids != null && outgoingUids.Count > 0)
			{
				List<JustUidAndOutgoingEdges> cList = new List<JustUidAndOutgoingEdges>();
				foreach (var outUid in outgoingUids)
					cList.Add(new JustUidAndOutgoingEdges(uid: outUid, initialiseTime: updateLastModTime));

				foreach (var nodeUid in nodeUids)
				{
					JustUidAndOutgoingEdges tmpN = new JustUidAndOutgoingEdges ( 
						uid: nodeUid, 
						initialiseTime: updateLastModTime
						);
					switch(etype){
						case EdgeType.Parent:
							tmpN.Children = cList;
							break;
						case EdgeType.Link:
							tmpN.OutLinks = cList;
							break;
						default:
							break;
					}
					mutateList.Add(tmpN);						
				}
			}	
		}

		private void OriginalAddOutgoingEdges(
			List<string> nodeUids, 
			List<string> childUids, 
			List<JustUidAndOutgoingEdges> mutateList, 
			EdgeType etype = EdgeType.Parent,
			bool updateLastModTime = true)
		{
			if(childUids != null && childUids.Count > 0)
				foreach (var nodeUid in nodeUids)
				{
					foreach (var childUid in childUids)
						mutateList.Add(new JustUidAndOutgoingEdges ( 
							uid: nodeUid, 
							children: new List<JustUidAndOutgoingEdges> { new JustUidAndOutgoingEdges(uid: childUid, initialiseTime: updateLastModTime) },
							initialiseTime: updateLastModTime
							) 
						);
				}
		}


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


		private async Task<ServerResponse> UpdateEdgesAsync(
			UpdateType utype,
			List<string> nodeUids, 
			List<string> srcUids = null, 
			List<string> destUids = null,
			EdgeType etype = EdgeType.Parent
			)
		{
			List<JustUidAndOutgoingEdges> updateList = new List<JustUidAndOutgoingEdges>();

			AddIncomingEdges(nodeUids, srcUids, updateList, etype);
			AddOutgoingEdges(nodeUids, destUids, updateList, etype);

			var transactionResult = await DoTransaction(utype, updateList, jsonSerialiseIgnoreNull);
			LogService.Log(LOGLEVEL.DEBUG,"DBManager UpdateEdgesAsync Result: "+transactionResult.ToString());

			// delete doesn't update any fields, so have to do another mutate TX to update the lastModTime for each of the nodes
			if(utype == UpdateType.Delete)
			{
				List<JustUidAndOutgoingEdges> setList = new List<JustUidAndOutgoingEdges>();
				foreach (var _uid in nodeUids)
					setList.Add(new JustUidAndOutgoingEdges(uid: _uid, initialiseTime: true) );
				if(srcUids?.Count > 0)
					foreach (var _uid in srcUids)
						setList.Add(new JustUidAndOutgoingEdges(uid: _uid, initialiseTime: true) );
				if(destUids?.Count > 0)
					foreach (var _uid in destUids)
						setList.Add(new JustUidAndOutgoingEdges(uid: _uid, initialiseTime: true) );

				transactionResult = await DoTransaction(UpdateType.Add, setList, jsonSerialiseIgnoreNull);
				LogService.Log(LOGLEVEL.DEBUG,"DBManager UpdateEdgesAsync (set lastmod) Result: "+transactionResult.ToString());
			}

			return new ServerResponse { message = ((transactionResult.IsFailed) ? "An Error Occurred" : "Update Success") , error = transactionResult.IsFailed};
		}

		public async Task<ServerResponse> AddParentChildRelationsAsync(List<string> nodeUids, List<string> parentUids = null, List<string> childUids = null)
		{
			return await UpdateEdgesAsync(
				utype: UpdateType.Add,
				nodeUids: nodeUids, 
				srcUids: parentUids, 
				destUids: childUids,
				etype: EdgeType.Parent
			);
		}

		public async Task<ServerResponse> RemoveParentChildRelationsAsync(List<string> nodeUids, List<string> parentUids = null, List<string> childUids = null)
		{
			return await UpdateEdgesAsync(
				utype: UpdateType.Delete,
				nodeUids: nodeUids, 
				srcUids: parentUids, 
				destUids: childUids,
				etype: EdgeType.Parent
			);
		}

		public async Task<ServerResponse> AddLinkRelationsAsync(List<string> nodeUids, List<string> srcUids = null, List<string> destUids = null)
		{
			return await UpdateEdgesAsync(
				utype: UpdateType.Add,
				nodeUids: nodeUids, 
				srcUids: srcUids, 
				destUids: destUids,
				etype: EdgeType.Link
			);
		}

		public async Task<ServerResponse> RemoveLinkRelationsAsync(List<string> nodeUids, List<string> srcUids = null, List<string> destUids = null)
		{
			return await UpdateEdgesAsync(
				utype: UpdateType.Delete,
				nodeUids: nodeUids, 
				srcUids: srcUids, 
				destUids: destUids,
				etype: EdgeType.Link
			);
		}

		
		
		public async Task<User> QueryUserAsync(string username)
		{

			var vars = new Dictionary<string,string> { 
				{ "$username" , username }
			};

			var query = "";

			query = @"query q($username: string){ qr(func: type(U)) @filter (eq(username,$username)) { uid username password } }";

			var res = await _dbclient.NewReadOnlyTransaction().QueryWithVars(query, vars);
			LogService.Log(LOGLEVEL.DEBUG,$"DBManager QueryUserAsync result:\nQuery: {query}\nvars: username='{username}'\nRes: {res}");

			if (res.IsFailed){
				//LogService.Log(LOGLEVEL.DEBUG,"result isFailed");
				return null;
			}
			
			QueryResultUser queryResult = JsonSerializer.Deserialize<QueryResultUser>(res.Value.Json);
			User u = queryResult.qr.Count > 0 ? queryResult.qr[0] : null;
			return u;
		}

		//TODO:
		//Delete() -> which should maybe only be executed by the backend.
		// instead of permanent delete requests from client, just link them to the recyclebin??
		//delete nodes (recursivedelete = true)
		//1 query first to get node's parents
		//2 unlink node from its parents
		//3 recursive query to get uids of node's children
		//4 delete nodes


    }
}
