namespace SnapEdit;

/// <summary>All user-visible Thai UI strings in one place.</summary>
public static class Strings
{
    public const string AppName = "SnapEdit";

    // Tray
    public const string TrayTooltip = "SnapEdit — จับภาพหน้าจอ";
    public const string TrayCaptureRegion = "แคปพื้นที่";
    public const string TrayCaptureScreen = "แคปจอนี้";
    public const string TrayCaptureAll = "แคปทุกจอ";
    public const string TrayOpenFolder = "เปิดโฟลเดอร์ที่บันทึก";
    public const string TraySettings = "ตั้งค่า";
    public const string TrayExit = "ออก";
    public const string AlreadyRunning = "SnapEdit ทำงานอยู่แล้วที่ถาดระบบ";
    public const string HotkeyHookFailed = "ไม่สามารถติดตั้งปุ่มลัดได้ — ใช้เมนูที่ไอคอนในถาดระบบแทน";

    // Hotkeys
    public const string HotkeyNone = "ไม่ได้ตั้ง";
    public const string HotkeyPressKeys = "กดปุ่มที่ต้องการ…";
    public const string HotkeyDuplicate = "ปุ่มลัดนี้ถูกใช้กับ \"{0}\" แล้ว";
    public const string HotkeyNeedKey = "ต้องมีปุ่มหลักอย่างน้อย 1 ปุ่ม (ไม่ใช่แค่ Ctrl/Shift/Alt/Win)";

    // Capture overlay
    public const string OverlayFullScreen = "เต็มจอนี้";
    public const string OverlayAllScreens = "ทุกจอ";
    public const string OverlayCancel = "ยกเลิก";
    public const string OverlayHint = "ลากเพื่อเลือกพื้นที่";
    public const string ActionEdit = "แก้ไข";
    public const string ActionCopy = "คัดลอก";
    public const string ActionSave = "บันทึก";
    public const string ActionSaveAs = "บันทึกเป็น…";
    public const string ActionCancel = "ยกเลิก";

    // Toasts
    public const string ToastCopied = "คัดลอกภาพแล้ว";
    public const string ToastSaved = "บันทึกแล้ว";
    public const string ToastClickToOpen = "คลิกเพื่อเปิดโฟลเดอร์";
    public const string ToastCopyFailed = "คัดลอกไม่สำเร็จ — คลิปบอร์ดไม่ว่าง";
    public const string ToastSaveFailed = "บันทึกไม่สำเร็จ";
    public const string ToastCaptureFailed = "จับภาพหน้าจอไม่สำเร็จ";

    // Dialogs
    public const string SaveDialogTitle = "บันทึกภาพหน้าจอ";
    public const string FileFilter = "ภาพ PNG (*.png)|*.png|ภาพ JPEG (*.jpg)|*.jpg;*.jpeg";
    public const string ErrorTitle = "SnapEdit — เกิดข้อผิดพลาด";
    public const string UnexpectedError = "เกิดข้อผิดพลาดที่ไม่คาดคิด\n\n{0}\n\nรายละเอียดถูกบันทึกไว้ใน:\n{1}";

    // Editor
    public const string EditorTitle = "SnapEdit — แก้ไขภาพ";
    public const string ToolSelect = "เลือก/ย้าย (V)";
    public const string ToolBrush = "แปรง (B)";
    public const string ToolHighlighter = "ปากกาไฮไลต์ (H)";
    public const string ToolLine = "เส้นตรง (L)";
    public const string ToolArrow = "ลูกศร (A)";
    public const string ToolRect = "สี่เหลี่ยม (R) — กด Shift = จัตุรัส";
    public const string ToolEllipse = "วงกลม/วงรี (O) — กด Shift = วงกลม";
    public const string ToolText = "ข้อความ (T)";
    public const string ToolMosaic = "โมเสกปิดข้อมูล (M)";
    public const string ToolStep = "ตัวเลขลำดับ (N)";
    public const string StrokeColor = "สีเส้น";
    public const string FillColor = "สีเติม";
    public const string FillEnabled = "เติมสี";
    public const string Thickness = "ความหนา";
    public const string FontSize = "ขนาดอักษร";
    public const string Undo = "เลิกทำ (Ctrl+Z)";
    public const string Redo = "ทำซ้ำ (Ctrl+Y)";
    public const string Delete = "ลบ (Delete)";
    public const string ZoomFit = "พอดีหน้าต่าง (Ctrl+0)";
    public const string Zoom100 = "100% ขนาดจริง (Ctrl+1)";
    public const string ZoomIn = "ซูมเข้า (Ctrl + ล้อเมาส์)";
    public const string ZoomOut = "ซูมออก";
    public const string Copy = "คัดลอก (Ctrl+C)";
    public const string Save = "บันทึก (Ctrl+S)";
    public const string SaveAs = "บันทึกเป็น… (Ctrl+Shift+S)";
    public const string PresetColors = "สีสำเร็จรูป";
    public const string RecentColors = "สีที่ใช้ล่าสุด";
    public const string UnsavedClose = "ภาพนี้ยังไม่ได้บันทึกหรือคัดลอก ต้องการปิดหรือไม่?";

    // Color picker
    public const string PickerTitle = "เลือกสี";
    public const string PickerOld = "เดิม";
    public const string PickerNew = "ใหม่";
    public const string PickerAlpha = "ความทึบ";
    public const string PickerEyedropper = "ดูดสีจากภาพ";
    public const string PickerEyedropperHint = "คลิกบนภาพเพื่อดูดสี (Esc = ยกเลิก)";
    public const string PickerOk = "ตกลง";
    public const string PickerCancel = "ยกเลิก";

    // Settings
    public const string SettingsTitle = "SnapEdit — ตั้งค่า";
    public const string SettingsHotkeys = "ปุ่มลัด";
    public const string SettingsHotkeyRegion = "แคปพื้นที่";
    public const string SettingsHotkeyScreen = "แคปเต็มจอที่เมาส์อยู่";
    public const string SettingsHotkeyAll = "แคปทุกจอรวมเป็นภาพเดียว";
    public const string SettingsHotkeyHelp = "คลิกที่ช่องแล้วกดปุ่มที่ต้องการ • Esc = ยกเลิก • Backspace = ล้าง";
    public const string SettingsSnippingNote = "ถ้ากด PrintScreen แล้ว Snipping Tool ยังเด้ง: ปิดที่ Settings › Accessibility › Keyboard › \"Use the Print screen key to open screen capture\"";
    public const string SettingsSave = "การบันทึก";
    public const string SettingsFolder = "โฟลเดอร์ที่บันทึก";
    public const string SettingsBrowse = "เลือก…";
    public const string SettingsResetFolder = "ค่าเริ่มต้น";
    public const string SettingsFormat = "รูปแบบไฟล์";
    public const string SettingsJpegQuality = "คุณภาพ JPG";
    public const string SettingsAfterCapture = "หลังแคปเต็มจอ / ทุกจอ";
    public const string SettingsAfterEditor = "เปิดหน้าแก้ไข";
    public const string SettingsAfterCopy = "คัดลอกทันที";
    public const string SettingsAfterSave = "บันทึกทันที";
    public const string SettingsGeneral = "ทั่วไป";
    public const string SettingsStartup = "เปิดพร้อม Windows";
    public const string SettingsOk = "บันทึก";
    public const string SettingsCancel = "ยกเลิก";
    public const string SettingsFolderDefaultHint = "(ค่าเริ่มต้น: Downloads)";
    public const string SettingsStartupFailed = "ไม่สามารถตั้งค่าเปิดพร้อม Windows ได้";
    public const string SettingsAbout = "SnapEdit เวอร์ชัน {0} • ทำงานในเครื่องเท่านั้น ไม่มีการเชื่อมต่ออินเทอร์เน็ต";
}
