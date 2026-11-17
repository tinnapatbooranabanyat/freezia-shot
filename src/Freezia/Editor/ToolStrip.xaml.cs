using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freezia.Editor;

/// <summary>
/// Tool buttons for an <see cref="EditSurface"/>: horizontal in the editor window, vertical next to the
/// capture selection. With <see cref="AllowNone"/>, clicking the active tool again deselects it (no tool).
/// </summary>
public partial class ToolStrip : UserControl
{
    private EditSurface? _surface;
    private readonly Dictionary<EditorTool, RadioButton> _buttons;
    private bool _syncing;

    public event Action? ResizeRequested;
    public event Action? OpenEditorRequested;

    /// <summary>Clicking the selected tool again switches to <see cref="EditorTool.None"/>.</summary>
    public bool AllowNone { get; set; }

    public ToolStrip()
    {
        InitializeComponent();
        _buttons = new()
        {
            [EditorTool.Select] = ToolSelect,
            [EditorTool.Brush] = ToolBrush,
            [EditorTool.Highlighter] = ToolHighlighter,
            [EditorTool.Line] = ToolLine,
            [EditorTool.Arrow] = ToolArrow,
            [EditorTool.Rect] = ToolRect,
            [EditorTool.Ellipse] = ToolEllipse,
            [EditorTool.Text] = ToolText,
            [EditorTool.Mosaic] = ToolMosaic,
            [EditorTool.Step] = ToolStep,
            [EditorTool.Eraser] = ToolEraser,
            [EditorTool.Blur] = ToolBlur,
            [EditorTool.Spotlight] = ToolSpotlight,
            [EditorTool.Crop] = ToolCrop,
        };
        foreach (var (tool, rb) in _buttons)
        {
            var t = tool;
            rb.Checked += (_, _) => { if (!_syncing) _surface?.SetTool(t); };
            rb.PreviewMouseLeftButtonDown += (_, e) =>
            {
                // a second click on the active tool = no tool (capture overlay)
                if (AllowNone && rb.IsChecked == true && _surface != null)
                {
                    _surface.SetTool(EditorTool.None);
                    e.Handled = true;
                }
            };
        }
        ShowCrop = true;
        ShowCommands = false;
        ShowOpenEditor = false;
        ApplyLanguage();
        Loaded += (_, _) => { Core.Loc.Changed -= ApplyLanguage; Core.Loc.Changed += ApplyLanguage; };
        Unloaded += (_, _) => Core.Loc.Changed -= ApplyLanguage;
    }

    public void Attach(EditSurface surface)
    {
        if (_surface != null)
        {
            _surface.StateChanged -= Sync;
            _surface.CommandsChanged -= SyncCommands;
        }
        _surface = surface;
        surface.StateChanged += Sync;
        surface.CommandsChanged += SyncCommands;
        Sync();
        SyncCommands();
    }

    public Orientation Orientation
    {
        get => Panel.Orientation;
        set
        {
            Panel.Orientation = value;
            bool vertical = value == Orientation.Vertical;
            foreach (var sep in new[] { SepTools, SepEraser, SepCommands })
            {
                sep.Width = vertical ? double.NaN : 1;
                sep.Height = vertical ? 1 : double.NaN;
                sep.Margin = vertical ? new Thickness(8, 6, 8, 6) : new Thickness(6, 8, 6, 8);
            }
            foreach (var child in Panel.Children)
                if (child is FrameworkElement fe && fe is not Border)
                    fe.Margin = vertical ? new Thickness(0, 1, 0, 1) : new Thickness(1, 0, 1, 0);
        }
    }

    /// <summary>Crop and Resize (editor window only).</summary>
    public bool ShowCrop
    {
        set => ToolCrop.Visibility = ResizeButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Undo / Redo / Delete / Clear all inside the strip (capture overlay).</summary>
    public bool ShowCommands
    {
        set => SepCommands.Visibility = UndoButton.Visibility = RedoButton.Visibility =
            DeleteButton.Visibility = ClearAllButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool ShowOpenEditor
    {
        set => OpenEditorButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Sync()
    {
        if (_surface == null) return;
        _syncing = true;
        try
        {
            foreach (var (tool, rb) in _buttons)
            {
                bool on = tool == _surface.Tool;
                if (rb.IsChecked != on) rb.IsChecked = on;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncCommands()
    {
        if (_surface == null) return;
        UndoButton.IsEnabled = _surface.Undo.CanUndo;
        RedoButton.IsEnabled = _surface.Undo.CanRedo;
        DeleteButton.IsEnabled = _surface.Selected != null;
        ClearAllButton.IsEnabled = _surface.Items.Count > 0;
    }

    /// <summary>Tooltips: tool name plus its key.</summary>
    public void ApplyLanguage()
    {
        static string K(string name, string key) => $"{name}  ({key})";
        ToolSelect.ToolTip = K(Strings.ToolSelect, "V");
        ToolBrush.ToolTip = K(Strings.ToolBrush, "B");
        ToolHighlighter.ToolTip = K(Strings.ToolHighlighter, "H");
        ToolLine.ToolTip = K(Strings.ToolLine, "L");
        ToolArrow.ToolTip = K(Strings.ToolArrow, "A");
        ToolRect.ToolTip = K(Strings.ToolRect, "R");
        ToolEllipse.ToolTip = K(Strings.ToolEllipse, "O");
        ToolText.ToolTip = K(Strings.ToolText, "T");
        ToolMosaic.ToolTip = K(Strings.ToolMosaic, "M");
        ToolStep.ToolTip = K(Strings.ToolStep, "N");
        ToolEraser.ToolTip = K(Strings.ToolEraser, "E");
        ToolBlur.ToolTip = K(Strings.ToolBlur, "U");
        ToolSpotlight.ToolTip = K(Strings.ToolSpotlight, "S");
        ToolCrop.ToolTip = K(Strings.ToolCrop, "C");
        ResizeButton.ToolTip = Strings.ResizeTitle;
        UndoButton.ToolTip = Strings.Undo + "  (Ctrl+Z)";
        RedoButton.ToolTip = Strings.Redo + "  (Ctrl+Y)";
        DeleteButton.ToolTip = Strings.Delete + "  (Delete)";
        ClearAllButton.ToolTip = Strings.ClearAll;
        OpenEditorButton.ToolTip = Strings.OpenInEditor + "  (Ctrl+E)";
    }

    private void OnResize(object sender, RoutedEventArgs e) => ResizeRequested?.Invoke();
    private void OnOpenEditor(object sender, RoutedEventArgs e) => OpenEditorRequested?.Invoke();
    private void OnUndo(object sender, RoutedEventArgs e) => _surface?.DoUndo();
    private void OnRedo(object sender, RoutedEventArgs e) => _surface?.DoRedo();
    private void OnDelete(object sender, RoutedEventArgs e) => _surface?.DeleteSelected();
    private void OnClearAll(object sender, RoutedEventArgs e) => _surface?.ClearAll();
}
