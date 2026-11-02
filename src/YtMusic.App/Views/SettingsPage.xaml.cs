using Microsoft.UI.Xaml.Controls;
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
	}
}
