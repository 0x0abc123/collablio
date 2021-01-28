using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace collablio.Models
{
	public class PropsInternal
	{
		// the strings need to match the property names of the Node class
		public static string UID = "UID";
		public static string Type = "Type";
		public static string LastModTime = "LastModifiedTime";
		public static string EventTimestamp  = "EventTimestamp";
		public static string WhoEditing = "WhoIsEditing";
		public static string Label = "Label";
		public static string Detail = "Detail";
		public static string CustomData = "CustomData";
		public static string B64Data = "B64Data";
		public static string TextData = "TextData";
		public static string DgraphType = "DgraphType";
		public static string Parents = "Parents";
		public static string Children = "Children";
	}

	public class PropsJson
	{
		// these strings need to match the predicate names of Dgraph types
		public static string UID = "uid";
		public static string Type = "ty";
		public static string LastModTime = "m";
		public static string EventTimestamp  = "t";
		public static string WhoEditing = "e";
		public static string Label = "l";
		public static string Detail = "d";
		public static string CustomData = "c";
		public static string B64Data = "b";
		public static string TextData = "x";
		public static string DgraphType = "dgraph.type";
		public static string Parents = "in";
		public static string Children = "out";
	}

	public class NodeJsonNamingPolicy : JsonNamingPolicy
	{		
		private static Dictionary<string,string> lookupTable = new Dictionary<string,string> {

			// json -> internal naming
			{ PropsJson.UID, PropsInternal.UID },
			{ PropsJson.LastModTime, PropsInternal.LastModTime },
			{ PropsJson.EventTimestamp, PropsInternal.EventTimestamp },
			{ PropsJson.WhoEditing, PropsInternal.WhoEditing },
			{ PropsJson.Label, PropsInternal.Label },
			{ PropsJson.Detail, PropsInternal.Detail },
			{ PropsJson.CustomData, PropsInternal.CustomData },
			{ PropsJson.TextData, PropsInternal.TextData },
			{ PropsJson.Type, PropsInternal.Type },
			{ PropsJson.B64Data, PropsInternal.B64Data },
			{ PropsJson.DgraphType, PropsInternal.DgraphType },
			{ PropsJson.Parents, PropsInternal.Parents },
			{ PropsJson.Children, PropsInternal.Children },

			// internal naming -> json
			{ PropsInternal.UID, PropsJson.UID },
			{ PropsInternal.LastModTime, PropsJson.LastModTime },
			{ PropsInternal.EventTimestamp, PropsJson.EventTimestamp },
			{ PropsInternal.WhoEditing, PropsJson.WhoEditing },
			{ PropsInternal.Label, PropsJson.Label },
			{ PropsInternal.Detail, PropsJson.Detail },
			{ PropsInternal.CustomData, PropsJson.CustomData },
			{ PropsInternal.TextData, PropsJson.TextData },
			{ PropsInternal.Type, PropsJson.Type },
			{ PropsInternal.B64Data, PropsJson.B64Data },
			{ PropsInternal.DgraphType, PropsJson.DgraphType },
			{ PropsInternal.Parents, PropsJson.Parents },
			{ PropsInternal.Children, PropsJson.Children },

		};
		
		public override string ConvertName(string name)
		{
			string convertedName = lookupTable.ContainsKey(name) ? lookupTable[name] : name;
			//LogService.Log(LOGLEVEL.DEBUG,String.Format("ConvertName {0} to {1}",name,convertedName));
			return convertedName;
		}
		
	}

    public class Node
    {
		public static readonly string TYPE_ROOTNODE = "_";
		public static readonly string TYPE_CLIENT = "Cl";
		public static readonly string TYPE_PROJECT = "Pr";
		public static readonly string TYPE_GROUP = "Gr";
		public static readonly string TYPE_SUBGROUP = "Sg";
		public static readonly string TYPE_ITEM = "It";
		public static readonly string TYPE_ATTACHMENT = "At";
		public static readonly string TYPE_REPORT = "Rp";

		public static readonly string ATTACH_EMPTY = "_";

		public static readonly string TAG_NORMAL = "_";
		public static readonly string TAG_ALERT = "!";
		public static readonly string TAG_PROTO = "p";

		public static readonly string GROUP_DEFAULT = "Default Group";

		public string UID { get; set; } // Dgraph UID (of the format 0xNN)
		public string Type { get; set; } // user defined type
		public DateTime LastModifiedTime { get; set; } // last modified timestamp
		public DateTime? EventTimestamp { get; set; } = null; // custom timestamp
		public string WhoIsEditing { get; set; } // if/who is currently editing
		public string Label { get; set; } // label/name/title
		public string Detail { get; set; } // description/detail
		public string CustomData { get; set; } // custom data string (for large text attachments use TextData)
		public string B64Data { get; set; } //base64 data (this is public but should be get/set using GetBase64AsBytes and SetBase64FromBytes
		public string TextData { get; set; } // custom data (string)
		public List<string> DgraphType { get; } //this should only be "N". The dgraph type is native to the database, the user type field is used by the client
		public List<NodeWithUidAndChildren> Parents { get; set; } //instead of "Edges" the app stores references to linked nodes UIDs
		public List<NodeWithUidAndChildren> Children { get; set; }
		
		public Node()
		{
			SetLastModTimeToNow();
			DgraphType = new List<string> { "N" };
		}

		public Node(string _type) : this()
		{
			Type = _type;
		}
		
		public void SetLastModTimeToNow()
		{
			LastModifiedTime = DateTime.UtcNow;
		}
		
		public void SetBase64FromBytes(Byte[] bytes)
		{
			B64Data = Convert.ToBase64String(bytes);
		}		

		public Byte[] GetBase64AsBytes()
		{
			return (B64Data == null || B64Data == "") ? null : Convert.FromBase64String(B64Data);
		}		

		public void AddParent(string parentUid)
		{
			if(Parents == null)
				Parents = new List<NodeWithUidAndChildren>();
			Parents.Add(new NodeWithUidAndChildren { UID = parentUid });
		}
	}

	public class NodeWithUid
	{
		public string UID {get; set;}
		public DateTime? LastModifiedTime { get; set; } = null;

		public NodeWithUid()
		{
		}

		public NodeWithUid(string uid, bool initialiseTime = false)
		{
			UID = uid;
			if (initialiseTime) SetLastModTimeToNow();
		}
		
		public void SetLastModTimeToNow()
		{
			LastModifiedTime = DateTime.UtcNow;
		}
		
	}
	
	public class NodeWithUidAndChildren
	{
		public string UID {get; set;}
		public List<NodeWithUid> Children {get; set;}
		public DateTime? LastModifiedTime { get; set; } = null;

		public NodeWithUidAndChildren()
		{
		}

		public NodeWithUidAndChildren(string uid, List<NodeWithUid> children = null, bool initialiseTime = false)
		{
			UID = uid;
			Children = children;
			if (initialiseTime) SetLastModTimeToNow();
		}
		
		public void SetLastModTimeToNow()
		{
			LastModifiedTime = DateTime.UtcNow;
		}
		
	}

}
