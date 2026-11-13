using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Freezia.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

/// <summary>A global hotkey: modifiers + one virtual-key code. Key = 0 means "not set".</summary>
public sealed record Hotkey
{
    public HotkeyModifiers Modifiers { get; init; }
    /// <summary>Win32 virtual-key code.</summary>
    public int Key { get; init; }

    public Hotkey() { }

    public Hotkey(HotkeyModifiers modifiers, int key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    [JsonIgnore]
    public bool IsEmpty => Key == 0;

    public static Hotkey None => new(HotkeyModifiers.None, 0);

    public const int VK_SNAPSHOT = 0x2C;

    public override string ToString()
    {
        if (IsEmpty) return Strings.HotkeyNone;
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    public static string ModifiersText(HotkeyModifiers m)
    {
        var parts = new List<string>();
        if (m.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (m.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    public static bool IsModifierKey(int vk) => vk is
        0x10 or 0x11 or 0x12 or          // Shift, Ctrl, Alt
        0xA0 or 0xA1 or 0xA2 or 0xA3 or  // L/R Shift, L/R Ctrl
        0xA4 or 0xA5 or                  // L/R Alt
        0x5B or 0x5C;                    // L/R Win

    public static string KeyName(int vk)
    {
        switch (vk)
        {
            case VK_SNAPSHOT: return "PrintScreen";
            case 0x08: return "Backspace";
            case 0x09: return "Tab";
            case 0x0D: return "Enter";
            case 0x13: return "Pause";
            case 0x14: return "CapsLock";
            case 0x1B: return "Esc";
            case 0x20: return "Space";
            case 0x21: return "PageUp";
            case 0x22: return "PageDown";
            case 0x23: return "End";
            case 0x24: return "Home";
            case 0x25: return "Left";
            case 0x26: return "Up";
            case 0x27: return "Right";
            case 0x28: return "Down";
            case 0x2D: return "Insert";
            case 0x2E: return "Delete";
            case 0x5D: return "Menu";
            case 0x6A: return "Num *";
            case 0x6B: return "Num +";
            case 0x6D: return "Num -";
            case 0x6E: return "Num .";
            case 0x6F: return "Num /";
            case 0x90: return "NumLock";
            case 0x91: return "ScrollLock";
            case 0xBA: return ";";
            case 0xBB: return "=";
            case 0xBC: return ",";
            case 0xBD: return "-";
            case 0xBE: return ".";
            case 0xBF: return "/";
            case 0xC0: return "`";
            case 0xDB: return "[";
            case 0xDC: return "\\";
            case 0xDD: return "]";
            case 0xDE: return "'";
        }
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        return $"Key 0x{vk:X2}";
    }
}
