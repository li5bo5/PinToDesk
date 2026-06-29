using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace PinToDesk.Helpers
{
    public class TrayHelper : IDisposable
    {
        private readonly NotifyIcon  _icon;
        private readonly Window      _window;
        private readonly System.Windows.Application _app;
        private readonly Action      _togglePin;
        private readonly Func<bool>  _getIsPinned;
        private readonly Action      _togglePassThrough;
        private readonly Func<bool>  _getIsPassThrough;

        private readonly ToolStripMenuItem _itemShow;        // 「显示」— 窗口隐藏时可见
        private readonly ToolStripMenuItem _itemHide;        // 「隐藏」— 窗口显示时可见
        private readonly ToolStripMenuItem _itemPin;         // 「置顶/取消置顶」
        private readonly ToolStripMenuItem _itemPassThrough; // 「鼠标穿透/关闭穿透」
        private readonly System.Windows.Forms.Timer _clickTimer;

        public TrayHelper(Window window, System.Windows.Application app,
                          Action togglePin,          Func<bool> getIsPinned,
                          Action togglePassThrough,  Func<bool> getIsPassThrough)
        {
            _window             = window;
            _app                = app;
            _togglePin          = togglePin;
            _getIsPinned        = getIsPinned;
            _togglePassThrough  = togglePassThrough;
            _getIsPassThrough   = getIsPassThrough;

            // ── 菜单项定义 ──────────────────────────────
            _itemShow = new ToolStripMenuItem("显示");
            _itemShow.Font = new System.Drawing.Font(_itemShow.Font, System.Drawing.FontStyle.Bold);
            _itemShow.Click += (s, e) =>
            {
                _window.Dispatcher.Invoke(() =>
                {
                    _window.Show();
                    _window.Activate();
                });
            };

            _itemHide = new ToolStripMenuItem("隐藏");
            _itemHide.Click += (s, e) =>
            {
                _window.Dispatcher.Invoke(() => _window.Hide());
            };

            _itemPin = new ToolStripMenuItem("置顶");
            _itemPin.Click += (s, e) =>
            {
                _window.Dispatcher.Invoke(() =>
                {
                    _window.Show();
                    _window.Activate();
                });
                _togglePin();
                SyncPinMenuItem();
            };

            _itemPassThrough = new ToolStripMenuItem("鼠标穿透");
            _itemPassThrough.Click += (s, e) =>
            {
                _togglePassThrough();
                SyncPassThroughMenuItem();
            };

            var itemAutoStart = new ToolStripMenuItem("开机启动");
            itemAutoStart.CheckOnClick = true;
            itemAutoStart.Checked      = IsAutoStartEnabled();
            itemAutoStart.CheckedChanged += (s, e) => SetAutoStart(itemAutoStart.Checked);

            var itemExit = new ToolStripMenuItem("退出");
            itemExit.Click += (s, e) => _app.Shutdown();

            // ── 组装菜单 ─────────────────────────────────
            var menu = new ContextMenuStrip();
            menu.Items.Add(_itemShow);
            menu.Items.Add(_itemHide);
            menu.Items.Add(_itemPin);
            menu.Items.Add(_itemPassThrough);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemAutoStart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemExit);

            // 打开菜单时实时刷新「显示/隐藏」可见性
            menu.Opening += (s, e) => SyncVisibilityMenuItems();

            // 从嵌入资源加载 PTD.ico 多分辨率图标，并上提一个尺寸等级加载以显示更大的图标
            var asm        = System.Reflection.Assembly.GetExecutingAssembly();
            var iconStream = asm.GetManifestResourceStream("PinToDesk.PTD.ico");
            int smallWidth  = (int)System.Windows.SystemParameters.SmallIconWidth;
            int smallHeight = (int)System.Windows.SystemParameters.SmallIconHeight;
            int targetWidth  = smallWidth <= 16 ? 32 : (smallWidth <= 24 ? 32 : 48);
            int targetHeight = smallHeight <= 16 ? 32 : (smallHeight <= 24 ? 32 : 48);
            var trayIcon   = iconStream != null ? new Icon(iconStream, new System.Drawing.Size(targetWidth, targetHeight)) : SystemIcons.Application;
            var version    = typeof(TrayHelper).Assembly.GetName().Version?.ToString(3) ?? "1.0.4";

            _icon = new NotifyIcon
            {
                Icon             = trayIcon,
                Text             = $"PTD {version}",
                Visible          = true,
                ContextMenuStrip = menu
            };

            // 初始化单击定时器，200ms 内无第二次点击则触发显示（前台）
            _clickTimer = new System.Windows.Forms.Timer();
            _clickTimer.Interval = 200;
            _clickTimer.Tick += (s, e) =>
            {
                _clickTimer.Stop();
                _window.Dispatcher.Invoke(() =>
                {
                    _window.Show();
                    _window.Activate();
                });
            };

            // 鼠标左键单击：启动延迟判定以触发显示
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    _clickTimer.Start();
                }
            };

            // 鼠标左键双击：拦截单击行为并隐藏窗口（后台）
            _icon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    _clickTimer.Stop();
                    _window.Dispatcher.Invoke(() =>
                    {
                        _window.Hide();
                    });
                }
            };

            // 启动时同步一次置顶与穿透的菜单文字状态
            SyncPinMenuItem();
            SyncPassThroughMenuItem();
        }

        /// <summary>根据窗口可见状态，切换「显示」/「隐藏」菜单项的可见性</summary>
        private void SyncVisibilityMenuItems()
        {
            bool visible = false;
            _window.Dispatcher.Invoke(() => visible = _window.IsVisible);
            _itemShow.Visible = !visible;   // 窗口已显示 → 「显示」不可见
            _itemHide.Visible = visible;    // 窗口已显示 → 「隐藏」可见
        }

        /// <summary>由 MainWindow 调用，同步置顶菜单文字</summary>
        public void SyncPinMenuItem()
        {
            _itemPin.Text = _getIsPinned() ? "取消置顶" : "置顶";
        }

        /// <summary>由 MainWindow 调用，同步穿透菜单文字</summary>
        public void SyncPassThroughMenuItem()
        {
            _itemPassThrough.Text = _getIsPassThrough() ? "关闭鼠标穿透" : "鼠标穿透";
        }

        public static void SetAutoStart(bool enable)
        {
            const string key = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, true);
            if (reg == null) return;
            string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (enable) reg.SetValue("PinToDesk", $"\"{exe}\"");
            else reg.DeleteValue("PinToDesk", false);
        }

        public static bool IsAutoStartEnabled()
        {
            const string key = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, false);
            return reg?.GetValue("PinToDesk") != null;
        }

        public void Dispose()
        {
            _clickTimer.Dispose();
            _icon.Dispose();
        }
    }
}
