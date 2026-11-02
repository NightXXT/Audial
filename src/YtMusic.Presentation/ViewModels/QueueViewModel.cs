using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtMusic.Core.Abstractions;
using YtMusic.Core.Models;
using YtMusic.Core.Playback;

namespace YtMusic.Presentation.ViewModels;

public sealed class QueueViewModel : ObservableObject
{
	private readonly PlaybackCoordinator _playback;

	private readonly IUiDispatcher _ui;

	public ObservableCollection<Track> Items { get; } = new ObservableCollection<Track>();


	public IAsyncRelayCommand<Track> PlayIndexCommand { get; }

	public IAsyncRelayCommand<Track> RemoveCommand { get; }

	public IAsyncRelayCommand ClearCommand { get; }

	public ObservableCollection<Track> UpNext { get; } = new ObservableCollection<Track>();


	public bool IsEmpty => Items.Count == 0;

	public string CurrentTitle => _playback.Queue.Current?.Title ?? "";

	public Track? Current => _playback.Queue.Current;

	public bool HasCurrent => (object)Current != null;

	public string Position => $"Faixa {_playback.Queue.CurrentIndex + 1:00} de {Items.Count}";

	public string Summary
	{
		get
		{
			if (Items.Count == 0)
			{
				return "Nenhuma faixa preparada";
			}
			return (Items.Count == 1) ? "1 faixa preparada" : $"{Items.Count} faixas preparadas";
		}
	}

	public QueueViewModel(PlaybackCoordinator playback, IUiDispatcher ui)
	{
		_playback = playback;
		_ui = ui;
		_playback.Queue.Changed += delegate
		{
			_ui.Post(Refresh);
		};
		PlayIndexCommand = new AsyncRelayCommand<Track>((Track? t) => ((object)t != null) ? _playback.PlayIndexAsync(Items.IndexOf(t)) : Task.CompletedTask);
		RemoveCommand = new AsyncRelayCommand<Track>((Track? t) => ((object)t != null) ? _playback.RemoveAsync(Items.IndexOf(t)) : Task.CompletedTask);
		ClearCommand = new AsyncRelayCommand(_playback.ClearAsync);
		Refresh();
	}

	private void Refresh()
	{
		Items.Clear();
		foreach (Track item in _playback.Queue.Items)
		{
			Items.Add(item);
		}
		UpNext.Clear();
		for (int i = _playback.Queue.CurrentIndex + 1; i < Items.Count; i++)
		{
			UpNext.Add(Items[i]);
		}
		OnPropertyChanged("IsEmpty");
		OnPropertyChanged("CurrentTitle");
		OnPropertyChanged("Current");
		OnPropertyChanged("HasCurrent");
		OnPropertyChanged("Position");
		OnPropertyChanged("Summary");
	}
}
