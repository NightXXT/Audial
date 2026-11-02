using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WinRT;
using Windows.System;
using YtMusic.App.Platform;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Controls;
public sealed partial class PlayerBar : UserControl
{














	public PlayerViewModel ViewModel { get; }

	public PlayerBar()
	{
		ViewModel = App.GetService<PlayerViewModel>();
		InitializeComponent();
		GoogleMusicReceiver receiver = App.GetService<GoogleMusicReceiver>();
		PropertyChangedEventHandler handler = delegate(object? _, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "ConnectedDevice")
			{
				RemoteDeviceName.Text = receiver.ConnectedDevice;
				RemoteDevice.Visibility = (string.IsNullOrEmpty(receiver.ConnectedDevice) ? Visibility.Collapsed : Visibility.Visible);
				ToolTipService.SetToolTip(RemoteDevice, "Controlado por " + receiver.ConnectedDevice);
			}
		};
		receiver.PropertyChanged += handler;
		base.Unloaded += delegate
		{
			receiver.PropertyChanged -= handler;
		};
	}

	private void OnSeekPointerPressed(object sender, PointerRoutedEventArgs e)
	{
		ViewModel.BeginSeeking();
	}

	private async void OnSeekPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		await ViewModel.SeekToAsync(SeekSlider.Value);
	}

	private async void OnSeekKeyUp(object sender, KeyRoutedEventArgs e)
	{
		VirtualKey key = e.Key;
		if ((uint)(key - 33) <= 7u)
		{
			await ViewModel.SeekToAsync(SeekSlider.Value);
		}
	}

	private void OnQueue(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).NavigateTo("queue");
	}

	private void OnNowPlaying(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).NavigateTo("playing");
	}

	private void OnMiniPlayer(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).ShowMiniPlayer();
	}

	private void OnDevices(object sender, RoutedEventArgs e)
	{
		((App)Application.Current).NavigateTo("devices");
	}






}
