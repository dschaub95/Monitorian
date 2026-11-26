using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using Monitorian.Core.Models;
using Monitorian.Core.ViewModels;

namespace Monitorian.Core.Views;

public partial class BrightnessScheduleWindow : Window
{
	public BrightnessScheduleViewModel ViewModel => (BrightnessScheduleViewModel)this.DataContext;

	private static BrightnessScheduleWindow _current;

	/// <summary>
	/// Shows the editor, reusing the one already open.
	/// </summary>
	public static void ShowSingle(Settings settings)
	{
		if (_current is null)
		{
			_current = new BrightnessScheduleWindow(settings);
			_current.Closed += (_, _) => _current = null;
			_current.Show();
		}
		else if (_current.WindowState is WindowState.Minimized)
		{
			_current.WindowState = WindowState.Normal;
		}
		_current.Activate();
	}

	public BrightnessScheduleWindow(Settings settings)
	{
		LanguageService.Switch();

		InitializeComponent();

		this.DataContext = new BrightnessScheduleViewModel(settings);
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);

		// Match the title bar to the app's theme.
		if (TryFindResource("App.Background.Plain") is SolidColorBrush { Color: var color })
		{
			var isDark = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) < 128;
			int value = isDark ? 1 : 0;
			var handle = new WindowInteropHelper(this).Handle;
			DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
		}
	}

	private void Add_Click(object sender, RoutedEventArgs e)
	{
		ViewModel.Add();

		// Focus the time box of the new entry.
		Dispatcher.BeginInvoke(new Action(() =>
		{
			var container = EntryList.ItemContainerGenerator.ContainerFromIndex(EntryList.Items.Count - 1) as FrameworkElement;
			if (FindChild<TextBox>(container) is { } box)
			{
				box.Focus();
				box.SelectAll();
			}
		}), System.Windows.Threading.DispatcherPriority.Loaded);
	}

	private void Remove_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is BrightnessScheduleEntryViewModel entry)
			ViewModel.Remove(entry);
	}

	private void TimeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is BrightnessScheduleEntryViewModel entry)
			entry.NormalizeTimeText();
	}

	private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (sender is Slider slider)
		{
			slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, slider.Value + Math.Sign(e.Delta) * 5));
			e.Handled = true;
		}
	}

	private void Cancel_Click(object sender, RoutedEventArgs e) => this.Close();

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		if (ViewModel.Save())
			this.Close();
	}

	protected override void OnClosed(EventArgs e)
	{
		ViewModel.Dispose();
		base.OnClosed(e);
	}

	private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
	{
		if (parent is null)
			return null;

		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is T found)
				return found;
			if (FindChild<T>(child) is { } descendant)
				return descendant;
		}
		return null;
	}

	#region Win32

	private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

	[DllImport("Dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

	#endregion
}
