using System;

namespace YtMusic.Core.Abstractions;

public interface IUiDispatcher
{
	void Post(Action action);
}
