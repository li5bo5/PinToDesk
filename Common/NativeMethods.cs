using System;
using System.Runtime.InteropServices;

namespace PinToDesk.Common
{
    /// <summary>
    /// 统一收敛的 Win32 API 原生方法、数据结构、多屏边界与窗口交互辅助
    /// </summary>
    public static class NativeMethods
    {
        // ── 常见常量定义 ───────────────────────────────────────────────
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new(-2);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint GA_ROOT = 2;

        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MBUTTONDOWN = 0x0207;

        public const int SM_CXDOUBLECLK = 36;
        public const int SM_CYDOUBLECLK = 37;

        public const byte VK_CTRL = 0x11;
        public const byte VK_LWIN = 0x5B;
        public const byte VK_RWIN = 0x5C;
        public const byte VK_ALT = 0x12;
        public const byte VK_SHIFT = 0x10;
        public const byte VK_ESCAPE = 0x1B;

        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const int LLKHF_INJECTED = 0x0010;

        // ── 常用结构体定义 ─────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        // ── P/Invoke 签名 ──────────────────────────────────────────────
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int RegisterWindowMessage(string lpString);

        public static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        /// <summary>
        /// 穿透 Windows 权限限制强制将指定窗口切换为前台焦点窗口
        /// </summary>
        public static void ForceSetForegroundWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

            IntPtr currentFg = GetForegroundWindow();
            if (currentFg == hWnd) return;

            uint currentThreadId = GetCurrentThreadId();
            uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);
            uint fgThreadId = currentFg != IntPtr.Zero ? GetWindowThreadProcessId(currentFg, out _) : 0;

            bool attachedTarget = false;
            bool attachedFg = false;

            try
            {
                if (targetThreadId != 0 && targetThreadId != currentThreadId)
                    attachedTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
                if (fgThreadId != 0 && fgThreadId != currentThreadId && fgThreadId != targetThreadId)
                    attachedFg = AttachThreadInput(currentThreadId, fgThreadId, true);

                SetForegroundWindow(hWnd);
                BringWindowToTop(hWnd);
            }
            finally
            {
                if (attachedFg)
                    AttachThreadInput(currentThreadId, fgThreadId, false);
                if (attachedTarget)
                    AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        // ── 多屏拖拽边界 / 桌面宿主 / 每显示器 DPI / 窗口交互统一辅助 ────────
        private const int GWLP_HWNDPARENT = -8;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        /// <summary>
        /// 统一窗口拖动与松手超界回弹（方案 A）：
        /// 0. 拖动期间交由系统原生 DragMove 自由跟随，跨屏幕零跳变；
        /// 1. 多屏幕交界边允许跨越显示与停留；
        /// 2. 无相邻屏幕的上边缘不允许把顶部标题栏拖出屏幕顶边（且受错位多屏列顶边约束）；
        /// 3. 无相邻屏幕的下边缘留在屏幕内的高度不小于 2/5；
        /// 4. 无相邻屏幕的左右边缘留在屏幕内的宽度不小于 1/5。
        /// </summary>
        public static void DragMoveWithBounds(System.Windows.Window window)
        {
            try
            {
                window.DragMove();
            }
            catch
            {
                return;
            }

            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rect)) return;

            int width  = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return;

            var curScreen = GetCursorPos(out POINT cursor)
                ? System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y))
                : System.Windows.Forms.Screen.FromHandle(hwnd);

            var area   = curScreen.WorkingArea;
            var bounds = curScreen.Bounds;

            int minAreaLeft   = area.Left;
            int maxAreaRight  = area.Right;
            int minAreaTop    = area.Top;
            int maxAreaBottom = area.Bottom;

            // 检测当前屏幕四边是否存在相邻屏幕，若存在则延伸交界边允许自由跨越显示
            foreach (var other in System.Windows.Forms.Screen.AllScreens)
            {
                if (other.DeviceName == curScreen.DeviceName) continue;
                var ob = other.Bounds;
                var oa = other.WorkingArea;

                // 垂直方向有重叠：检测左/右相邻屏幕
                if (Math.Min(ob.Bottom, bounds.Bottom) > Math.Max(ob.Top, bounds.Top))
                {
                    if (ob.Left < bounds.Left && ob.Right >= bounds.Left - 4)
                        minAreaLeft = Math.Min(minAreaLeft, oa.Left);
                    if (ob.Right > bounds.Right && ob.Left <= bounds.Right + 4)
                        maxAreaRight = Math.Max(maxAreaRight, oa.Right);
                }

                // 水平方向有重叠：检测上/下相邻屏幕
                if (Math.Min(ob.Right, bounds.Right) > Math.Max(ob.Left, bounds.Left))
                {
                    if (ob.Top < bounds.Top && ob.Bottom >= bounds.Top - 4)
                        minAreaTop = Math.Min(minAreaTop, oa.Top);
                    if (ob.Bottom > bounds.Bottom && ob.Top <= bounds.Bottom + 4)
                        maxAreaBottom = Math.Max(maxAreaBottom, oa.Bottom);
                }
            }

            // 左右外边缘：最少保留 1/5 宽度
            int minKeepW = Math.Max(1, width / 5);
            int minLeft  = minAreaLeft - (width - minKeepW);
            int maxLeft  = maxAreaRight - minKeepW;

            int newLeft = minLeft <= maxLeft ? Math.Clamp(rect.Left, minLeft, maxLeft) : minLeft;

            // 上外边缘：不允许把顶部标题栏拖出屏幕顶边；下外边缘：最少保留 2/5 高度
            int minKeepH = Math.Max(1, height * 2 / 5);
            int minTop   = minAreaTop;
            int maxTop   = maxAreaBottom - minKeepH;

            // 上下错位多屏：窗口水平中心所在列若顶边更低（上方无屏幕），标题栏主体也不得越过该列顶边
            int centerX = newLeft + width / 2;
            int columnTop = int.MaxValue;
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
            {
                if (centerX >= s.Bounds.Left && centerX < s.Bounds.Right)
                    columnTop = Math.Min(columnTop, s.WorkingArea.Top);
            }
            if (columnTop != int.MaxValue) minTop = Math.Max(minTop, columnTop);

            int newTop = minTop <= maxTop ? Math.Clamp(rect.Top, minTop, maxTop) : minTop;

            if (newLeft != rect.Left || newTop != rect.Top)
            {
                SetWindowPos(hwnd, IntPtr.Zero, newLeft, newTop, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        /// <summary>
        /// 将窗口所有者绑定为桌面 Progman，使非置顶窗口在 Win+D 时随桌面一同显示
        /// </summary>
        public static void BindOwnerToProgman(IntPtr hwnd)
        {
            IntPtr progman = FindWindow("Progman", null);
            if (hwnd != IntPtr.Zero && progman != IntPtr.Zero)
                SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, progman);
        }

        /// <summary>
        /// 获取指定屏幕坐标所在显示器的 DPI 缩放比例（1.0 = 96 DPI）
        /// </summary>
        public static double GetScaleForPoint(int x, int y)
        {
            IntPtr monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
            return 1.0;
        }

        /// <summary>
        /// 统一处理窗口右下角拖拽缩放，并钳制在当前所在屏幕的工作区右/下边界内
        /// </summary>
        public static void ApplyResizeWithScreenClamp(System.Windows.Window window, double startW, double startH, System.Windows.Vector delta)
        {
            double newW = Math.Max(window.MinWidth, startW + delta.X);
            double newH = Math.Max(window.MinHeight, startH + delta.Y);

            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT rect))
            {
                // 统一在物理像素坐标系下计算窗口左上角到工作区右/下边界的距离，再按窗口自身 DPI 换算为逻辑尺寸上限
                var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                double maxW = (area.Right - rect.Left) / (dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0);
                double maxH = (area.Bottom - rect.Top) / (dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0);

                if (newW > maxW) newW = Math.Max(window.MinWidth, maxW);
                if (newH > maxH) newH = Math.Max(window.MinHeight, maxH);
            }

            window.Width = newW;
            window.Height = newH;
        }

        /// <summary>
        /// 为窗口右下角缩放手柄统一挂载拖拽缩放事件
        /// </summary>
        public static void AttachResizeGrip(System.Windows.Window window, System.Windows.UIElement grip, Action? onResizeCompleted = null)
        {
            bool isResizing = false;
            System.Windows.Point resizeStart = default;
            double startW = 0, startH = 0;

            System.Windows.Input.MouseEventHandler onMove = (_, e) =>
            {
                if (!isResizing) return;
                ApplyResizeWithScreenClamp(window, startW, startH, e.GetPosition(null) - resizeStart);
            };

            System.Windows.Input.MouseButtonEventHandler? onUp = null;
            System.Windows.Input.MouseEventHandler? onLostCapture = null;

            void FinishResize(bool releaseCapture)
            {
                if (!isResizing) return;
                isResizing = false;
                grip.MouseMove -= onMove;
                if (onUp != null) grip.MouseLeftButtonUp -= onUp;
                if (onLostCapture != null) grip.LostMouseCapture -= onLostCapture;
                if (releaseCapture && grip.IsMouseCaptured) grip.ReleaseMouseCapture();
                onResizeCompleted?.Invoke();
            }

            onUp = (_, _) => FinishResize(releaseCapture: true);
            onLostCapture = (_, _) => FinishResize(releaseCapture: false);

            grip.MouseLeftButtonDown += (_, e) =>
            {
                if (isResizing) return;
                isResizing = true;
                resizeStart = e.GetPosition(null);
                startW = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
                startH = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
                grip.MouseMove += onMove;
                grip.MouseLeftButtonUp += onUp;
                grip.LostMouseCapture += onLostCapture;
                if (!grip.CaptureMouse())
                {
                    FinishResize(releaseCapture: false);
                }
                e.Handled = true;
            };
        }
    }
}
