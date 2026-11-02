using System;
using System.Collections.Generic;
using YtMusic.Core.Models;

namespace YtMusic.Core.Playback;

public sealed class PlaybackQueue
{
	private readonly List<Track> _items = new List<Track>();

	public IReadOnlyList<Track> Items => _items;

	public int CurrentIndex { get; private set; } = -1;


	public Track? Current
	{
		get
		{
			if (CurrentIndex < 0 || CurrentIndex >= _items.Count)
			{
				return null;
			}
			return _items[CurrentIndex];
		}
	}

	public bool HasNext => CurrentIndex + 1 < _items.Count;

	public bool HasPrevious => CurrentIndex > 0;

	public event EventHandler? Changed;

	public void Replace(IEnumerable<Track> tracks, int startIndex)
	{
		_items.Clear();
		_items.AddRange(tracks);
		CurrentIndex = ((_items.Count == 0) ? (-1) : Math.Clamp(startIndex, 0, _items.Count - 1));
		this.Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Add(Track track)
	{
		_items.Add(track);
		this.Changed?.Invoke(this, EventArgs.Empty);
	}

	public bool MoveTo(int index)
	{
		if (index < 0 || index >= _items.Count)
		{
			return false;
		}
		CurrentIndex = index;
		this.Changed?.Invoke(this, EventArgs.Empty);
		return true;
	}

	public bool MoveNext()
	{
		return MoveTo(CurrentIndex + 1);
	}

	public bool MovePrevious()
	{
		if (CurrentIndex > 0)
		{
			return MoveTo(CurrentIndex - 1);
		}
		return false;
	}

	public void RemoveAt(int index)
	{
		if (index >= 0 && index < _items.Count)
		{
			_items.RemoveAt(index);
			if (index < CurrentIndex)
			{
				CurrentIndex--;
			}
			else if (index == CurrentIndex && CurrentIndex >= _items.Count)
			{
				CurrentIndex = _items.Count - 1;
			}
			this.Changed?.Invoke(this, EventArgs.Empty);
		}
	}

	public void Clear()
	{
		_items.Clear();
		CurrentIndex = -1;
		this.Changed?.Invoke(this, EventArgs.Empty);
	}
}
