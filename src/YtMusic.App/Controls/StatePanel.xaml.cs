using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using WinRT;
using YtMusic.Presentation.ViewModels;

namespace YtMusic.App.Controls;
public sealed partial class StatePanel : UserControl
{
	public static readonly DependencyProperty StateProperty = DependencyProperty.Register("State", typeof(ViewState), typeof(StatePanel), new PropertyMetadata(ViewState.Loading, OnChanged));

	public static readonly DependencyProperty MessageProperty = DependencyProperty.Register("Message", typeof(string), typeof(StatePanel), new PropertyMetadata("", OnChanged));

	public static readonly DependencyProperty RetryCommandProperty = DependencyProperty.Register("RetryCommand", typeof(ICommand), typeof(StatePanel), new PropertyMetadata(null));

	public static readonly DependencyProperty SignInCommandProperty = DependencyProperty.Register("SignInCommand", typeof(ICommand), typeof(StatePanel), new PropertyMetadata(null));

















	public ViewState State
	{
		get
		{
			return (ViewState)GetValue(StateProperty);
		}
		set
		{
			SetValue(StateProperty, value);
		}
	}

	public string Message
	{
		get
		{
			return (string)GetValue(MessageProperty);
		}
		set
		{
			SetValue(MessageProperty, value);
		}
	}

	public ICommand? RetryCommand
	{
		get
		{
			return (ICommand)GetValue(RetryCommandProperty);
		}
		set
		{
			SetValue(RetryCommandProperty, value);
		}
	}

	public ICommand? SignInCommand
	{
		get
		{
			return (ICommand)GetValue(SignInCommandProperty);
		}
		set
		{
			SetValue(SignInCommandProperty, value);
		}
	}

	public StatePanel()
	{
		InitializeComponent();
		Update();
	}

	private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((StatePanel)d).Update();
	}

	private void Update()
	{
		ViewState state = State;
		base.Visibility = ((state == ViewState.Content) ? Visibility.Collapsed : Visibility.Visible);
		LoadingPanel.Visibility = ((state != 0) ? Visibility.Collapsed : Visibility.Visible);
		bool flag = (uint)(state - 2) <= 3u;
		bool flag2 = flag;
		MessagePanel.Visibility = ((!flag2) ? Visibility.Collapsed : Visibility.Visible);
		MessageText.Text = Message;
		FontIcon icon = Icon;
		icon.Glyph = state switch
		{
			ViewState.Error => "\ue783", 
			ViewState.SessionExpired => "\ue72e", 
			ViewState.SignedOut => "\ue77b", 
			_ => "\ue946", 
		};
		RetryButton.Visibility = ((state != ViewState.Error) ? Visibility.Collapsed : Visibility.Visible);
		Button signInButton = SignInButton;
		flag = (uint)(state - 4) <= 1u;
		signInButton.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void OnRetry(object sender, RoutedEventArgs e)
	{
		ICommand? retryCommand = RetryCommand;
		if (retryCommand != null && retryCommand.CanExecute(null))
		{
			RetryCommand.Execute(null);
		}
	}

	private void OnSignIn(object sender, RoutedEventArgs e)
	{
		ICommand? signInCommand = SignInCommand;
		if (signInCommand != null && signInCommand.CanExecute(null))
		{
			SignInCommand.Execute(null);
		}
	}






}
