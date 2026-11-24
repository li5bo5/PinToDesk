using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PinToDesk.Common;
using PinToDesk.Models;
using PinToDesk.Services;
using WinPoint  = System.Windows.Point;
using WinMouse  = System.Windows.Input.MouseEventArgs;
using WinButton = System.Windows.Controls.Button;
using WinPanel  = System.Windows.Controls.Panel;

namespace PinToDesk
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<TodoItem> _items = new();
        private readonly TodoService _todoService;
        private readonly RecycleBinService _recycleBinService;
        private readonly SettingsManager _settingsManager;

        public RecycleBinService RecycleBinService => _recycleBinService;

        // 拖拽平滑排序动画状态
        private WinPoint _dragStartPos;
        private WinPoint _lastMousePos;
        private TodoItem? _dragItem;
        private System.Windows.Controls.ListViewItem? _dragContainer;
        private bool _isDragging = false;
        private int _dragStartIndex = -1;
        private int _targetDropIndex = -1;
        private double _dragGrabOffsetY = 0;
        private double _dragStartContainerTopInList = 0;
        private double _dragStartScrollOffset = 0;
        private ScrollViewer? _todoScrollViewer;
        private DispatcherTimer? _autoScrollTimer;
        private double _autoScrollVelocity = 0;
        private readonly List<(double Top, double Height)> _itemInitialBounds = new();

        // 置顶 / 穿透（严格联动状态）
        private bool _isPinned      = false;
        private bool _isPassThrough = false;

        // 托盘引用（用于任务栏重建时恢复托盘图标）
        private TrayHelper? _tray;
        public void SetTray(TrayHelper tray) => _tray = tray;

        private const int WM_SHOWWINDOW = 0x0018;
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const int SWP_HIDEWINDOW = 0x0080;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        private bool _isExplicitlyHiding = false;

        public void SafeHide()
        {
            _isExplicitlyHiding = true;
            try
            {
                StopPassThroughMonitor();
                Hide();
            }
            finally
            {
                _isExplicitlyHiding = false;
            }
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                if (_isPassThrough)
                {
                    StartPassThroughMonitor();
                }
            }
            else
            {
                StopPassThroughMonitor();
            }
        }

        // ══════════════════════════════════════════════
        // 构造函数
        // ══════════════════════════════════════════════
        public MainWindow(SettingsManager settingsManager)
        {
            InitializeComponent();
            _settingsManager = settingsManager;

            // 监听窗口可见性切换以启停穿透监听器
            IsVisibleChanged += MainWindow_IsVisibleChanged;

            // 确保窗口句柄创建并立即挂钩 WndProc，确保 Win+D 等底层拦截从第一毫秒起生效
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource?.AddHook(WndProc);

            _todoService = new TodoService();
            _recycleBinService = new RecycleBinService(_settingsManager);
            foreach (var item in _todoService.LoadTodos())
                _items.Add(item);
            TodoList.ItemsSource = _items;

            // 监听集合变化，同步空列表占位符
            _items.CollectionChanged += (s, e) => UpdateEmptyPlaceholder();

            // 订阅状态变化：免疫 Win+D 强制最小化
            StateChanged += MainWindow_StateChanged;

            // 监听鼠标捕获丢失，安全终止拖拽与滚屏并复位视觉
            TodoList.LostMouseCapture += (s, e) =>
            {
                if (_isDragging)
                {
                    ResetAllDragVisuals();
                    _isDragging = false;
                    _dragItem = null;
                    _dragContainer = null;
                    _dragStartIndex = -1;
                    _targetDropIndex = -1;
                    _itemInitialBounds.Clear();
                }
            };

            // 初始化或恢复保存的窗口位置与大小
            ApplyInitialOrSavedBounds();

            // 载入并应用保存的设置
            LoadSettings();

            // 监听统一偏好设置变更广播（先同步置顶再同步穿透，确保“穿透必置顶”不被反向撤销）
            _settingsManager.SettingsChanged += (s, settings) =>
            {
                Dispatcher.Invoke(() =>
                {
                    bool effectivePinned = settings.TodoIsPinned || settings.TodoIsPassThrough;
                    if (_isPinned != effectivePinned)
                    {
                        SetPinned(effectivePinned);
                    }
                    if (_isPassThrough != settings.TodoIsPassThrough)
                    {
                        ApplyPassThroughState(settings.TodoIsPassThrough);
                    }
                });
            };

            // 统一挂载右下角拖拽缩放与落盘保存
            NativeMethods.AttachResizeGrip(this, ResizeGripArea, SaveWindowBounds);

            // 初始化占位符状态
            UpdateEmptyPlaceholder();

            // 初始状态下标题栏按钮保持隐藏（仅悬停时浮现）
            Loaded += (s, e) =>
            {
                UpdateEmptyPlaceholder();
                SetTitleButtonsOpacity(0);
            };
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                if (!IsVisible) Show();
            }
        }

        // ══════════════════════════════════════════════
        // 窗口初始化：注入 WS_EX_TOOLWINDOW 并绑定桌面宿主
        // ══════════════════════════════════════════════
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;

            int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            extendedStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle);
            // 所有者绑定桌面 Progman：非置顶时 Win+D 随桌面一同显示
            NativeMethods.BindOwnerToProgman(hwnd);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 1. 免疫 Win + D（显示桌面）：多重防御拦截
            // A. 拦截系统命令最小化 (Win+M 或 Win+D 触发的最小化)
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
            {
                handled = true;
                return IntPtr.Zero;
            }

            // B. 拦截非显式主动调用的隐藏指令 (Win+D 发送的 WM_SHOWWINDOW 消息)
            if (msg == WM_SHOWWINDOW && wParam == IntPtr.Zero)
            {
                if (!_isExplicitlyHiding)
                {
                    handled = true;
                    EnsureWindowAboveDesktop();
                    return IntPtr.Zero;
                }
            }

            // C. 拦截通过 SetWindowPos 强制注入 SWP_HIDEWINDOW 的外部指令
            if (msg == WM_WINDOWPOSCHANGING && !_isExplicitlyHiding)
            {
                try
                {
                    var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                    if ((pos.flags & (uint)SWP_HIDEWINDOW) != 0)
                    {
                        pos.flags &= ~((uint)SWP_HIDEWINDOW);
                        Marshal.StructureToPtr(pos, lParam, false);
                    }
                }
                catch { }
            }

            // 2. 任务栏重建自愈（Explorer 重启后恢复托盘图标并重绑桌面宿主）
            if (msg == NativeMethods.WM_TASKBARCREATED)
            {
                _tray?.EnsureVisible();
                NativeMethods.BindOwnerToProgman(hwnd);
            }
            return IntPtr.Zero;
        }

        private void EnsureWindowAboveDesktop()
        {
            const uint flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
            Action updatePos = () =>
            {
                var h = new WindowInteropHelper(this).Handle;
                if (h != IntPtr.Zero && IsVisible)
                {
                    if (_isPinned)
                    {
                        NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
                    }
                    else
                    {
                        // 非置顶状态：先刷到 TOPMOST 再落回 NOTOPMOST，确保跃居刚提升的桌面之上但保持非置顶
                        NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
                        NativeMethods.SetWindowPos(h, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
                    }
                }
            };

            Dispatcher.BeginInvoke(DispatcherPriority.Render, updatePos);

            // 50ms 延时再次确认，跨越 Windows Shell 显示桌面切换的动画滞后
            Task.Delay(50).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, updatePos);
            });
        }

        // ══════════════════════════════════════════════
        // TitleBar 区域悬停：严格鼠标悬停显隐控制
        // ══════════════════════════════════════════════
        private void TitleBar_MouseEnter(object sender, WinMouse e) => SetTitleButtonsOpacity(1);
        private void TitleBar_MouseLeave(object sender, WinMouse e) => SetTitleButtonsOpacity(0);

        private void SetTitleButtonsOpacity(double opacity)
        {
            if (AddBtn != null) AddBtn.Opacity                 = opacity;
            if (PinBtn != null) PinBtn.Opacity                 = opacity;
            if (PassThroughBtn != null) PassThroughBtn.Opacity = opacity;
            if (CloseBtn != null) CloseBtn.Opacity             = opacity;
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowCreateTodoDialog();
        }

        // ResizeGrip 区域悬停：控制 Grip 显示
        private void ResizeGrip_MouseEnter(object sender, WinMouse e)
        {
            if (!_isPinned) ResizeGripArea.Opacity = 1;
        }
        private void ResizeGrip_MouseLeave(object sender, WinMouse e) => ResizeGripArea.Opacity = 0;

        // ══════════════════════════════════════════════
        // 标题栏拖动（自由跟随 + 松手连通多屏回弹）
        // ══════════════════════════════════════════════
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                NativeMethods.DragMoveWithBounds(this);
                SaveWindowBounds();
            }
        }

        // ══════════════════════════════════════════════
        // 标题栏按钮
        // ══════════════════════════════════════════════
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => SafeHide();

        private void PinBtn_Click(object sender, RoutedEventArgs e)
        {
            TogglePinState();
        }

        public void SetPinned(bool pinned)
        {
            _isPinned = pinned;
            this.Topmost = _isPinned;
            UpdatePinButton();

            // 置顶时同步禁用右下角缩放手柄命中测试，避免隐藏状态下仍可拖拽缩放
            if (ResizeGripArea != null)
            {
                ResizeGripArea.IsHitTestVisible = !_isPinned;
                if (_isPinned) ResizeGripArea.Opacity = 0;
            }

            // 若取消置顶，且当前正处于穿透模式，则必须强制联动退出穿透模式，严禁出现“非置顶 + 鼠标穿透”悬挂态
            if (!_isPinned && _isPassThrough)
            {
                ApplyPassThroughState(false);
            }
        }

        private void TogglePinState()
        {
            SetPinned(!_isPinned);
            SaveSettings();
        }

        private void UpdatePinButton()
        {
            PinBtn.Content = "📌";
            if (TopAccentLine != null)
                TopAccentLine.Visibility = _isPinned ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══════════════════════════════════════════════
        // 鼠标穿透控制逻辑（标题栏热区自动临时解除穿透）
        // ══════════════════════════════════════════════
        private void PassThroughBtn_Click(object sender, RoutedEventArgs e)
        {
            TogglePassThroughState();
        }

        private void TogglePassThroughState()
        {
            _isPassThrough = !_isPassThrough;
            ApplyPassThroughState(_isPassThrough);
            SaveSettings();
        }

        private DispatcherTimer? _passThroughMonitorTimer;
        private bool _isPassThroughHovered = false;

        private void StartPassThroughMonitor()
        {
            if (_passThroughMonitorTimer == null)
            {
                _passThroughMonitorTimer = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(40)
                };
                _passThroughMonitorTimer.Tick += PassThroughMonitorTimer_Tick;
            }
            _passThroughMonitorTimer.Start();
        }

        private void StopPassThroughMonitor()
        {
            _passThroughMonitorTimer?.Stop();
            _isPassThroughHovered = false;
        }

        private void PassThroughMonitorTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isPassThrough || !IsVisible || WindowState == WindowState.Minimized)
            {
                StopPassThroughMonitor();
                return;
            }

            // 若当前有正在交互的弹窗或偏好设置窗口，保持控制按钮可点击状态，暂不恢复穿透
            if (IsPreferencesWindowOpen || ActiveEditDialog != null)
            {
                return;
            }

            if (!NativeMethods.GetCursorPos(out var screenPoint)) return;

            WinPoint relPos;
            try
            {
                relPos = PointFromScreen(new WinPoint(screenPoint.X, screenPoint.Y));
            }
            catch
            {
                return;
            }

            // 顶部区域：涵盖 TitleBar 范围（高度 40px，向外宽容 4px 缓冲区以便顺滑移入）
            double barHeight = TitleBar.ActualHeight > 0 ? TitleBar.ActualHeight : 40;
            bool inTitleBarZone = (relPos.X >= 0 && relPos.X <= ActualWidth && relPos.Y >= 0 && relPos.Y <= barHeight + 4);

            if (inTitleBarZone)
            {
                if (!_isPassThroughHovered)
                {
                    _isPassThroughHovered = true;
                    SetTitleButtonsOpacity(1);
                    SetWindowPassThrough(false);
                }
            }
            else
            {
                if (_isPassThroughHovered)
                {
                    _isPassThroughHovered = false;
                    SetTitleButtonsOpacity(0);
                    SetWindowPassThrough(true);
                }
            }
        }

        private void SetWindowPassThrough(bool transparent)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if (transparent)
            {
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_TRANSPARENT);
            }
            else
            {
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle & ~NativeMethods.WS_EX_TRANSPARENT);
            }
        }

        private void ApplyPassThroughState(bool isPassThrough)
        {
            _isPassThrough = isPassThrough;
            PassThroughBtn.Content = _isPassThrough ? "◉" : "⊙";
            if (_isPassThrough)
            {
                ResizeGripArea.Opacity = 0;
                // 开启穿透模式时，强制置顶，防止穿透点击下层窗口时导致 TodoList 被覆盖
                if (!_isPinned)
                {
                    SetPinned(true);
                }
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                if (_isPassThrough)
                {
                    SetTitleButtonsOpacity(0);
                    SetWindowPassThrough(true);
                    StartPassThroughMonitor();
                }
                else
                {
                    StopPassThroughMonitor();
                    SetWindowPassThrough(false);
                }
            }
        }

        public bool IsPreferencesWindowOpen { get; set; } = false;
        public EditDialog? ActiveEditDialog { get; set; } = null;

        // ══════════════════════════════════════════════
        // 待办创建与编辑统一弹窗呼出
        // ══════════════════════════════════════════════
        private void ShowCreateTodoDialog()
        {
            if (IsPreferencesWindowOpen) return;

            DiagnosticLogger.RecordStep("Todo", "打开新建待办弹窗");
            var dlg = new EditDialog("新建待办", "") { Owner = this };
            ActiveEditDialog = dlg;
            try
            {
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
                {
                    AddTodoItem(dlg.ResultText);
                    DiagnosticLogger.RecordStep("Todo", $"新建待办完成 (Len={dlg.ResultText.Length})");
                }
            }
            finally
            {
                ActiveEditDialog = null;
            }
        }

        private void TodoList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsPreferencesWindowOpen) return;

            var src = e.OriginalSource as DependencyObject;
            while (src != null)
            {
                if (src is System.Windows.Controls.Button) return;
                src = VisualTreeHelper.GetParent(src);
            }

            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is TodoItem item)
            {
                ShowEditDialog(item);
                return;
            }

            ShowCreateTodoDialog();
        }

        public void HideInlineInput()
        {
            if (ActiveEditDialog != null)
            {
                ActiveEditDialog.Close();
                ActiveEditDialog = null;
            }
        }

        public void HideInlineInputAndWindow()
        {
            HideInlineInput();
            SafeHide();
        }

        /// <summary>
        /// 快速唤出 / 隐藏待办清单窗口（供托盘双击与快捷键统一调用）：
        /// 1. 若当前在视野中无遮挡（在顶层激活，或桌面无其他窗口遮挡待办） -> 直接收起隐藏；
        /// 2. 若当前被隐藏或被其他应用窗口遮挡 -> 穿透前台锁，瞬间跃居屏幕最前方并获得焦点。
        /// </summary>
        public void ToggleVisibility()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            bool isUncovered = false;

            if (IsVisible && _isPassThrough)
            {
                // 穿透模式强制置顶且带 WS_EX_TRANSPARENT（WindowFromPoint 会穿透），必然位于视野最上层，直接一键隐藏
                isUncovered = true;
            }
            else if (IsVisible && hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                // 直接从窗口真实物理矩形取中心点（兼容多显示器异构 DPI 缩放）
                int centerX = (rect.Left + rect.Right) / 2;
                int centerY = (rect.Top + rect.Bottom) / 2;

                // 探测物理屏幕上覆盖在此坐标最上层的顶层窗口
                IntPtr hitWnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = centerX, Y = centerY });
                IntPtr hitRoot = hitWnd != IntPtr.Zero ? NativeMethods.GetAncestor(hitWnd, NativeMethods.GA_ROOT) : IntPtr.Zero;

                isUncovered = (hitRoot == hwnd || NativeMethods.GetForegroundWindow() == hwnd);
            }

            if (isUncovered)
            {
                HideInlineInputAndWindow();
            }
            else
            {
                if (!IsVisible)
                {
                    Show();
                }
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                if (hwnd != IntPtr.Zero)
                {
                    const uint flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_SHOWWINDOW;
                    if (_isPinned)
                    {
                        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
                    }
                    else
                    {
                        // 经典双阶段 Z 序提升：先 TOPMOST 再 NOTOPMOST，突破 Windows Z-Order 下沉粘滞
                        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
                        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
                    }

                    NativeMethods.ForceSetForegroundWindow(hwnd);
                }

                Activate();
            }
        }

        private void UpdateEmptyPlaceholder()
        {
            EmptyPlaceholder.Visibility =
                _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══════════════════════════════════════════════
        // 编辑 / 删除 / 恢复
        // ══════════════════════════════════════════════
        private void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsPreferencesWindowOpen) return;

            var id   = (Guid)((WinButton)sender).Tag;
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null) ShowEditDialog(item);
        }

        public void AddTodoItem(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            _items.Add(new TodoItem { Title = title });
            _todoService.SaveTodos(_items);
            if (_items.Count > 0)
                TodoList.ScrollIntoView(_items[^1]);
        }

        /// <summary>
        /// 从回收站还原待办事项（保持原始 ID 和创建时间，防止同 ID 重复恢复）
        /// </summary>
        public void RestoreTodoItem(TodoItem restoredItem)
        {
            if (restoredItem == null || string.IsNullOrWhiteSpace(restoredItem.Title)) return;
            if (_items.Any(x => x.Id == restoredItem.Id)) return;
            _items.Add(restoredItem);
            _todoService.SaveTodos(_items);
            _todoService.Flush();
            if (_items.Count > 0)
                TodoList.ScrollIntoView(_items[^1]);
        }

        public void DeleteTodoItem(TodoItem item)
        {
            if (item == null) return;
            _recycleBinService.AddToRecycleBin(item);
            _items.Remove(item);
            _todoService.SaveTodos(_items);
            _todoService.Flush();
        }

        private void ShowEditDialog(TodoItem item)
        {
            if (IsPreferencesWindowOpen) return;

            DiagnosticLogger.RecordStep("Todo", "打开编辑待办弹窗");
            var dlg = new EditDialog("编辑待办", item.Title) { Owner = this };
            ActiveEditDialog = dlg;
            try
            {
                if (dlg.ShowDialog() == true)
                {
                    if (string.IsNullOrWhiteSpace(dlg.ResultText))
                    {
                        // 空值即删除
                        DeleteTodoItem(item);
                        DiagnosticLogger.RecordStep("Todo", "编辑待办置空自动删除");
                    }
                    else
                    {
                        item.Title = dlg.ResultText;
                        _todoService.SaveTodos(_items);
                        DiagnosticLogger.RecordStep("Todo", $"编辑待办保存完成 (Len={dlg.ResultText.Length})");
                    }
                }
            }
            finally
            {
                ActiveEditDialog = null;
            }
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var id   = (Guid)((WinButton)sender).Tag;
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                DeleteTodoItem(item);
            }
        }

        // ══════════════════════════════════════════════
        // 拖拽排序与平滑位移动画
        // ══════════════════════════════════════════════
        private bool IsClickOnInteractiveControl(DependencyObject? src)
        {
            while (src != null && src != TodoList)
            {
                if (src is System.Windows.Controls.Button || src is System.Windows.Controls.Primitives.ButtonBase)
                    return true;
                src = VisualTreeHelper.GetParent(src);
            }
            return false;
        }

        private ScrollViewer? GetTodoScrollViewer()
        {
            if (_todoScrollViewer != null) return _todoScrollViewer;
            if (TodoList.Template != null)
            {
                _todoScrollViewer = TodoList.Template.FindName("PART_ScrollViewer", TodoList) as ScrollViewer;
            }
            if (_todoScrollViewer == null)
            {
                _todoScrollViewer = FindVisualChild<ScrollViewer>(TodoList);
            }
            return _todoScrollViewer;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void StartAutoScrollTimer()
        {
            if (_autoScrollTimer == null)
            {
                _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(20)
                };
                _autoScrollTimer.Tick += AutoScrollTimer_Tick;
            }
            _autoScrollTimer.Start();
        }

        private void StopAutoScrollTimer()
        {
            _autoScrollVelocity = 0;
            _autoScrollTimer?.Stop();
        }

        private void AutoScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isDragging || _autoScrollVelocity == 0) return;

            var sv = GetTodoScrollViewer();
            if (sv == null || sv.ScrollableHeight <= 0) return;

            double oldOffset = sv.VerticalOffset;
            double newOffset = Math.Clamp(oldOffset + _autoScrollVelocity, 0, sv.ScrollableHeight);
            if (Math.Abs(newOffset - oldOffset) > 0.1)
            {
                sv.ScrollToVerticalOffset(newOffset);
                UpdateDragPositionAndSlots();
            }
        }

        private void CheckAutoScrollZone(WinPoint currentPos)
        {
            const double edgeThreshold = 36.0;
            double listHeight = TodoList.ActualHeight;

            if (listHeight <= 0)
            {
                _autoScrollVelocity = 0;
                return;
            }

            if (currentPos.Y < edgeThreshold)
            {
                double d = edgeThreshold - currentPos.Y;
                _autoScrollVelocity = -Math.Min(9.0, Math.Max(2.0, d * 0.25));
            }
            else if (currentPos.Y > listHeight - edgeThreshold)
            {
                double d = currentPos.Y - (listHeight - edgeThreshold);
                _autoScrollVelocity = Math.Min(9.0, Math.Max(2.0, d * 0.25));
            }
            else
            {
                _autoScrollVelocity = 0;
            }
        }

        private void TodoList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsClickOnInteractiveControl(e.OriginalSource as DependencyObject))
            {
                _dragItem = null;
                _dragContainer = null;
                return;
            }

            var container = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
            if (container != null && container.DataContext is TodoItem item)
            {
                _dragItem = item;
                _dragContainer = container;
                _dragStartPos = e.GetPosition(TodoList);
                _lastMousePos = _dragStartPos;
                _isDragging = false;
                _dragStartIndex = _items.IndexOf(item);
                _targetDropIndex = _dragStartIndex;

                _dragGrabOffsetY = e.GetPosition(_dragContainer).Y;
                _dragStartContainerTopInList = _dragContainer.TranslatePoint(new WinPoint(0, 0), TodoList).Y;

                var sv = GetTodoScrollViewer();
                _dragStartScrollOffset = sv?.VerticalOffset ?? 0;
            }
        }

        private void TodoList_PreviewMouseMove(object sender, WinMouse e)
        {
            if (_dragItem == null || _dragContainer == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var currentPos = e.GetPosition(TodoList);
            _lastMousePos = currentPos;
            var deltaY = currentPos.Y - _dragStartPos.Y;

            // 移动超过阈值才启动拖拽状态，保护正常单击与双击
            if (!_isDragging && Math.Abs(deltaY) > 4)
            {
                _isDragging = true;
                TodoList.CaptureMouse();
                WinPanel.SetZIndex(_dragContainer, 999);
                _dragContainer.Opacity = 0.88;

                var sv = GetTodoScrollViewer();
                _dragStartScrollOffset = sv?.VerticalOffset ?? 0;
                _dragStartContainerTopInList = _dragContainer.TranslatePoint(new WinPoint(0, 0), TodoList).Y;

                _itemInitialBounds.Clear();
                for (int i = 0; i < _items.Count; i++)
                {
                    var container = TodoList.ItemContainerGenerator.ContainerFromItem(_items[i]) as System.Windows.Controls.ListViewItem;
                    if (container != null)
                    {
                        if (container.RenderTransform is not TranslateTransform trans || trans.IsFrozen)
                        {
                            container.RenderTransform = new TranslateTransform(0, 0);
                        }
                        double top = container.TranslatePoint(new WinPoint(0, 0), TodoList).Y;
                        _itemInitialBounds.Add((top, container.ActualHeight));
                    }
                    else
                    {
                        _itemInitialBounds.Add((0, 0));
                    }
                }

                StartAutoScrollTimer();
            }

            if (!_isDragging) return;

            CheckAutoScrollZone(currentPos);
            UpdateDragPositionAndSlots(currentPos);
        }

        private void UpdateDragPositionAndSlots(WinPoint? mousePos = null)
        {
            if (!_isDragging || _dragContainer == null || _dragItem == null) return;

            var currentPos = mousePos ?? _lastMousePos;
            var sv = GetTodoScrollViewer();
            double currentScrollOffset = sv?.VerticalOffset ?? 0;

            double desiredVisualTop = currentPos.Y - _dragGrabOffsetY;
            double maxVisualTop = Math.Max(0, TodoList.ActualHeight - _dragContainer.ActualHeight);
            double clampedVisualTop = Math.Clamp(desiredVisualTop, 0, maxVisualTop);

            double scrollDelta = currentScrollOffset - _dragStartScrollOffset;
            double currentBaseTop = _dragStartContainerTopInList - scrollDelta;
            double transformY = clampedVisualTop - currentBaseTop;

            if (_dragContainer.RenderTransform is TranslateTransform dragTransform && !dragTransform.IsFrozen)
            {
                dragTransform.Y = transformY;
            }
            else
            {
                _dragContainer.RenderTransform = new TranslateTransform(0, transformY);
            }

            int newTargetIndex = _dragStartIndex;

            bool isAtTopEdge = clampedVisualTop <= 6 || (currentPos.Y <= 28 && (sv == null || sv.VerticalOffset <= 2));
            bool isAtBottomEdge = (clampedVisualTop >= maxVisualTop - 6 && maxVisualTop > 0) ||
                                  (currentPos.Y >= TodoList.ActualHeight - 28 && (sv == null || sv.VerticalOffset >= (sv.ScrollableHeight - 2)));

            if (isAtTopEdge)
            {
                newTargetIndex = 0;
            }
            else if (isAtBottomEdge)
            {
                newTargetIndex = _items.Count - 1;
            }
            else
            {
                double dragCenterY = clampedVisualTop + _dragContainer.ActualHeight / 2;

                for (int i = 0; i < _items.Count; i++)
                {
                    if (i == _dragStartIndex || i >= _itemInitialBounds.Count) continue;

                    double otherBaseCenterY = _itemInitialBounds[i].Top - scrollDelta + _itemInitialBounds[i].Height / 2;

                    if (i < _dragStartIndex)
                    {
                        if (dragCenterY < otherBaseCenterY)
                        {
                            newTargetIndex = Math.Min(newTargetIndex, i);
                        }
                    }
                    else if (i > _dragStartIndex)
                    {
                        if (dragCenterY > otherBaseCenterY)
                        {
                            newTargetIndex = Math.Max(newTargetIndex, i);
                        }
                    }
                }
            }

            if (newTargetIndex != _targetDropIndex)
            {
                _targetDropIndex = newTargetIndex;
                double itemHeight = _dragContainer.ActualHeight + 4;

                for (int i = 0; i < _items.Count; i++)
                {
                    if (i == _dragStartIndex) continue;
                    var container = TodoList.ItemContainerGenerator.ContainerFromItem(_items[i]) as System.Windows.Controls.ListViewItem;
                    if (container == null) continue;

                    double targetOffsetY = 0;
                    if (_targetDropIndex > _dragStartIndex)
                    {
                        if (i > _dragStartIndex && i <= _targetDropIndex)
                        {
                            targetOffsetY = -itemHeight;
                        }
                    }
                    else if (_targetDropIndex < _dragStartIndex)
                    {
                        if (i >= _targetDropIndex && i < _dragStartIndex)
                        {
                            targetOffsetY = itemHeight;
                        }
                    }

                    if (container.RenderTransform is not TranslateTransform trans || trans.IsFrozen)
                    {
                        trans = new TranslateTransform(0, 0);
                        container.RenderTransform = trans;
                    }

                    var anim = new DoubleAnimation(targetOffsetY, new Duration(TimeSpan.FromMilliseconds(160)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    trans.BeginAnimation(TranslateTransform.YProperty, anim);
                }
            }
        }

        private void ResetAllDragVisuals()
        {
            StopAutoScrollTimer();
            foreach (var it in _items)
            {
                var container = TodoList.ItemContainerGenerator.ContainerFromItem(it) as System.Windows.Controls.ListViewItem;
                if (container != null)
                {
                    WinPanel.SetZIndex(container, 0);
                    container.BeginAnimation(UIElement.OpacityProperty, null);
                    container.Opacity = 1.0;
                    if (container.RenderTransform is TranslateTransform trans && !trans.IsFrozen)
                    {
                        trans.BeginAnimation(TranslateTransform.YProperty, null);
                        trans.Y = 0;
                    }
                    container.RenderTransform = new TranslateTransform(0, 0);
                }
            }
        }

        private void TodoList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                _dragItem = null;
                _dragContainer = null;
                return;
            }

            int fromIndex = _dragStartIndex;
            int toIndex = _targetDropIndex;
            var dragItem = _dragItem;

            // 必须在 ReleaseMouseCapture 之前将 _isDragging 设为 false，
            // 避免 Release 激发的 LostMouseCapture 事件将目标索引意外重置为 -1
            _isDragging = false;
            _dragItem = null;
            _dragContainer = null;
            _dragStartIndex = -1;
            _targetDropIndex = -1;
            _itemInitialBounds.Clear();

            TodoList.ReleaseMouseCapture();
            ResetAllDragVisuals();

            if (toIndex >= 0 && toIndex != fromIndex && toIndex < _items.Count && dragItem != null)
            {
                _items.Move(fromIndex, toIndex);
                _todoService.SaveTodos(_items);
                AnimateItemFadeIn(dragItem);
            }

            e.Handled = true;
        }

        private void AnimateItemFadeIn(TodoItem item)
        {
            var container = TodoList.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.Controls.ListViewItem;
            if (container == null) return;
            var fade = new DoubleAnimation(0.5, 1.0, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                FillBehavior = FillBehavior.Stop
            };
            fade.Completed += (s, e) =>
            {
                container.BeginAnimation(UIElement.OpacityProperty, null);
                container.Opacity = 1.0;
            };
            container.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void LoadSettings()
        {
            var settings = _settingsManager.Settings;
            SetPinned(settings.TodoIsPinned);
            ApplyPassThroughState(settings.TodoIsPassThrough);
        }

        private void SaveSettings()
        {
            var settings = _settingsManager.Settings;
            settings.TodoIsPinned = _isPinned;
            settings.TodoIsPassThrough = _isPassThrough;
            _settingsManager.SaveSettings(settings);
        }

        // ══════════════════════════════════════════════
        // 窗口大小与位置记忆 / 恢复 / 越界保护
        // ══════════════════════════════════════════════
        private void ApplyInitialOrSavedBounds()
        {
            var settings = _settingsManager.Settings;
            if (settings.TodoWindowLeft.HasValue &&
                settings.TodoWindowTop.HasValue &&
                settings.TodoWindowWidth.HasValue &&
                settings.TodoWindowHeight.HasValue)
            {
                double savedLeft   = settings.TodoWindowLeft.Value;
                double savedTop    = settings.TodoWindowTop.Value;
                double savedWidth  = Math.Max(MinWidth, settings.TodoWindowWidth.Value);
                double savedHeight = Math.Max(MinHeight, settings.TodoWindowHeight.Value);

                if (IsWindowBoundsVisible(savedLeft, savedTop, savedWidth, savedHeight))
                {
                    Width  = savedWidth;
                    Height = savedHeight;
                    Left   = savedLeft;
                    Top    = savedTop;
                    return;
                }
            }

            // 首次启动或保存的坐标已在屏幕外（如外接显示器断开），恢复默认右上角
            ResetToDefaultPosition(false);
        }

        private bool IsWindowBoundsVisible(double left, double top, double width, double height)
        {
            try
            {
                var virtualRect = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);

                var winRect = new Rect(left, top, width, height);
                winRect.Intersect(virtualRect);

                // 窗口在可视区域内至少有 60x40 像素的有效面积（确保标题栏可点击）
                if (winRect.IsEmpty || winRect.Width < 60 || winRect.Height < 40)
                {
                    return false;
                }

                // 按各显示器自身 DPI 将物理屏幕边界换算为 WPF 逻辑坐标进行比对（兼容混合 DPI 多显示器）
                var probeDip = new WinPoint(left + 30, top + 20);
                var centerDip = new WinPoint(left + width / 2, top + height / 2);

                foreach (var scr in System.Windows.Forms.Screen.AllScreens)
                {
                    double scale = NativeMethods.GetScaleForPoint(scr.Bounds.Left + 1, scr.Bounds.Top + 1);
                    var logicalBounds = new Rect(
                        scr.Bounds.Left / scale,
                        scr.Bounds.Top / scale,
                        scr.Bounds.Width / scale,
                        scr.Bounds.Height / scale);

                    if (logicalBounds.Contains(probeDip) || logicalBounds.Contains(centerDip))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public void ResetToDefaultPosition(bool save = true)
        {
            var area = SystemParameters.WorkArea;
            Width  = 280;
            Height = Width * 1.3;
            Left   = area.Right - Width - 20;
            Top    = area.Top + 20;

            if (save)
            {
                SaveWindowBounds();
            }
        }

        public void SaveWindowBounds()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsNaN(Width) || double.IsNaN(Height) ||
                double.IsInfinity(Left) || double.IsInfinity(Top) || double.IsInfinity(Width) || double.IsInfinity(Height))
            {
                return;
            }

            var settings = _settingsManager.Settings;
            settings.TodoWindowLeft   = Left;
            settings.TodoWindowTop    = Top;
            settings.TodoWindowWidth  = Math.Max(MinWidth, Width);
            settings.TodoWindowHeight = Math.Max(MinHeight, Height);
            // 纯窗口几何边界保存静默落盘，不触发全应用主题重绘
            _settingsManager.SaveSettings(settings, notify: false);
        }

        /// <summary>
        /// 确保待办与回收站数据立即刷盘并释放存储资源
        /// </summary>
        public void FlushAndDispose()
        {
            _todoService.Dispose();
            _recycleBinService.Dispose();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowBounds();
            _todoService.Flush();
            if (App.IsExiting)
            {
                base.OnClosing(e);
                return;
            }
            e.Cancel = true;
            SafeHide();
        }
    }
}
