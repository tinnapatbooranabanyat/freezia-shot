using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Freezia.Core;

namespace Freezia.UI;

/// <summary>
/// Shows a hotkey; click it and press a key combination to change it. Recording goes through the
/// global keyboard hook, so PrintScreen and Win+... combinations can be recorded too.
/// </summary>
public sealed class HotkeyBox : Border
{
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly Button _clear;
    private Hotkey _hotkey = Hotkey.None;
    private bool _recording;

    /// <summary>Validates a candidate; returns an error message or null if acceptable.</summary>
    public Func<HotkeyBox, Hotkey, string?>? Validate { get; set; }

    public event Action<HotkeyBox>? RecordingStarted;
    public event Action<HotkeyBox, string?>? Message;

    public string Label { get; set; } = "";

    public HotkeyBox()
    {
        Height = 36;
        MinWidth = 240;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 0, 4, 0);
        Cursor = Cursors.Hand;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(BorderBrushProperty, "BorderBrush");

        _clear = new Button
        {
            Style = (Style)Application.Current.FindResource("IconButton"),
            Width = 28,
            Height = 28,
            FontSize = 11,
            Content = "",
            ToolTip = Strings.HotkeyNone,
        };
        _clear.Click += (_, _) =>
        {
            StopRecording();
            Hotkey = Hotkey.None;
            Message?.Invoke(this, null);
        };

        var dock = new DockPanel();
        DockPanel.SetDock(_clear, Dock.Right);
        dock.Children.Add(_clear);
        dock.Children.Add(_text);
        Child = dock;

        MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInClearButton(d)) return;
            StartRecording();
        };
        MouseEnter += (_, _) => { if (!_recording) SetResourceReference(BorderBrushProperty, "TextDimBrush"); };
        MouseLeave += (_, _) => { if (!_recording) SetResourceReference(BorderBrushProperty, "BorderBrush"); };
        Unloaded += (_, _) =>
        {
            StopRecording();
            Loc.Changed -= Refresh;
        };
        Loaded += (_, _) => { Loc.Changed -= Refresh; Loc.Changed += Refresh; };
        Refresh();
    }

    private bool IsInClearButton(DependencyObject d)
    {
        for (var cur = d; cur != null; cur = VisualTreeHelper.GetParent(cur))
            if (cur == _clear) return true;
        return false;
    }

    public Hotkey Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value ?? Hotkey.None;
            Refresh();
        }
    }

    public bool IsRecording => _recording;

    public void StartRecording()
    {
        var hk = App.Current.Hotkeys;
        if (hk == null || _recording) return;
        RecordingStarted?.Invoke(this);
        _recording = true;
        SetResourceReference(BorderBrushProperty, "AccentBrush");
        BorderThickness = new Thickness(2);
        _text.Text = Strings.HotkeyPressKeys;
        _text.SetResourceReference(TextBlock.ForegroundProperty, "AccentHoverBrush");
        hk.BeginRecording(OnKey, OnModifiers, StopRecording, OnClear);
    }

    public void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        App.Current.Hotkeys?.EndRecording();
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        BorderThickness = new Thickness(1);
        Refresh();
    }

    private void OnModifiers(HotkeyModifiers mods)
    {
        if (!_recording) return;
        _text.Text = mods == HotkeyModifiers.None ? Strings.HotkeyPressKeys : Hotkey.ModifiersText(mods) + " + …";
    }

    private void OnClear()
    {
        if (!_recording) return;
        StopRecording();
        Hotkey = Hotkey.None;
        Message?.Invoke(this, null);
    }

    private void OnKey(Hotkey candidate)
    {
        if (!_recording) return;
        string? error = Validate?.Invoke(this, candidate);
        if (error != null)
        {
            // stay in recording mode so the user can try another combination
            Message?.Invoke(this, error);
            _text.Text = Strings.HotkeyPressKeys;
            return;
        }
        StopRecording();
        Hotkey = candidate;
        Message?.Invoke(this, null);
    }

    private void Refresh()
    {
        if (_recording) return;
        _text.Text = _hotkey.ToString();
        _text.SetResourceReference(TextBlock.ForegroundProperty, _hotkey.IsEmpty ? "TextDimBrush" : "TextBrush");
        _clear.Visibility = _hotkey.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }
}
