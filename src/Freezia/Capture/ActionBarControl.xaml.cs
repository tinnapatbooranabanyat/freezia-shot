using System;
using System.Windows;
using System.Windows.Controls;
using Freezia.Editor;

namespace Freezia.Capture;

/// <summary>
/// Buttons shown after selecting a region: edit, copy, save, save as, pin, OCR, QR, cancel.
/// With drawing tools on the overlay it also shows undo / redo / delete / clear all for the drawings,
/// and "edit" opens the editor window with the drawings.
/// </summary>
public partial class ActionBarControl : UserControl
{
    private EditSurface? _surface;

    /// <summary>A button asked to end the capture with this outcome.</summary>
    public event Action<CaptureOutcome>? Requested;

    public ActionBarControl()
    {
        InitializeComponent();
        ApplyLanguage(tools: false);
    }

    /// <summary>Show the drawing commands for <paramref name="surface"/> (null = classic bar).</summary>
    public void Attach(EditSurface? surface)
    {
        if (_surface != null) _surface.CommandsChanged -= SyncCommands;
        _surface = surface;
        Commands.Visibility = surface != null ? Visibility.Visible : Visibility.Collapsed;
        if (surface != null)
        {
            surface.CommandsChanged += SyncCommands;
            SyncCommands();
        }
        ApplyLanguage(surface != null);
    }

    private void SyncCommands()
    {
        if (_surface == null) return;
        BtnUndo.IsEnabled = _surface.Undo.CanUndo;
        BtnRedo.IsEnabled = _surface.Undo.CanRedo;
        BtnDelete.IsEnabled = _surface.Selected != null;
        BtnClearAll.IsEnabled = _surface.Items.Count > 0;
    }

    /// <summary>Tooltips with the keys of the current mode.</summary>
    public void ApplyLanguage(bool tools)
    {
        BtnEdit.ToolTip = tools ? Strings.OpenInEditor + " (Ctrl+E)" : Strings.ActionEdit + " (E)";
        BtnCopy.ToolTip = Strings.ActionCopy + " (Ctrl+C / Enter)";
        BtnSave.ToolTip = Strings.ActionSave + " (Ctrl+S)";
        BtnSaveAs.ToolTip = Strings.ActionSaveAs + " (Ctrl+Shift+S)";
        BtnCancel.ToolTip = Strings.ActionCancel + " (Esc)";
        BtnPin.ToolTip = Strings.ActionPin + " (P)";
        BtnOcr.ToolTip = Strings.ActionOcr + (tools ? " (Ctrl+T)" : " (T)");
        BtnQr.ToolTip = Strings.ActionQr + " (Q)";
        BtnUndo.ToolTip = Strings.Undo + " (Ctrl+Z)";
        BtnRedo.ToolTip = Strings.Redo + " (Ctrl+Y)";
        BtnDelete.ToolTip = Strings.Delete + " (Delete)";
        BtnClearAll.ToolTip = Strings.ClearAll;
    }

    private void OnEdit(object sender, RoutedEventArgs e) => Requested?.Invoke(CaptureOutcome.Edit);
    private void OnCopy(object sender, RoutedEventArgs e) => Requested?.Invoke(CaptureOutcome.Copy);
    private void OnSave(object sender, RoutedEventArgs e) => Requested?.Invoke(CaptureOutcome.Save);
    private void OnSaveAs(object sender, RoutedEventArgs e) => Requested?.Invoke(CaptureOutcome.SaveAs);
    private void OnCancel(object sender, RoutedEventArgs e) => Requested?.Invoke(CaptureOutcome.Cancel);
    private void OnPin(object sender, RoutedEventArgs e) => Requested?.Invoke(CaptureOutcome.Pin);
    private void OnOcr(object sender, RoutedEventArgs e) => Requested?.Invoke(CaptureOutcome.Ocr);
    private void OnQr(object sender, RoutedEventArgs e) => Requested?.Invoke(CaptureOutcome.Qr);
    private void OnUndo(object sender, RoutedEventArgs e) => _surface?.DoUndo();
    private void OnRedo(object sender, RoutedEventArgs e) => _surface?.DoRedo();
    private void OnDelete(object sender, RoutedEventArgs e) => _surface?.DeleteSelected();
    private void OnClearAll(object sender, RoutedEventArgs e) => _surface?.ClearAll();
}
