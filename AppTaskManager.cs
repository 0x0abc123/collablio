using System;
using System.Collections.Generic;
using collablio.Models;
using collablio.AppTasks;
using System.Threading;
using System.Threading.Tasks;

namespace collablio
{	

	class AppTaskManager
	{
		private Dictionary<string,AppTask> _tasks = new Dictionary<string,AppTask>();
		private readonly object _tasksLock = new object();
		private Dictionary<string,Action<object>> actions = new Dictionary<string,Action<object>>();

		private static AppTaskManager _singleton = new AppTaskManager();
		public static AppTaskManager Instance()
		{
			return _singleton;			
		}
		
		public AppTaskManager()
		{
			//any database activity needs to run in an async task
			//async example
			actions["test1"] = async (msg) => {
				//if you need to talk to the database:
				//DatabaseManager dbmgr = DatabaseManager.Instance();
				await Task.Delay(2000);
				string formattedInput = String.Format(
				"AppTaskTest1={0}, Task={1}, Thread={2}",
				msg, Task.CurrentId, Thread.CurrentThread.ManagedThreadId
				);
				LogService.Log(LOGLEVEL.DEBUG,formattedInput);
			};
			//synchronous example
			actions["test2"] = (msg) => {
				Thread.Sleep(2000);
				string formattedInput = String.Format(
				"AppTaskTest2={0}, Task={1}, Thread={2}",
				msg, Task.CurrentId, Thread.CurrentThread.ManagedThreadId
				);
				LogService.Log(LOGLEVEL.DEBUG,formattedInput);
			}; 
			//XMLImportNmap
			actions["import_nmap_xml"] = async (msg) => {
				await XMLImportNmap.Run((AppTask)msg);
				string formattedInput = String.Format(
				"AppTask XMLImportNmap={0}, Task={1}, Thread={2}",
				msg, Task.CurrentId, Thread.CurrentThread.ManagedThreadId
				);
				LogService.Log(LOGLEVEL.DEBUG,formattedInput);
			};
			//file upload
			actions["file_upload"] = async (msg) => {
				await FileUpload.Run((AppTask)msg);
				string formattedInput = String.Format(
				"AppTask FileUpload={0}, Task={1}, Thread={2}",
				msg, Task.CurrentId, Thread.CurrentThread.ManagedThreadId
				);
				LogService.Log(LOGLEVEL.DEBUG,formattedInput);
			};

		}
		
		private void AddOrUpdateTask(AppTask _task)
		{
			//lock(_tasksLock)
			//{
			//	_tasks[_task.getLabel()] = _task;
			//}
		}

		public void AddTask(string x)
		{			
			//AppTask at = new AppTask();
			//at.setLabel(x);
			//AddOrUpdateTask(at);
		}


		public string RunAppTask(AppTask _task)
		{
			//create a holder class TaskHolder { AppTask a, Task t } 
			// generate a taskID
			//  create a dict<string,TaskHolder> as the master task index
			//  can then cancel tasks, get status etc.
			// if tasks are accessing the index then need to synchronise it with semaphoreslim
			try 
			{
				// the action parameter is read from AppTask.type
				if(actions.ContainsKey(_task.type))
				{
					Action<object> selectedAction = actions[_task.type];
					Task t1 = Task.Factory.StartNew(selectedAction,_task);
					//put the task object in the task index
					return "OK";
				}
				else
				{
					return String.Format("Action: {0} not found",_task.type);
				}
			}
			catch (Exception e)
			{
				return e.ToString();
			}
		}
	}
}