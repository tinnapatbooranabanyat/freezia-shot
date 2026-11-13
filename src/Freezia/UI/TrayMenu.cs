using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Freezia.Capture;
using Freezia.Native;

namespace Freezia.UI;

/// <summary>Themed popup menu shown from the tray icon. Closes when it loses focus.</summary>
public sealed class TrayMenu : Window
{
    // Small path icons (20x20 box)
    public const string IconRegion = "M3,7 V3 H7 M13,3 H17 V7 M17,13 V17 H13 M7,17 H3 V13 M8,10 H12 M10,8 V12";
    public const string IconScreen = "M2.5,3.5 H17.5 V13.5 H2.5 Z M7,17 H13 M10,13.5 V17";
    public const string IconAll = "M1.5,4 H10 V11 H1.5 Z M12,6 H18.5 V13 H12 Z M4,14.5 H8 M13.5,16 H17";

    private readonly StackPanel _items = new();
    private bool _closing;

    public TrayMenu()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, "UiFont");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var card = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            MinWidth = 250,
            Effect = (System.Windows.Media.Effects.Effect)FindResource("CardShadow"),
            Child = _items,
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = card;

        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this);
        Deactivated += (_, _) => SafeClose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
    }

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    public void AddItem(string pathData, string text, string shortcut, Action action, bool bold = false)
    {
        var icon = new Path
        {
            Data = Geometry.Parse(pathData),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        icon.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        Add(icon, text, shortcut, action, bold);
    }

    public void AddGlyphItem(string glyph, string text, Action action)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        Add(icon, text, "", action, false);
    }

    private void Add(FrameworkElement icon, string text, string shortcut, Action action, bool bold)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);

        var label = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (!string.IsNullOrEmpty(shortcut))
        {
            var key = new TextBlock { Text = shortcut, FontSize = 11.5, Margin = new Thickness(24, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            key.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            Grid.SetColumn(key, 2);
            grid.Children.Add(key);
        }

        var button = new Button
        {
            Content = grid,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Template = ItemTemplate(),
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
        };
        button.Click += (_, _) =>
        {
            SafeClose();
            action();
        };
        _items.Children.Add(button);
    }

    public void AddSeparator()
    {
        var line = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) };
        line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        _items.Children.Add(line);
    }

    private static ControlTemplate ItemTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bd.SetValue(Border.PaddingProperty, new Thickness(10, 7, 10, 7));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBrush"), "Bd"));
        t.Triggers.Add(hover);
        return t;
    }

    /// <summary>Show next to the mouse, like a native tray menu (above the cursor near a bottom taskbar).</summary>
    public void ShowAtCursor()
    {
        var (cx, cy) = Monitors.CursorPosition();
        var mon = Monitors.FromPoint(cx, cy);
        Left = -10000;
        Top = -10000;
        Show();
        Place(cx, cy, mon);
        // A DPI change after moving to another monitor re-measures the window: place again.
        Dispatcher.BeginInvoke(() => Place(cx, cy, mon), System.Windows.Threading.DispatcherPriority.Loaded);
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
    }

    private void Place(int cx, int cy, MonitorInfo mon)
    {
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Ceiling(ActualWidth * s), h = (int)Math.Ceiling(ActualHeight * s);
        int margin = (int)(12 * s); // transparent shadow margin
        var wa = mon.WorkArea;
        int x = cx - margin;
        int y = cy - h + margin;                         // open upwards
        if (y < wa.Y) y = cy - margin;                   // not enough room: open downwards
        x = Math.Clamp(x, wa.X - margin, wa.Right - w + margin);
        y = Math.Clamp(y, wa.Y - margin, wa.Bottom - h + margin);
        WindowHelper.SetPhysicalBounds(this, new PxRect(x, y, w, h));
    }
}
