using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace YtMusic.App.Platform;

public static class UiHelpers
{
	/// <summary>
	/// Finds the data item behind a control inside a list. ItemsRepeater runs compiled x:Bind
	/// templates without setting DataContext, so the item is read from the repeater by position.
	/// </summary>
	public static object? ItemOf(object sender)
	{
		DependencyObject? element = sender as DependencyObject;
		while (element != null)
		{
			DependencyObject parent = VisualTreeHelper.GetParent(element);
			if (parent is ItemsRepeater itemsRepeater && element is UIElement element2)
			{
				int elementIndex = itemsRepeater.GetElementIndex(element2);
				return (elementIndex >= 0) ? itemsRepeater.ItemsSourceView.GetAt(elementIndex) : null;
			}
			element = parent;
		}
		return (sender as FrameworkElement)?.DataContext;
	}

	public static ImageSource? Image(string? url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result) || (!(result.Scheme == Uri.UriSchemeHttps) && !(result.Scheme == "ms-appx")))
		{
			return null;
		}
		return new BitmapImage(result);
	}

	public static string Duration(TimeSpan? t)
	{
		if (t.HasValue)
		{
			if (!(t.Value.TotalHours >= 1.0))
			{
				return t.Value.ToString("m\\:ss");
			}
			return t.Value.ToString("h\\:mm\\:ss");
		}
		return "";
	}

	public static string Count(int? n)
	{
		if (n.HasValue)
		{
			if (n.GetValueOrDefault() != 1)
			{
				return $"{n} itens";
			}
			return "1 item";
		}
		return "";
	}

	public static string PlayGlyph(bool isPlaying)
	{
		if (!isPlaying)
		{
			return "\ue768";
		}
		return "\ue769";
	}

	public static Visibility Not(bool value)
	{
		if (!value)
		{
			return Visibility.Visible;
		}
		return Visibility.Collapsed;
	}

	public static Visibility Visible(bool value)
	{
		if (!value)
		{
			return Visibility.Collapsed;
		}
		return Visibility.Visible;
	}
}
