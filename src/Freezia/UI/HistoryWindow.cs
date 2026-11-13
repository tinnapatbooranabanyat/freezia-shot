using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Freezia.Capture;
using Freezia.Core;

namespace Freezia.UI;

/// <summary>Grid of recent captures: open in editor, copy, save as, pin, show in folder, delete.</summary>
public sealed class HistoryWindow : Window
{
    private readonly CaptureController _controller;
    private readonly WrapPanel _grid = new() { Margin = new Thickness(14, 4, 14, 14) };
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _count = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 0, 0) };
    private readonly TextBlock _empty = new() { FontSize = 14, Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button _openFolder = new() { Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _clear = new();

    public HistoryWindow(CaptureController controller)
    {
        _controller = controller;
        Width = 900;
        Height = 640;
        MinWidth = 520;
        MinHeight = 360;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontFamilyProperty, "UiFont");
        Icon = new BitmapImage(new Uri("pack://application:,,,/Freezia;component/Assets/app.ico"));
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _count.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _openFolder.Click += (_, _) => ToastWindow.RevealInExplorer(HistoryService.Dir);
        _clear.Click += (_, _) =>
        {
            if (ConfirmDialog.Show(this, Strings.HistoryClearConfirm, Strings.HistoryClear, Strings.PickerCancel)) HistoryService.Clear();
        };

        var header = new DockPanel { Margin = new Thickness(18, 14, 18, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_openFolder);
        right.Children.Add(_clear);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_title);
        left.Children.Add(_count);
        header.Children.Add(left);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Children = { _empty, _grid } } };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(scroll);
        Content = root;

        ApplyLanguage();
        Loc.Changed += ApplyLanguage;
        HistoryService.Changed += Reload;
        Closed += (_, _) =>
        {
            Loc.Changed -= ApplyLanguage;
            HistoryService.Changed -= Reload;
        };
        WindowHelper.ApplyChrome(this);
        WindowHelper.CenterOnCursorMonitor(this);
        Loaded += (_, _) => Reload();
    }

    private void ApplyLanguage()
    {
        Title = Strings.HistoryTitle;
        _title.Text = Strings.HistoryTitle;
        _openFolder.Content = Strings.TrayOpenFolder;
        _clear.Content = Strings.HistoryClear;
        _empty.Text = Strings.HistoryEmpty;
        if (IsLoaded) Reload();
    }

    private void Reload()
    {
        _grid.Children.Clear();
        var list = HistoryService.List();
        _count.Text = string.Format(Strings.HistoryCount, list.Count);
        _empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _clear.IsEnabled = list.Count > 0;
        foreach (var e in list) _grid.Children.Add(Card(e));
    }

    private FrameworkElement Card(HistoryService.Entry e)
    {
        var img = new Image { Height = 120, Width = 200, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        string size = "";
        try
        {
            img.Source = HistoryService.Thumbnail(e.Path, 400);
            // read only the header, and close the file right away so it can be deleted later
            using var fs = System.IO.File.OpenRead(e.Path);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            size = $"{decoder.Frames[0].PixelWidth} × {decoder.Frames[0].PixelHeight}";
        }
        catch (Exception ex)
        {
            Logger.Error("History thumbnail failed", ex);
        }

        var time = new TextBlock { Text = e.Time.ToString("d MMM yyyy  HH:mm:ss", Loc.Language == AppLanguage.English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("th-TH")), FontSize = 12 };
        var dims = new TextBlock { Text = size, FontSize = 11 };
        dims.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var stack = new StackPanel();
        var imgHost = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = img, Margin = new Thickness(0, 0, 0, 8) };
        imgHost.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");
        stack.Children.Add(imgHost);
        stack.Children.Add(time);
        stack.Children.Add(dims);

        var card = new Border
        {
            Width = 224,
            Margin = new Thickness(6),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = stack,
            ToolTip = Strings.HistoryCardTip,
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        card.MouseLeftButtonDown += (_, ev) => { if (ev.ClickCount == 2) Open(e.Path); };

        var menu = new ContextMenu();
        void Add(string text, Action act)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        Add(Strings.ActionEdit, () => Open(e.Path));
        Add(Strings.ActionCopy, () => WithImage(e.Path, b => _controller.Copy(b)));
        Add(Strings.ActionSaveAs, () => WithImage(e.Path, b => _controller.SaveAs(b, this)));
        Add(Strings.ActionPin, () => WithImage(e.Path, b => _controller.Pin(PixelImage.FromBitmapSource(b), atCapturePosition: false)));
        Add(Strings.ActionOcr, () => WithImage(e.Path, b => _controller.Ocr(PixelImage.FromBitmapSource(b))));
        Add(Strings.HistoryShowFile, () => ToastWindow.RevealInExplorer(e.Path));
        Add(Strings.Delete, () => HistoryService.Delete(e.Path));
        card.ContextMenu = menu;
        return card;
    }

    private void WithImage(string path, Action<BitmapSource> act)
    {
        try
        {
            act(HistoryService.Load(path));
        }
        catch (Exception ex)
        {
            Logger.Error("History open failed", ex);
            ToastWindow.ShowToast(Strings.ErrorTitle, ex.Message, error: true);
        }
    }

    private void Open(string path) => WithImage(path, b => _controller.OpenEditor?.Invoke(PixelImage.FromBitmapSource(b)));
}
