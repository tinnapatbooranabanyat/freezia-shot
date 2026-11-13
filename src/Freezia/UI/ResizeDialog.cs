using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Freezia.UI;

/// <summary>Pick a new image size in pixels or percent, optionally keeping the aspect ratio.</summary>
public sealed class ResizeDialog : Window
{
    private readonly int _w0, _h0;
    private readonly TextBox _w = new() { Width = 90 }, _h = new() { Width = 90 }, _pct = new() { Width = 70 };
    private readonly CheckBox _lock = new() { IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _error = new() { FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private bool _sync;

    public int ResultWidth { get; private set; }
    public int ResultHeight { get; private set; }

    public ResizeDialog(Window owner, int width, int height)
    {
        _w0 = width;
        _h0 = height;
        Owner = owner;
        Title = Strings.ResizeTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontFamilyProperty, "UiFont");
        FontSize = 13;
        UseLayoutRounding = true;

        _lock.Style = (Style)FindResource("ToggleSwitch");
        _lock.Content = Strings.ResizeKeepRatio;
        _error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        _w.Text = width.ToString(CultureInfo.InvariantCulture);
        _h.Text = height.ToString(CultureInfo.InvariantCulture);
        _pct.Text = "100";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Row(int r, string label, TextBox box, string unit)
        {
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
            var u = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            u.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            box.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(l, r); Grid.SetRow(box, r); Grid.SetRow(u, r);
            Grid.SetColumn(box, 1); Grid.SetColumn(u, 2);
            grid.Children.Add(l); grid.Children.Add(box); grid.Children.Add(u);
        }
        Row(0, Strings.ResizeWidth, _w, "px");
        Row(1, Strings.ResizeHeight, _h, "px");
        Row(2, Strings.ResizePercent, _pct, "%");

        var note = new TextBlock { Text = Strings.ResizeNote, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Margin = new Thickness(0, 12, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var ok = new Button { Content = Strings.PickerOk, MinWidth = 90, IsDefault = true, Style = (Style)FindResource("AccentButton") };
        var cancel = new Button { Content = Strings.PickerCancel, MinWidth = 90, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(grid);
        root.Children.Add(_lock);
        root.Children.Add(note);
        root.Children.Add(_error);
        root.Children.Add(buttons);
        Content = root;

        _w.TextChanged += (_, _) => FromWidth();
        _h.TextChanged += (_, _) => FromHeight();
        _pct.TextChanged += (_, _) => FromPercent();
        foreach (var b in new[] { _w, _h, _pct })
        {
            b.PreviewTextInput += (_, e) => e.Handled = !int.TryParse(e.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int _);
            b.GotKeyboardFocus += (s, _) => ((TextBox)s!).SelectAll();
        }
        WindowHelper.ApplyChrome(this);
        Loaded += (_, _) => { _w.Focus(); _w.SelectAll(); };
    }

    private static int Num(TextBox b) => int.TryParse(b.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private void Set(TextBox b, int v)
    {
        string t = v.ToString(CultureInfo.InvariantCulture);
        if (b.Text != t) b.Text = t;
    }

    private void FromWidth()
    {
        if (_sync) return;
        _sync = true;
        int w = Num(_w);
        if (w > 0 && _lock.IsChecked == true) Set(_h, Math.Max(1, (int)Math.Round(w * (double)_h0 / _w0)));
        if (w > 0) Set(_pct, (int)Math.Round(w * 100.0 / _w0));
        _sync = false;
    }

    private void FromHeight()
    {
        if (_sync) return;
        _sync = true;
        int h = Num(_h);
        if (h > 0 && _lock.IsChecked == true) Set(_w, Math.Max(1, (int)Math.Round(h * (double)_w0 / _h0)));
        if (h > 0) Set(_pct, (int)Math.Round(h * 100.0 / _h0));
        _sync = false;
    }

    private void FromPercent()
    {
        if (_sync) return;
        _sync = true;
        int p = Num(_pct);
        if (p > 0)
        {
            Set(_w, Math.Max(1, (int)Math.Round(_w0 * p / 100.0)));
            Set(_h, Math.Max(1, (int)Math.Round(_h0 * p / 100.0)));
        }
        _sync = false;
    }

    private void Accept()
    {
        int w = Num(_w), h = Num(_h);
        if (w < 1 || h < 1 || w > 20000 || h > 20000)
        {
            _error.Text = Strings.ResizeInvalid;
            return;
        }
        ResultWidth = w;
        ResultHeight = h;
        DialogResult = true;
    }
}
