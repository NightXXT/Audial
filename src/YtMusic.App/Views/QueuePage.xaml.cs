using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using YtMusic.App.Platform;
using YtMusic.Core.Models;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Views;
public sealed partial class QueuePage : Page
{














	public QueueViewModel ViewModel { get; }

	public QueuePage()
	{
		ViewModel = App.GetService<QueueViewModel>();
		InitializeComponent();
	}

	private void OnItemClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is Track parameter)
		{
			ViewModel.PlayIndexCommand.Execute(parameter);
		}
	}

	private void OnRemoveClick(object sender, RoutedEventArgs e)
	{
		if (UiHelpers.ItemOf(sender) is Track dataContext)
		{
			ViewModel.RemoveCommand.Execute(dataContext);
		}
	}






}
