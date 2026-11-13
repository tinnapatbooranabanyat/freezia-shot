using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Freezia.Core;
using Freezia.Native;

namespace Freezia.UI;

/// <summary>Shows OCR / QR results: an editable text box, Copy, and (for links) Open link.</summary>
public sealed class TextResultWindow : Window
{
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
        FontSize = 14,
        MinHeight = 160,
        Padding = new Thickness(10, 8, 10, 8),
    };
    private readonly Button _copy = new() { MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _open = new() { MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _close = new() { MinWidth = 96, IsCancel = true };
    private string? _link;

    public TextResultWindow(string title, BitmapSource? thumb)
    {
        Title = title;
        Width = 560;
        Height = 420;
        MinWidth = 380;
        MinHeight = 260;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontFamilyProperty, "UiFont");
        Icon = new BitmapImage(new Uri("pack://application:,,,/Freezia;component/Assets/app.ico"));
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _copy.Style = (Style)FindResource("AccentButton");
        _copy.Content = Strings.ActionCopy;
        _open.Content = Strings.OpenLink;
        _close.Content = Strings.Close;
        _copy.Click += (_, _) => CopyText(showToast: true);
        _open.Click += (_, _) => OpenLink();
        _close.Click += (_, _) => Close();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (thumb != null)
        {
            var img = new Image { Source = thumb, MaxHeight = 56, MaxWidth = 140, Stretch = Stretch.Uniform, Margin = new Thickness(12, 0, 0, 0) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            DockPanel.SetDock(img, Dock.Right);
            header.Children.Add(img);
        }
        var titleText = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(titleText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_close);
        buttons.Children.Add(_open);
        buttons.Children.Add(_copy);

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(_status);
        root.Children.Add(buttons);
        root.Children.Add(_text);
        Content = root;

        WindowHelper.ApplyChrome(this);
        WindowHelper.CenterOnCursorMonitor(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && _text.SelectionLength == 0)
            {
                CopyText(showToast: true);
                e.Handled = true;
            }
        };
    }

    public void ShowBusy(string message)
    {
        _status.Text = message;
        _text.Text = "";
        _text.IsEnabled = false;
        _copy.IsEnabled = false;
    }

    /// <summary>Show the result and copy it to the clipboard right away.</summary>
    public void ShowResult(string status, string text, string? link = null)
    {
        _status.Text = status;
        _text.Text = text;
        _text.IsEnabled = true;
        _copy.IsEnabled = text.Length > 0;
        _link = link;
        _open.Visibility = link != null ? Visibility.Visible : Visibility.Collapsed;
        if (text.Length > 0) CopyText(showToast: false);
        _text.Focus();
    }

    public void ShowError(string message)
    {
        _status.Text = message;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        _text.IsEnabled = false;
        _copy.IsEnabled = false;
    }

    private void CopyText(bool showToast)
    {
        string t = _text.SelectionLength > 0 ? _text.SelectedText : _text.Text;
        if (t.Length == 0) return;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                Clipboard.SetText(t);
                if (showToast) ToastWindow.ShowToast(Strings.TextCopied);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
    }

    /// <summary>Only when the user clicks: Freezia itself never connects anywhere.</summary>
    private void OpenLink()
    {
        if (_link == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_link) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("Open link failed", ex);
        }
    }

    public void ShowAndActivate()
    {
        Show();
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
    }
}
