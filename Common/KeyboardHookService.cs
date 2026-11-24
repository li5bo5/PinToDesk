using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PinToDesk.Common
{
    /// <summary>
    /// 已解析的热键描述：虚拟键码 + 各修饰键是否按下
    /// </summary>
    public class HotkeyDescriptor
    {
        public int VkCode { get; }
        public bool Win { get; }
        public bool Ctrl { get; }
        public bool Alt { get; }
        public bool Shift { get; }

        public HotkeyDescriptor(int vkCode, bool win, bool ctrl, bool alt, bool shift)
        {
            VkCode = vkCode; Win = win; Ctrl = ctrl; Alt = alt; Shift = shift;
        }

        /// <summary>
        /// 将 "Ctrl+D" / "Win+Shift+T" / "Alt+F1" / "Ctrl+`" 等字符串解析为描述符
        /// </summary>
        public static HotkeyDescriptor? Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var parts = raw.Split('+');
            bool win = false, ctrl = false, alt = false, shift = false;
            int vk = 0;
            foreach (var p in parts)
            {
                var upper = p.Trim().ToUpperInvariant();
                switch (upper)
                {
                    case "WIN":   win   = true; break;
                    case "CTRL":  ctrl  = true; break;
                    case "ALT":   alt   = true; break;
                    case "SHIFT": shift = true; break;
                    case "ESC":
                    case "ESCAPE":
                        vk = 0x1B;
                        break;
                    case "ENTER":
                    case "RETURN":
                        vk = 0x0D;
                        break;
                    case "UP":
                        vk = 0x26;
                        break;
                    case "DOWN":
                        vk = 0x28;
                        break;
                    case "LEFT":
                        vk = 0x25;
                        break;
                    case "RIGHT":
                        vk = 0x27;
                        break;
                    case "INSERT":
                    case "INS":
                        vk = 0x2D;
                        break;
                    case "SPACE":
                        vk = 0x20;
                        break;
                    case "TAB":
                        vk = 0x09;
                        break;
                    case "BACK":
                    case "BACKSPACE":
                        vk = 0x08;
                        break;
                    case "DELETE":
                    case "DEL":
                        vk = 0x2E;
                        break;
                    case "HOME":
                        vk = 0x24;
                        break;
                    case "END":
                        vk = 0x23;
                        break;
                    case "PAGEUP":
                    case "PGUP":
                    case "PRIOR":
                        vk = 0x21;
                        break;
                    case "PAGEDOWN":
                    case "PGDN":
                    case "NEXT":
                        vk = 0x22;
                        break;
                    case "`":
                    case "~":
                    case "OEMTILDE":
                    case "OEM3":
                        vk = 0xC0;
                        break;
                    case "-":
                    case "_":
                    case "OEMMINUS":
                        vk = 0xBD;
                        break;
                    case "=":
                    case "+":
                    case "OEMPLUS":
                        vk = 0xBB;
                        break;
                    case "[":
                    case "{":
                    case "OEMOPENBRACKETS":
                    case "OEM4":
                        vk = 0xDB;
                        break;
                    case "]":
                    case "}":
                    case "OEMCLOSEBRACKETS":
                    case "OEM6":
                        vk = 0xDD;
                        break;
                    case ";":
                    case ":":
                    case "OEMSEMICOLON":
                    case "OEM1":
                        vk = 0xBA;
                        break;
                    case "'":
                    case "\"":
                    case "OEMQUOTES":
                    case "OEM7":
                        vk = 0xDE;
                        break;
                    case ",":
                    case "<":
                    case "OEMCOMMA":
                        vk = 0xBC;
                        break;
                    case ".":
                    case ">":
                    case "OEMPERIOD":
                        vk = 0xBE;
                        break;
                    case "/":
                    case "?":
                    case "OEMQUESTION":
                    case "OEM2":
                        vk = 0xBF;
                        break;
                    case "\\":
                    case "|":
                    case "OEMPIPE":
                    case "OEM5":
                        vk = 0xDC;
                        break;
                    default:
                        if (upper.Length == 1)
                        {
                            char c = upper[0];
                            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                            {
                                vk = (int)c;
                            }
                        }
                        else if (upper.Length == 2 && upper[0] == 'D' && upper[1] >= '0' && upper[1] <= '9')
                        {
                            vk = (int)upper[1];
                        }
                        else if (upper.StartsWith("NUMPAD", StringComparison.Ordinal) &&
                                 int.TryParse(upper.Substring(6), out int numPadDigit) &&
                                 numPadDigit >= 0 && numPadDigit <= 9)
                        {
                            vk = 0x60 + numPadDigit;
                        }
                        else if (upper.Length >= 2 && upper[0] == 'F' && int.TryParse(upper.Substring(1), out int fNum)
                                 && fNum >= 1 && fNum <= 24)
                        {
                            vk = 0x70 + (fNum - 1);
                        }
                        break;
                }
            }
            if (vk == 0) return null;
            return new HotkeyDescriptor(vk, win, ctrl, alt, shift);
        }

        /// <summary>判断当前按键事件是否与本描述符匹配</summary>
        public bool Matches(int pressedVk, bool lwinDown, bool rwinDown, bool ctrlDown, bool altDown, bool shiftDown)
        {
            if (pressedVk != VkCode) return false;
            bool winDown = lwinDown || rwinDown;
            if (Win   != winDown)   return false;
            if (Ctrl  != ctrlDown)  return false;
            if (Alt   != altDown)   return false;
            if (Shift != shiftDown) return false;
            return true;
        }

        /// <summary>是否至少包含修饰键或功能键 F1-F24（防止误录纯字符单键劫持打字）</summary>
        public bool HasValidModifierOrFunctionKey =>
            Win || Ctrl || Alt || Shift || (VkCode >= 0x70 && VkCode <= 0x87);

        /// <summary>
        /// 将虚拟键码与修饰键格式化为与 Parse 100% 对称的标准热键字符串
        /// </summary>
        public static string? FormatFromVk(int vkCode, bool win, bool ctrl, bool alt, bool shift)
        {
            string? keyName = vkCode switch
            {
                >= 0x41 and <= 0x5A => ((char)vkCode).ToString(),
                >= 0x30 and <= 0x39 => ((char)vkCode).ToString(),
                >= 0x60 and <= 0x69 => $"NumPad{vkCode - 0x60}",
                >= 0x70 and <= 0x87 => $"F{vkCode - 0x70 + 1}",
                0x20 => "Space",
                0x09 => "Tab",
                0x08 => "Backspace",
                0x0D => "Enter",
                0x2E => "Delete",
                0x2D => "Insert",
                0x24 => "Home",
                0x23 => "End",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x25 => "Left",
                0x26 => "Up",
                0x27 => "Right",
                0x28 => "Down",
                0xC0 => "`",
                0xBD => "-",
                0xBB => "=",
                0xDB => "[",
                0xDD => "]",
                0xBA => ";",
                0xDE => "'",
                0xBC => ",",
                0xBE => ".",
                0xBF => "/",
                0xDC => "\\",
                _ => null
            };

            if (keyName == null) return null;

            var sb = new System.Text.StringBuilder();
            if (win) sb.Append("Win+");
            if (ctrl) sb.Append("Ctrl+");
            if (alt) sb.Append("Alt+");
            if (shift) sb.Append("Shift+");
            sb.Append(keyName);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 全局低级键盘钩子服务 — 支持多热键动态注册、快捷键直通录入与 Win 键 0xFF 防弹菜单注入
    /// </summary>
    public class KeyboardHookService : IDisposable
    {
        /// <summary>
        /// 当处于快捷键录入捕获模式时设为 true，拦截物理组合键直通回调 KeyCaptured
        /// </summary>
        public bool IsCapturing { get; set; } = false;

        /// <summary>
        /// 录入模式下捕获到有效组合键时触发（已格式化为标准热键文本）
        /// </summary>
        public event Action<string>? KeyCaptured;

        private readonly List<(HotkeyDescriptor Desc, Func<bool> Callback)> _hotkeys = new();
        private NativeMethods.LowLevelProc? _proc;
        private IntPtr _hookID = IntPtr.Zero;

        public KeyboardHookService()
        {
            _proc = HookCallback;
            InstallHook();
        }

        private void InstallHook()
        {
            if (_hookID != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }

            if (_proc != null)
            {
                _hookID = SetHook(_proc);
                if (_hookID == IntPtr.Zero)
                {
                    System.Threading.Thread.Sleep(50);
                    _hookID = SetHook(_proc);
                }
            }
        }

        /// <summary>动态注册热键（无条件拦截）</summary>
        public void RegisterHotkey(string hotkeyString, Action callback)
        {
            RegisterHotkey(hotkeyString, () => { callback(); return true; });
        }

        /// <summary>动态注册热键（按需条件拦截：返回 true 拦截按键，返回 false 放行物理按键）</summary>
        public void RegisterHotkey(string hotkeyString, Func<bool> callback)
        {
            var desc = HotkeyDescriptor.Parse(hotkeyString);
            if (desc == null || callback == null) return;

            _hotkeys.RemoveAll(h => h.Desc.VkCode == desc.VkCode
                                 && h.Desc.Win == desc.Win
                                 && h.Desc.Ctrl == desc.Ctrl
                                 && h.Desc.Alt == desc.Alt
                                 && h.Desc.Shift == desc.Shift);
            _hotkeys.Add((desc, callback));
        }

        /// <summary>注销所有已注册的热键</summary>
        public void UnregisterAll()
        {
            _hotkeys.Clear();
        }

        private static IntPtr SetHook(NativeMethods.LowLevelProc proc)
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule?.ModuleName != null)
                return NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc, NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
            return IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

                // 软件注入的按键直接放行，避免模拟按键时死循环
                if ((hookStruct.flags & NativeMethods.LLKHF_INJECTED) != 0)
                {
                    return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
                }

                int vkCode = hookStruct.vkCode;
                int message = wParam.ToInt32();

                bool isKeyDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
                if (isKeyDown)
                {
                    bool lwinDown  = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN)  & 0x8000) != 0;
                    bool rwinDown  = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN)  & 0x8000) != 0;
                    bool ctrlDown  = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CTRL)  & 0x8000) != 0;
                    bool altDown   = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ALT)   & 0x8000) != 0;
                    bool shiftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;

                    // 处于快捷键捕获录入状态时：直接拦截并回报组合键（含 Win+D 等系统保留热键）
                    if (IsCapturing)
                    {
                        bool isModifierKey = vkCode == NativeMethods.VK_LWIN || vkCode == NativeMethods.VK_RWIN ||
                                             vkCode == NativeMethods.VK_CTRL || vkCode == 0xA2 || vkCode == 0xA3 ||
                                             vkCode == NativeMethods.VK_ALT || vkCode == 0xA4 || vkCode == 0xA5 ||
                                             vkCode == NativeMethods.VK_SHIFT || vkCode == 0xA0 || vkCode == 0xA1;
                        if (isModifierKey || vkCode == NativeMethods.VK_ESCAPE)
                        {
                            return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
                        }

                        string? formatted = HotkeyDescriptor.FormatFromVk(vkCode, lwinDown || rwinDown, ctrlDown, altDown, shiftDown);
                        if (!string.IsNullOrEmpty(formatted))
                        {
                            if (lwinDown || rwinDown)
                            {
                                NativeMethods.keybd_event(0xFF, 0, 0, UIntPtr.Zero);
                                NativeMethods.keybd_event(0xFF, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
                            }

                            string captured = formatted;
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                KeyCaptured?.Invoke(captured);
                            }));
                            return (IntPtr)1;
                        }
                        return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
                    }

                    foreach (var (desc, callback) in _hotkeys)
                    {
                        if (desc.Matches(vkCode, lwinDown, rwinDown, ctrlDown, altDown, shiftDown))
                        {
                            bool handled = callback();
                            if (handled)
                            {
                                // 若涉及 Win 键，注入 0xFF 防止开始菜单弹出
                                if (desc.Win)
                                {
                                    NativeMethods.keybd_event(0xFF, 0, 0, UIntPtr.Zero);
                                    NativeMethods.keybd_event(0xFF, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
                                }
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookID != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
            _hotkeys.Clear();
        }
    }

    /// <summary>
    /// 轻量级全局鼠标低级钩子：在激活期间监听任意鼠标按键按下事件，判断是否在指定窗口外部
    /// </summary>
    public class GlobalMouseClickMonitor : IDisposable
    {
        private NativeMethods.LowLevelProc? _proc;
        private IntPtr _hookID = IntPtr.Zero;

        private readonly HashSet<IntPtr> _protectedHwnds = new();

        /// <summary>点击发生在保护窗口外部时触发</summary>
        public event Action? ClickedOutside;

        public void Start(IEnumerable<IntPtr> protectedHwnds)
        {
            _protectedHwnds.Clear();
            foreach (var h in protectedHwnds) _protectedHwnds.Add(h);

            if (_hookID != IntPtr.Zero) return;

            _proc = MouseHookCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule?.ModuleName != null)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(curModule.ModuleName);
                _hookID = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
                if (_hookID == IntPtr.Zero)
                {
                    System.Threading.Thread.Sleep(30);
                    _hookID = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
                }
            }
        }

        public void Stop()
        {
            if (_hookID != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_RBUTTONDOWN ||
                    msg == NativeMethods.WM_MBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    var hwndClicked = NativeMethods.WindowFromPoint(info.pt);
                    var rootClicked = NativeMethods.GetAncestor(hwndClicked, NativeMethods.GA_ROOT);

                    bool isProtected = _protectedHwnds.Contains(hwndClicked)
                                    || _protectedHwnds.Contains(rootClicked);

                    if (!isProtected)
                    {
                        ClickedOutside?.Invoke();
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose() => Stop();
    }
}
