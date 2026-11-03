using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using YtMusic.App.Platform;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Views;
public sealed partial class SettingsPage : Page
{
	public SettingsViewModel ViewModel { get; }

	public ShellViewModel Shell { get; }

	public SettingsPage()
	{
		ViewModel = App.GetService<SettingsViewModel>();
		Shell = App.GetService<ShellViewModel>();
		InitializeComponent();
		VersionText.Text = $"Versão {App.GetService<UpdateService>().CurrentVersion}. O Audial procura novidades ao abrir.";
		OverlayUrl.Text = LiveOverlayService.Url;
	}

	private void OnCopyOverlay(object sender, RoutedEventArgs e)
	{
		DataPackage dataPackage = new DataPackage();
		dataPackage.SetText(LiveOverlayService.Url);
		Clipboard.SetContent(dataPackage);
	}

	private async void OnCheckUpdates(object sender, RoutedEventArgs e)
	{
		UpdateButton.IsEnabled = false;
		VersionText.Text = "Procurando atualizações…";
		VersionText.Text = await ((App)Application.Current).CheckForUpdatesAsync();
		UpdateButton.IsEnabled = true;
	}
}
