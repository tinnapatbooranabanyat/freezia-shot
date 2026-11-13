using System;
using System.Drawing;
using System.Windows.Forms;
using SnapEdit.Core;

namespace SnapEdit.UI;

/// <summary>System tray icon with a dark context menu (WinForms NotifyIcon).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _region, _screen, _all;
    private readonly Icon _appIcon;

    public event Action? CaptureRegion;
    public event Action? CaptureScreen;
    public event Action? CaptureAll;
    public event Action? OpenFolder;
    public event Action? OpenSettings;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _appIcon = LoadIcon();
        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(4),
        };

        // Delay so the menu's fade-out is gone before the screen is frozen.
        _region = Add(Strings.TrayCaptureRegion, () => CaptureRegion?.Invoke(), true);
        _screen = Add(Strings.TrayCaptureScreen, () => CaptureScreen?.Invoke(), true);
        _all = Add(Strings.TrayCaptureAll, () => CaptureAll?.Invoke(), true);
        _region.Font = new Font(_menu.Font, FontStyle.Bold);
        _menu.Items.Add(new ToolStripSeparator());
        Add(Strings.TrayOpenFolder, () => OpenFolder?.Invoke(), false);
        Add(Strings.TraySettings, () => OpenSettings?.Invoke(), false);
        _menu.Items.Add(new ToolStripSeparator());
        Add(Strings.TrayExit, () => ExitRequested?.Invoke(), false);

        _icon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = Strings.TrayTooltip,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Delayed(() => CaptureRegion?.Invoke(), 150);
        };
    }

    public void UpdateHotkeys(AppSettings s)
    {
        _region.ShortcutKeyDisplayString = s.HotkeyRegion.IsEmpty ? "" : s.HotkeyRegion.ToString();
        _screen.ShortcutKeyDisplayString = s.HotkeyScreen.IsEmpty ? "" : s.HotkeyScreen.ToString();
        _all.ShortcutKeyDisplayString = s.HotkeyAllScreens.IsEmpty ? "" : s.HotkeyAllScreens.ToString();
        _region.ShowShortcutKeys = _screen.ShowShortcutKeys = _all.ShowShortcutKeys = true;
    }

    private ToolStripMenuItem Add(string text, Action action, bool delayed)
    {
        var item = new ToolStripMenuItem(text) { Padding = new Padding(6, 5, 6, 5) };
        item.Click += (_, _) =>
        {
            if (delayed) Delayed(action, 250);
            else action();
        };
        _menu.Items.Add(item);
        return item;
    }

    private static void Delayed(Action action, int ms)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico");
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info != null)
            {
                using var stream = info.Stream;
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Tray icon load failed", ex);
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _appIcon.Dispose();
    }

    // ---- Dark menu look ----
    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Bg = Color.FromArgb(0x20, 0x21, 0x27);
        private static readonly Color Hover = Color.FromArgb(0x34, 0x35, 0x3E);
        private static readonly Color Border = Color.FromArgb(0x3B, 0x3C, 0x46);
        private static readonly Color Text = Color.FromArgb(0xED, 0xED, 0xF2);
        private static readonly Color Dim = Color.FromArgb(0x9C, 0x9D, 0xAA);

        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = true; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool isShortcut = e.Item is ToolStripMenuItem mi && e.Text == mi.ShortcutKeyDisplayString && !string.IsNullOrEmpty(e.Text);
            e.TextColor = isShortcut ? Dim : Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
            using var b = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Hover : Bg);
            e.Graphics.FillRectangle(b, r);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var p = new Pen(Border);
            e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var p = new Pen(Border);
            var r = e.AffectedBounds;
            e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Bg;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color ImageMarginGradientBegin => Bg;
            public override Color ImageMarginGradientMiddle => Bg;
            public override Color ImageMarginGradientEnd => Bg;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;
        }
    }
}
