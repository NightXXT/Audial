using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
	}

	private async void OnCheckUpdates(object sender, RoutedEventArgs e)
	{
		UpdateButton.IsEnabled = false;
		VersionText.Text = "Procurando atualizações…";
		VersionText.Text = await ((App)Application.Current).CheckForUpdatesAsync();
		UpdateButton.IsEnabled = true;
	}
}
