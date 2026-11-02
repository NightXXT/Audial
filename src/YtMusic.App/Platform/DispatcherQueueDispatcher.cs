using System;
using Microsoft.UI.Dispatching;
using YtMusic.Core.Abstractions;

namespace YtMusic.App.Platform;

public sealed class DispatcherQueueDispatcher : IUiDispatcher
{
	private readonly DispatcherQueue _queue;

	public DispatcherQueueDispatcher(DispatcherQueue queue)
	{
		_queue = queue;
	}

	public void Post(Action action)
	{
		Action action2 = action;
		if (_queue.HasThreadAccess)
		{
			action2();
			return;
		}
		_queue.TryEnqueue(delegate
		{
			action2();
		});
	}
}
