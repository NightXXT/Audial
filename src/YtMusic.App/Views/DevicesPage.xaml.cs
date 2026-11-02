using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using YtMusic.App.Platform;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Views;
public sealed partial class DevicesPage : Page
{
	private readonly GoogleMusicReceiver _receiver;













	public DevicesPage()
	{
		_receiver = App.GetService<GoogleMusicReceiver>();
		InitializeComponent();
		base.Loaded += delegate
		{
			_receiver.PropertyChanged += OnChanged;
			Refresh();
			_ = RefreshOutputAsync();
		};
		base.Unloaded += delegate
		{
			_receiver.PropertyChanged -= OnChanged;
		};
	}

	private void OnChanged(object? sender, PropertyChangedEventArgs e)
	{
		Refresh();
	}

	private void Refresh()
	{
		PairingCode.Text = _receiver.PairingCode;
		CodePanel.Visibility = (string.IsNullOrEmpty(_receiver.PairingCode) ? Visibility.Collapsed : Visibility.Visible);
		StatusText.Text = _receiver.Status;
		StartButton.IsEnabled = !_receiver.IsRunning;
		StopButton.IsEnabled = _receiver.IsRunning;
	}

	private async Task RefreshOutputAsync()
	{
		try
		{
			string id = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
			if (!string.IsNullOrEmpty(id))
			{
				OutputName.Text = (await DeviceInformation.CreateFromIdAsync(id)).Name;
			}
		}
		catch (Exception ex) when (ex is COMException || ex is UnauthorizedAccessException)
		{
		}
	}

	private void OnCopyCode(object sender, RoutedEventArgs e)
	{
		DataPackage dataPackage = new DataPackage();
		dataPackage.SetText(_receiver.PairingCode);
		Clipboard.SetContent(dataPackage);
	}

	private async void OnStart(object sender, RoutedEventArgs e)
	{
		if (App.GetService<ShellViewModel>().IsDemo)
		{
			StatusText.Text = "Abra o catálogo real para ativar o receptor. A demonstração não toca áudio.";
		}
		else
		{
			await _receiver.StartAsync(ReceiverName.Text);
		}
	}

	private void OnStop(object sender, RoutedEventArgs e)
	{
		_receiver.Stop();
	}






}
