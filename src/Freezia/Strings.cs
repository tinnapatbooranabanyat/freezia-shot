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
    public static string TrayHistory => T("ประวัติการแคป", "Capture history");
    public static string TrayPickColor => T("ดูดสีจากหน้าจอ", "Pick a color from the screen");
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
    public static string OverlayHint => T("ลากเพื่อเลือก • คลิก = ทั้งหน้าต่าง • กด Ctrl = ส่วนย่อย",
                                          "Drag to select • Click = whole window • Hold Ctrl = UI element");
    public static string ActionPin => T("ปักหมุดไว้บนจอ", "Pin to screen");
    public static string ActionOcr => T("ดึงข้อความ (OCR)", "Extract text (OCR)");
    public static string OpenLink => T("เปิดลิงก์", "Open link");
    public static string Close => T("ปิด", "Close");
    public static string TextCopied => T("คัดลอกข้อความแล้ว", "Text copied");
    public static string PinActualSize => T("ขนาดจริง 100%", "Actual size 100%");
    public static string OcrTitle => T("ข้อความจากภาพ", "Text from image");
    public static string OcrBusy => T("กำลังอ่านข้อความ… (ครั้งแรกอาจใช้เวลาสักครู่)", "Reading text… (the first time takes a moment)");
    public static string OcrNoText => T("ไม่พบข้อความในภาพนี้", "No text found in this image");
    public static string OcrDone => T("พบข้อความ {0} ตัวอักษร • คัดลอกแล้ว แก้ไขก่อนคัดลอกซ้ำได้", "Found {0} characters • copied — you can edit and copy again");
    public static string OcrFailed => T("อ่านข้อความไม่สำเร็จ", "Text recognition failed");
    public static string OcrMissingRuntime => T("ต้องติดตั้ง Microsoft Visual C++ Redistributable (x64) ก่อนใช้ OCR — ติดตั้ง Freezia ใหม่ด้วยตัวติดตั้งจะได้ส่วนนี้มาด้วย",
                                                "OCR needs the Microsoft Visual C++ Redistributable (x64) — reinstalling Freezia with its installer adds it");
    public static string QrTitle => T("QR code / บาร์โค้ด", "QR code / barcode");
    public static string QrBusy => T("กำลังอ่านโค้ด…", "Reading codes…");
    public static string QrNone => T("ไม่พบ QR code หรือบาร์โค้ดในภาพนี้", "No QR code or barcode found in this image");
    public static string QrFound => T("พบ {0} โค้ด ({1}) • คัดลอกแล้ว", "Found {0} code(s) ({1}) • copied");
    public static string QrFailed => T("อ่านโค้ดไม่สำเร็จ", "Could not read codes");
    public static string PickColorHint => T("คลิกเพื่อคัดลอกสี (HEX) • Shift + คลิก = rgb(…) • Esc = ยกเลิก",
                                            "Click to copy the color (HEX) • Shift + click = rgb(…) • Esc = cancel");
    public static string ColorCopied => T("คัดลอกสีแล้ว", "Color copied");
    public static string HistoryTitle => T("ประวัติการแคป", "Capture history");
    public static string HistoryCount => T("{0} ภาพ", "{0} images");
    public static string HistoryEmpty => T("ยังไม่มีประวัติ ภาพที่คุณคัดลอก บันทึก หรือแก้ไขจะมาอยู่ที่นี่", "No history yet — images you copy, save or edit appear here");
    public static string HistoryClear => T("ล้างประวัติ", "Clear history");
    public static string HistoryClearConfirm => T("ลบภาพทั้งหมดในประวัติการแคป? (ไฟล์ที่บันทึกไว้เองจะไม่ถูกลบ)", "Delete every image in the capture history? (files you saved yourself are kept)");
    public static string HistoryShowFile => T("แสดงไฟล์ในโฟลเดอร์", "Show file in folder");
    public static string HistoryCardTip => T("ดับเบิลคลิก = แก้ไข • คลิกขวา = ตัวเลือกเพิ่มเติม", "Double-click = edit • Right-click = more");
    public static string SettingsHotkeyColor => T("ดูดสีจากหน้าจอ", "Pick a color from the screen");
    public static string SettingsHistory => T("เก็บประวัติการแคป", "Keep a capture history");
    public static string SettingsHistoryLimit => T("จำนวนภาพที่เก็บ", "Images to keep");
    public static string SettingsHistoryHint => T("เก็บไว้ในเครื่องที่ %LocalAppData%\\Freezia\\history เท่านั้น • ปิดแล้วจะลบประวัติทั้งหมด", "Stored only on this PC in %LocalAppData%\\Freezia\\history • turning it off deletes the history");
    public static string SettingsCaptureMethod => T("วิธีจับภาพ", "Capture method");
    public static string CaptureMethodAuto => T("อัตโนมัติ (แนะนำ)", "Automatic (recommended)");
    public static string CaptureMethodGdi => T("แบบเดิม (GDI)", "Classic (GDI)");
    public static string CaptureMethodHint => T("อัตโนมัติ = ใช้ Windows.Graphics.Capture ซึ่งแคปเกมเต็มจอและจอ HDR ได้ถูกต้อง และกลับไปใช้แบบเดิมเองถ้าใช้ไม่ได้ • แบบเดิมเร็วกว่าเล็กน้อยแต่เกม/HDR อาจเป็นสีดำหรือซีด",
                                                "Automatic = Windows.Graphics.Capture, which handles full-screen games and HDR monitors, falling back to classic when unavailable • Classic is slightly faster but games/HDR may come out black or washed out");
    public static string ActionQr => T("อ่าน QR / บาร์โค้ด", "Read QR / barcode");
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
    public static string ToolBlur => T("เบลอ (U) — แทนที่พิกเซลจริง กู้คืนไม่ได้", "Blur (U) — replaces the real pixels");
    public static string ToolSpotlight => T("สปอตไลต์ (S) — พื้นที่ที่เลือกสว่าง ส่วนอื่นมืดลง • กด Shift ตอนเริ่ม = วงรี", "Spotlight (S) — keep an area bright, dim the rest • Shift at start = ellipse");
    public static string ToolCrop => T("ครอป (C)", "Crop (C)");
    public static string BlurStrength => T("ความเบลอ", "Blur strength");
    public static string CropHint => T("ลากกรอบ แล้วกด Enter เพื่อครอป • Esc = ยกเลิก", "Drag a frame, then press Enter to crop • Esc = cancel");
    public static string CropApply => T("ครอป", "Crop");
    public static string ResizeTitle => T("ปรับขนาดภาพ", "Resize image");
    public static string ResizeWidth => T("กว้าง", "Width");
    public static string ResizeHeight => T("สูง", "Height");
    public static string ResizePercent => T("เปอร์เซ็นต์", "Percent");
    public static string ResizeKeepRatio => T("รักษาสัดส่วน", "Keep aspect ratio");
    public static string ResizeNote => T("สิ่งที่วาดไว้จะถูกรวมเข้ากับภาพ (กด Ctrl+Z เพื่อย้อนกลับได้)", "Drawings are merged into the image (Ctrl+Z undoes it)");
    public static string ResizeInvalid => T("ใส่ขนาด 1–20000 พิกเซล", "Enter a size of 1–20000 pixels");
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
    public static string SettingsThemeHint => T("ชุดสีพื้นหลัง แผง ข้อความ และขอบ ของทั้งโปรแกรม", "Colors of backgrounds, panels, text and borders across the app");
    public static string ThemeCustom => T("กำหนดเอง", "Custom");
    public static string ThemeCustomPick => T("เลือกสีพื้นหลัง แล้วระบบจะสร้างชุดสีทั้งหมดให้", "Pick a background color and the rest of the theme is generated");
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
    public static string SettingsAbout => T("Freezia เวอร์ชัน {0} • ภาพทั้งหมดอยู่ในเครื่องนี้เท่านั้น",
                                            "Freezia version {0} • Your screenshots never leave this PC");

    // ---- editing other images ----
    public static string TrayOpenImage => T("เปิดภาพมาแก้ไข…", "Open image to edit…");
    public static string TrayEditClipboard => T("แก้ไขภาพจากคลิปบอร์ด", "Edit image from clipboard");
    public static string OpenImageTitle => T("เลือกภาพที่จะแก้ไข", "Choose images to edit");
    public static string ImageFiles => T("ไฟล์ภาพ", "Image files");
    public static string AllFiles => T("ทุกไฟล์", "All files");
    public static string OpenImageFailed => T("เปิดภาพไม่ได้", "Could not open the image");
    public static string ClipboardNoImage => T("ไม่มีภาพในคลิปบอร์ด", "There is no image on the clipboard");
    public static string ImageTooLarge => T("ภาพใหญ่เกินไป ({0} × {1}) รองรับไม่เกิน {2} พิกเซลต่อด้าน",
                                            "The image is too large ({0} × {1}); up to {2} pixels per side");
    public static string EditedSuffix => T("_แก้ไข", "_edited");

    // ---- updates ----
    public static string TrayUpdate => T("อัปเดตเป็นเวอร์ชัน {0}", "Update to version {0}");
    public static string UpdateToastTitle => T("มี Freezia {0} แล้ว", "Freezia {0} is available");
    public static string UpdateToastMessage => T("คลิกเพื่อดูรายละเอียดและอัปเดต", "Click to see what's new and update");
    public static string UpdateWindowTitle => T("อัปเดต Freezia", "Update Freezia");
    public static string UpdateAvailableTitle => T("Freezia {0} พร้อมให้อัปเดต", "Freezia {0} is ready to install");
    public static string UpdateCurrentVersion => T("เวอร์ชันที่ใช้อยู่: {0}", "You have version {0}");
    public static string UpdateWhatsNew => T("มีอะไรใหม่", "What's new");
    public static string UpdateNow => T("อัปเดตเลย", "Update now");
    public static string UpdateOpenPage => T("เปิดหน้าดาวน์โหลด", "Open download page");
    public static string UpdateLater => T("ไว้ทีหลัง", "Later");
    public static string UpdateRetry => T("ลองอีกครั้ง", "Try again");
    public static string UpdateDownloading => T("กำลังดาวน์โหลด…", "Downloading…");
    public static string UpdateDownloadingPercent => T("กำลังดาวน์โหลด… {0}% {1}", "Downloading… {0}% {1}");
    public static string UpdateInstalling => T("กำลังติดตั้ง Freezia จะปิดแล้วเปิดขึ้นมาใหม่เอง", "Installing. Freezia will close and start again by itself.");
    public static string UpdateFailed => T("อัปเดตไม่สำเร็จ: {0}", "Update failed: {0}");
    public static string SettingsAutoUpdate => T("ตรวจหาอัปเดตอัตโนมัติ", "Check for updates automatically");
    public static string SettingsCheckNow => T("ตรวจหาอัปเดตตอนนี้", "Check for updates now");
    public static string SettingsUpdateHint => T("ตรวจที่ GitHub ตอนเปิดโปรแกรมและทุก 6 ชั่วโมง ไม่ส่งข้อมูลหรือภาพใดๆ ออกไป",
                                                 "Checks GitHub at start-up and every 6 hours. No data or screenshots are sent.");
    public static string UpdateChecking => T("กำลังตรวจหาอัปเดต…", "Checking for updates…");
    public static string UpdateUpToDate => T("ใช้เวอร์ชันล่าสุดอยู่แล้ว ({0})", "You have the latest version ({0})");
    public static string UpdateFound => T("พบเวอร์ชัน {0}", "Version {0} is available");
    public static string UpdateCheckFailed => T("ตรวจหาอัปเดตไม่สำเร็จ: {0}", "Could not check for updates: {0}");
}
