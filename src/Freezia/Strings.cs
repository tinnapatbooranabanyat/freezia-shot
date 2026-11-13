using Freezia.Core;

namespace Freezia;

/// <summary>
/// All user-visible UI text in one place, Thai and English side by side.
/// Every property reads the current language, so text built after a language change is translated.
/// </summary>
public static class Strings
{
    private static string T(string th, string en) => Loc.Language == AppLanguage.English ? en : th;

    public const string AppName = "Freezia";

    // Tray
    public static string TrayTooltip => T("Freezia — จับภาพหน้าจอ", "Freezia — Screen capture");
    public static string TrayCaptureRegion => T("แคปพื้นที่", "Capture region");
    public static string TrayCaptureScreen => T("แคปจอนี้", "Capture this screen");
    public static string TrayCaptureAll => T("แคปทุกจอ", "Capture all screens");
    public static string TrayOpenFolder => T("เปิดโฟลเดอร์ที่บันทึก", "Open save folder");
    public static string TraySettings => T("ตั้งค่า", "Settings");
    public static string TrayExit => T("ออก", "Exit");
    public static string HotkeyHookFailed => T("ไม่สามารถติดตั้งปุ่มลัดได้ — ใช้เมนูที่ไอคอนในถาดระบบแทน",
                                               "Could not register hotkeys — use the tray icon menu instead");

    // Hotkeys
    public static string HotkeyNone => T("ไม่ได้ตั้ง", "Not set");
    public static string HotkeyPressKeys => T("กดปุ่มที่ต้องการ…", "Press a key combination…");
    public static string HotkeyDuplicate => T("ปุ่มลัดนี้ถูกใช้กับ \"{0}\" แล้ว", "This hotkey is already used for \"{0}\"");
    public static string HotkeyNeedsModifier => T("ปุ่มนี้ใช้เดี่ยวๆ ไม่ได้เพราะจะรบกวนการพิมพ์ — กดร่วมกับ Ctrl / Shift / Alt / Win",
                                                  "This key can't be used alone because it would interfere with typing — combine it with Ctrl / Shift / Alt / Win");

    // Capture overlay
    public static string OverlayFullScreen => T("เต็มจอนี้", "This screen");
    public static string OverlayAllScreens => T("ทุกจอ", "All screens");
    public static string OverlayCancel => T("ยกเลิก", "Cancel");
    public static string OverlayHint => T("ลากเพื่อเลือกพื้นที่", "Drag to select an area");
    public static string ActionEdit => T("แก้ไข", "Edit");
    public static string ActionCopy => T("คัดลอก", "Copy");
    public static string ActionSave => T("บันทึก", "Save");
    public static string ActionSaveAs => T("บันทึกเป็น…", "Save as…");
    public static string ActionCancel => T("ยกเลิก", "Cancel");

    // Toasts
    public static string ToastCopied => T("คัดลอกภาพแล้ว", "Image copied");
    public static string ToastSaved => T("บันทึกแล้ว", "Saved");
    public static string ToastClickToOpen => T("คลิกเพื่อเปิดโฟลเดอร์", "Click to open the folder");
    public static string ToastCopyFailed => T("คัดลอกไม่สำเร็จ — คลิปบอร์ดไม่ว่าง", "Copy failed — the clipboard is busy");
    public static string ToastSaveFailed => T("บันทึกไม่สำเร็จ", "Save failed");
    public static string ToastCaptureFailed => T("จับภาพหน้าจอไม่สำเร็จ", "Screen capture failed");

    // Dialogs
    public static string SaveDialogTitle => T("บันทึกภาพหน้าจอ", "Save screenshot");
    public static string FileFilter => T("ภาพ PNG (*.png)|*.png|ภาพ JPEG (*.jpg)|*.jpg;*.jpeg",
                                         "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg;*.jpeg");
    public static string ErrorTitle => T("Freezia — เกิดข้อผิดพลาด", "Freezia — Error");
    public static string UnexpectedError => T("เกิดข้อผิดพลาดที่ไม่คาดคิด\n\n{0}\n\nรายละเอียดถูกบันทึกไว้ใน:\n{1}",
                                              "An unexpected error occurred\n\n{0}\n\nDetails were written to:\n{1}");

    // Editor
    public static string EditorTitle => T("Freezia — แก้ไขภาพ", "Freezia — Edit image");
    public static string ToolSelect => T("เลือก/ย้าย (V)", "Select / move (V)");
    public static string ToolBrush => T("แปรง (B)", "Brush (B)");
    public static string ToolHighlighter => T("ปากกาไฮไลต์ (H)", "Highlighter (H)");
    public static string ToolLine => T("เส้นตรง (L) — กด Shift = ล็อกมุม", "Line (L) — Shift = snap angle");
    public static string ToolArrow => T("ลูกศร (A) — กด Shift = ล็อกมุม", "Arrow (A) — Shift = snap angle");
    public static string ToolRect => T("สี่เหลี่ยม (R) — กด Shift = จัตุรัส", "Rectangle (R) — Shift = square");
    public static string ToolEllipse => T("วงกลม/วงรี (O) — กด Shift = วงกลม", "Ellipse (O) — Shift = circle");
    public static string ToolText => T("ข้อความ (T)", "Text (T)");
    public static string ToolMosaic => T("โมเสกปิดข้อมูล (M)", "Mosaic / redact (M)");
    public static string ToolEraser => T("ยางลบ (E) — ลบเฉพาะสิ่งที่วาด ภาพหน้าจอไม่ถูกลบ", "Eraser (E) — erases drawings only, never the screenshot");
    public static string EraserSize => T("ขนาดยางลบ", "Eraser size");
    public static string ClearAll => T("ล้างทั้งหมด (Ctrl+Shift+Delete)", "Clear all (Ctrl+Shift+Delete)");
    public static string ToolStep => T("ตัวเลขลำดับ (N)", "Numbered step (N)");
    public static string StrokeColor => T("สีเส้น", "Stroke color");
    public static string FillColor => T("สีเติม", "Fill color");
    public static string FillEnabled => T("เติมสี", "Fill");
    public static string Thickness => T("ความหนา", "Thickness");
    public static string FontSize => T("ขนาดอักษร", "Font size");
    public static string Undo => T("เลิกทำ (Ctrl+Z)", "Undo (Ctrl+Z)");
    public static string Redo => T("ทำซ้ำ (Ctrl+Y)", "Redo (Ctrl+Y)");
    public static string Delete => T("ลบ (Delete)", "Delete (Delete)");
    public static string ZoomFit => T("พอดีหน้าต่าง (Ctrl+0)", "Fit to window (Ctrl+0)");
    public static string Zoom100 => T("100% ขนาดจริง (Ctrl+1)", "100% actual pixels (Ctrl+1)");
    public static string ZoomIn => T("ซูมเข้า (Ctrl + ล้อเมาส์)", "Zoom in (Ctrl + mouse wheel)");
    public static string ZoomOut => T("ซูมออก", "Zoom out");
    public static string Copy => T("คัดลอก (Ctrl+C)", "Copy (Ctrl+C)");
    public static string Save => T("บันทึก (Ctrl+S)", "Save (Ctrl+S)");
    public static string SaveAs => T("บันทึกเป็น… (Ctrl+Shift+S)", "Save as… (Ctrl+Shift+S)");
    public static string PresetColors => T("สีสำเร็จรูป", "Preset colors");
    public static string SavedColors => T("สีที่บันทึกไว้ — คลิกขวาที่สีเพื่อลบ", "Saved colors — right-click a color to remove it");
    public static string SaveCurrentColor => T("บันทึกสีปัจจุบันไว้ใช้ครั้งต่อไป", "Save the current color for later");
    public static string RemoveColor => T("ลบสีนี้", "Remove this color");
    public static string RecentColors => T("สีที่ใช้ล่าสุด", "Recent colors");
    public static string UnsavedClose => T("ภาพที่แก้ไขยังไม่ได้บันทึกหรือคัดลอก ต้องการปิดหน้าต่างนี้หรือไม่?",
                                           "The edited image has not been saved or copied. Close this window anyway?");
    public static string UnsavedCloseYes => T("ปิดโดยไม่บันทึก", "Close without saving");
    public static string UnsavedCloseNo => T("กลับไปแก้ไข", "Keep editing");

    // Color picker
    public static string PickerOld => T("เดิม", "Old");
    public static string PickerNew => T("ใหม่", "New");
    public static string PickerAlpha => T("ความทึบ", "Opacity");
    public static string PickerEyedropper => T("ดูดสีจากภาพ", "Pick a color from the image");
    public static string PickerEyedropperHint => T("คลิกบนภาพเพื่อดูดสี (Esc = ยกเลิก)", "Click the image to pick a color (Esc = cancel)");
    public static string PickerOk => T("ตกลง", "OK");
    public static string PickerCancel => T("ยกเลิก", "Cancel");

    // Settings
    public static string SettingsTitle => T("Freezia — ตั้งค่า", "Freezia — Settings");
    public static string SettingsHotkeys => T("ปุ่มลัด", "Hotkeys");
    public static string SettingsHotkeyRegion => T("แคปพื้นที่", "Capture region");
    public static string SettingsHotkeyScreen => T("แคปเต็มจอที่เมาส์อยู่", "Capture the screen under the mouse");
    public static string SettingsHotkeyAll => T("แคปทุกจอรวมเป็นภาพเดียว", "Capture all screens as one image");
    public static string SettingsHotkeyHelp => T("คลิกที่ช่องแล้วกดปุ่มที่ต้องการ • Esc = ยกเลิก • Backspace = ล้าง",
                                                 "Click a box, then press the keys • Esc = cancel • Backspace = clear");
    public static string SettingsSnippingNote => T("ถ้ากด PrintScreen แล้ว Snipping Tool ยังเด้ง: ปิดที่ Settings › Accessibility › Keyboard › \"Use the Print screen key to open screen capture\"",
                                                   "If PrintScreen still opens the Snipping Tool, turn off Settings › Accessibility › Keyboard › \"Use the Print screen key to open screen capture\"");
    public static string SettingsAppearance => T("รูปลักษณ์", "Appearance");
    public static string SettingsLanguage => T("ภาษา", "Language");
    public static string SettingsTheme => T("ธีม", "Theme");
    public static string SettingsAccent => T("สีหลัก", "Accent color");
    public static string SettingsAccentHint => T("ชุดสีของปุ่ม กรอบเลือก และไฮไลต์ทั้งโปรแกรม ใช้ได้ทั้งธีมมืดและสว่าง",
                                                 "Color of buttons, selections and highlights across the app, in both dark and light themes");
    public static string AccentCustom => T("กำหนดสีเอง…", "Custom color…");
    public static string ThemeDark => T("มืด", "Dark");
    public static string ThemeLight => T("สว่าง", "Light");
    public static string ThemeSystem => T("ตามระบบ", "System");
    public static string SettingsSave => T("การบันทึก", "Saving");
    public static string SettingsFolder => T("โฟลเดอร์ที่บันทึก", "Save folder");
    public static string SettingsBrowse => T("เลือก…", "Browse…");
    public static string SettingsResetFolder => T("ค่าเริ่มต้น", "Default");
    public static string SettingsFormat => T("รูปแบบไฟล์", "File format");
    public static string SettingsJpegQuality => T("คุณภาพ JPG", "JPG quality");
    public static string SettingsAfterCapture => T("หลังแคปเต็มจอ / ทุกจอ", "After a full-screen / all-screens capture");
    public static string SettingsAfterEditor => T("เปิดหน้าแก้ไข", "Open the editor");
    public static string SettingsAfterCopy => T("คัดลอกทันที", "Copy right away");
    public static string SettingsAfterSave => T("บันทึกทันที", "Save right away");
    public static string SettingsGeneral => T("ทั่วไป", "General");
    public static string SettingsStartup => T("เปิดพร้อม Windows", "Start with Windows");
    public static string SettingsOk => T("บันทึก", "Save");
    public static string SettingsCancel => T("ยกเลิก", "Cancel");
    public static string SettingsFolderDefaultHint => T("(ค่าเริ่มต้น: Downloads)", "(default: Downloads)");
    public static string SettingsStartupFailed => T("ไม่สามารถตั้งค่าเปิดพร้อม Windows ได้", "Could not change the start-with-Windows setting");
    public static string SettingsAbout => T("Freezia เวอร์ชัน {0} • ทำงานในเครื่องเท่านั้น ไม่มีการเชื่อมต่ออินเทอร์เน็ต",
                                            "Freezia version {0} • Works offline, never connects to the internet");
}
