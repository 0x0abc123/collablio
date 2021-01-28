using System;
using System.Collections.Generic;
using collablio.Models;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Xml.Linq;

namespace collablio.AppTasks
{
	class NodePlusChildren
	{
		public Node n {get; set;}
		public List<NodePlusChildren> children {get; set;}
		public NodePlusChildren() { children = new List<NodePlusChildren>(); }
	}

	//curl -v  -F 'filedata=@/mnt/d/Archive/vmshare/proj/python/autorecon-working/testfiles/nmap-svsc-test2.xml' -F 'id=test1234' -F 'type=import_nmap_xml' -F '_p={"parentid":"0x9"}' http://10.3.3.60:5000/apptask

	// NMAP DTD is out of date -> https://nmap.org/book/nmap-dtd.html
	// this looks more up to date ->  https://nmap.org/book/output-formats-xml-output.html
	class XMLImportNmap
	{
		private static HashSet<string> allowedContentTypes = new HashSet<string> { "application/xml", "text/plain" };
		private static string NMAP = "nmap";
		
		public static async Task Run(AppTask apptask)
		{
			//check if there's actually a file and it's not empty:
			if(apptask.fileMemStream == null || apptask.fileMemStream.Length < 1  ||  !allowedContentTypes.Contains(apptask.filedata.ContentType))
			{
				LogService.Log(LOGLEVEL.ERROR,String.Format("XMLImportNmap: file length is 0 or mime-type {0} not allowed",apptask.filedata.ContentType));
				return;
			}
			//**also check what the parent ID is, we need it to complete the task
			if(apptask.param == null || !apptask.param.ContainsKey(AppTask.PARENTID) || apptask.param[AppTask.PARENTID].Length < 1)
			{
				LogService.Log(LOGLEVEL.ERROR,"XMLImportNmap: no PARENTID was provided");
				return;
			}
			
			DatabaseManager dbmgr = DatabaseManager.Instance();

			string parentID = apptask.param[AppTask.PARENTID];
			
			//search root ID is optional -> if supplied then search for existing hosts under it and update them instead of creating new ones
			string rootID = Helpers.GetValueOrBlank(AppTask.ROOTID,apptask.param);
			if(rootID == "")
				rootID = parentID;


			if (apptask.fileMemStream.Position > 0)
				apptask.fileMemStream.Position = 0;

			XDocument xdoc = XDocument.Load(apptask.fileMemStream);

			XElement rootNode = xdoc.Element("nmaprun");

			Dictionary<string,string> keyvalsNmaprun = new Dictionary<string,string>();
			
			foreach (XAttribute att in rootNode.Attributes()) {
				keyvalsNmaprun[att.Name.ToString()] = att.Value;						
			}
			
			Console.WriteLine("{0}\nCmd: {1}\n",
				Helpers.GetValueOrBlank("startstr",keyvalsNmaprun),
				Helpers.GetValueOrBlank("args",keyvalsNmaprun));

			XElement nmaprun = xdoc.Element("nmaprun");
			foreach (XElement host in nmaprun.Elements("host"))
			{
				if(host.Element("status")?.Attribute("state")?.Value == "up")
				{

					//create a Node (for this host)
					NodePlusChildren hostNode = new NodePlusChildren { n = new Node(AppTask.TYPE_HOST) };
					
					NodePlusChildren hostDetailsNode = new NodePlusChildren { n = new Node(AppTask.TYPE_CAT) };
					hostDetailsNode.n.Label = "Host Details";

					string _ipv4addr = null;
					string _ipv6addr = null;
					string _macaddr = null;
					
					foreach (XElement address in host.Elements("address"))
					{
						string addr = address.Attribute("addr").Value;
						string addrtype = address.Attribute("addrtype").Value;

						if(addrtype == "ipv4")
							_ipv4addr = addr;
						else if(addrtype == "ipv6")
							_ipv6addr = addr;
						else if(addrtype == "mac")
							_macaddr = addr;

						NodePlusChildren a = new NodePlusChildren { n = new Node(AppTask.TYPE_ANNOT) };
						a.n.Label = addr;
						a.n.Detail = "addr";

						hostDetailsNode.children.Add(a);
					}

					hostNode.n.Label = _ipv4addr ?? (_ipv6addr ?? (_macaddr ?? Guid.NewGuid().ToString()));
					
					foreach (XElement hostname in host.Element("hostnames").Elements("hostname"))
					{
						string name = hostname.Attribute("name").Value;
						NodePlusChildren a = new NodePlusChildren { n = new Node(AppTask.TYPE_ANNOT) };
						a.n.Label = name;
						a.n.Detail = "name";

						hostDetailsNode.children.Add(a);
					}

					hostNode.children.Add(hostDetailsNode);


					foreach (XElement port in host.Element("ports").Elements("port"))
					{
						if(port.Element("state")?.Attribute("state")?.Value == "open")
						{
							NodePlusChildren portNode = new NodePlusChildren { n = new Node(AppTask.TYPE_PORT) };
							string protocol = port.Attribute("protocol").Value;
							string portid = port.Attribute("portid").Value;
							portNode.n.Label = $"{portid}/{protocol}";

							NodePlusChildren attachNode = new NodePlusChildren { n = new Node(AppTask.TYPE_TEXT) };

							attachNode.n.Label = "scan "+portNode.n.Label;
							attachNode.n.Detail = NMAP;
							
							string body = "";
							
							body += (port.Element("service")?.Attribute("product")?.Value+" " ?? "");
							body += (port.Element("service")?.Attribute("version")?.Value+" " ?? "");
							body += (port.Element("service")?.Attribute("extrainfo")?.Value+" " ?? "");
							body += (port.Element("service")?.Attribute("ostype")?.Value+" " ?? "");
							
							body = body.Trim();
							
							string UNKNOWN_SERVICE = "Unknown Service";
							if( body == "")
								body = UNKNOWN_SERVICE;

							foreach (XElement script in port.Elements("script"))
							{
								body += "\n\n";
								string scriptname = "NSE Script: "+(script.Attribute("id")?.Value ?? "<?>");
								body += scriptname+"\n";
								body += new string ('-', scriptname.Length);
								body += "\nOutput:\n";
								body += (script.Attribute("output")?.Value ?? "<?>")+"\n\n";
								body += "Additional Details:\n";
								foreach (XElement el in script.Elements())
								{
									if(el.Name.ToString() == "table")
										foreach (XElement elem in el.Elements())
											body += " [*] "+(elem.Attribute("key")?.Value+": " ?? "") + elem.Value + "\n";
									else
										body += (el.Attribute("key")?.Value+": " ?? "") + el.Value + "\n";
								}
							}

							if(body != UNKNOWN_SERVICE)
							{
								attachNode.n.TextData = body;

								//this needs to be re-thought, the service attribute is just based on the portnum
								// need to analyse the fingerprint and script output to intelligently classify it
								
								//string service = port.Element("service")?.Attribute("name")?.Value;
								// tag portNode eg. http tls_ssl  smb, vnc etc., 
								//if(service != null && service != "unknown")
								//	portNode.n.AddProtocolTag(service);
								
							}
							else
							{
								attachNode.n.Type = AppTask.TYPE_ANNOT;
								attachNode.n.Label += " (unknown)";
							}
							
							portNode.children.Add(attachNode);
							hostNode.children.Add(portNode);
						}
					}

					string upsertGroupNodeID = parentID;

					ServerResponse searchResult = await dbmgr.QueryAsync(
						uidsOfParentNodes: new List<string> { rootID }, 
						field: PropsJson.Label, 
						op: "eq", 
						val: hostNode.n.Label, 
						recurseDepth: 10,
						nodeType: AppTask.TYPE_HOST
						);
					
					if(searchResult?.nodes?.Count > 0)
					{
						// use its id for item children
						hostNode.n.UID = searchResult.nodes[0].UID;
						// check if each port already exists for host

						// need to fix this. also need to fetch the host details (TYPE_CAT)
						// maybe just do a recursive search for all nodes under the existing host?
						// will have to fetch items too if adding overwrite or append mode
						ServerResponse searchResultForHostChildren = await dbmgr.QueryAsync(
							uidsOfParentNodes: new List<string> { hostNode.n.UID }, 
							recurseDepth: DatabaseManager.MAX_RECURSE_DEPTH
							);

						if(searchResultForHostChildren?.nodes?.Count > 0)
						{
							Dictionary<string,NodePlusChildren> lookupChildrenByLabel = new Dictionary<string,NodePlusChildren>();
							foreach (NodePlusChildren newN in hostNode.children)
								lookupChildrenByLabel[newN.n.Label+newN.n.Type] = newN;

							foreach (Node existingN in searchResultForHostChildren.nodes)
							{
								if(lookupChildrenByLabel.ContainsKey(existingN.Label+existingN.Type))
									lookupChildrenByLabel[existingN.Label+existingN.Type].n.UID = existingN.UID;
							}
							//   depending on ingest type (overwrite, append, new)
							//   query for all existing attachments for item
							//   (overwrite, append, new)
							//   todo overwrite, append
						}

					}

					//upsert everything for this host
					//ports to host, attachments to port
					List<Node> upsertList = new List<Node>();
					try 
					{
						
						//if hostNode.n.UID != null then we dont need to set a parent because it already exists and we're leaving it where it is
						//else 
						if(hostNode.n.UID == null)
						{
							hostNode.n.AddParent(upsertGroupNodeID);
							hostNode.n.UID = hostNode.n.Label;
						}
						upsertList.Add(hostNode.n);

						foreach (NodePlusChildren portsOrDetailsN in hostNode.children)
						{
							//	if they don't have a uid set, 
							if(portsOrDetailsN.n.UID == null)
							{
								// set it to child.n.Label
								portsOrDetailsN.n.UID = portsOrDetailsN.n.Label;
								portsOrDetailsN.n.AddParent(hostNode.n.UID);
							}
							upsertList.Add(portsOrDetailsN.n);

							foreach (NodePlusChildren attachmentN in portsOrDetailsN.children)
							{
								attachmentN.n.AddParent(portsOrDetailsN.n.UID);
								upsertList.Add(attachmentN.n);
							}
						}

						//can do bulk update of all the nodes in one list
						List<string> upsertUids = await dbmgr.UpsertNodesAsync(upsertList);
						LogService.Log(LOGLEVEL.DEBUG,"XMLImportNmap: upsert UIDs: "+String.Join(",",upsertUids));
					}
					catch (Exception e)
					{
						LogService.Log(LOGLEVEL.ERROR,"XMLImportNmap: update failed: "+e.ToString());
						continue;
					}

				}
			}
			
			return;
		}
	}
}