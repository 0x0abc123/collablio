using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace collablio
{
    public class MessageQueue
    {
		protected ConcurrentQueue<Message> _messages = new ConcurrentQueue<Message>();
		protected SemaphoreSlim _messagesAvailable = new SemaphoreSlim(0);
		
		public void Enqueue(Message message)
		{
			// pass the sempaphore increment message
			_messages.Enqueue(message);
			// signal messages available
			_messagesAvailable.Release(1);
		}

		public void Enqueue(string message)
		{
			Enqueue(new Message { content = message });
		}
		
		public Message Dequeue()
		{
			while (true)
			{
				_messagesAvailable.Wait();
				Message message;
				// we use TryDequeue here because we're multithreaded
				// so it's possible that between the Wait() call
				// and the dequeue call the queue may be cleared
				if (_messages.TryDequeue(out message))
					return message;
			}
		}

		public async Task<Message> DequeueAsync()
		{
			while (true)
			{
				await _messagesAvailable.WaitAsync();
				Message message;
				// we use TryDequeue here because we're multithreaded
				// so it's possible that between the Wait() call
				// and the dequeue call the queue may be cleared
				if (_messages.TryDequeue(out message))
					return message;
			}
		}

    }
}
