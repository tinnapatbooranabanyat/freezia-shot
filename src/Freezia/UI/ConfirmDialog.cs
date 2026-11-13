using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Freezia.UI;

/// <summary>Small dark-themed Yes/No dialog (instead of the light system MessageBox).</summary>
public static class ConfirmDialog
{
    public static bool Show(Window owner, string message, string yes, string no)
    {
        bool result = false;
        var win = new Window
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont"),
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            FontSize = 14,
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
        };
        var yesBtn = new Button { Content = yes, MinWidth = 90, Style = (Style)Application.Current.FindResource("AccentButton"), IsDefault = true };
        var noBtn = new Button { Content = no, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        yesBtn.Click += (_, _) => { result = true; win.Close(); };
        noBtn.Click += (_, _) => win.Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(noBtn);
        buttons.Children.Add(yesBtn);

        var panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(buttons);

        win.Content = new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(22, 20, 22, 18),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Effect = (System.Windows.Media.Effects.Effect)Application.Current.FindResource("CardShadow"),
            Child = panel,
        };
        win.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };
        win.ShowDialog();
        return result;
    }
}
