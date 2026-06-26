using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PinToDesk.Helpers;
using PinToDesk.Models;
using PinToDesk.Services;
using WinPoint       = System.Windows.Point;
using WinKey         = System.Windows.Input.KeyEventArgs;
using WinMouse       = System.Windows.Input.MouseEventArgs;
using WinDrag        = System.Windows.DragEventArgs;
using WinButton      = System.Windows.Controls.Button;
using WinDropEffects = System.Windows.DragDropEffects;

namespace PinToDesk
{
    public partial class MainWindow : Window
    {
        // ══════════════════════════════════════════════
        // Win32 P/Invoke — 鼠标穿透（仅内容区）
        // 注意：不使用 WS_EX_TRANSPARENT，避免整窗口穿透
        // 只通过 WM_NCHITTEST 返回 HTTRANSPARENT 实现内容区穿透
        // TitleBar（前36px）始终保留交互
        // ══════════════════════════════════════════════
        private const int WM_NCHITTEST  = 0x0084;
        private const int HTTRANSPARENT = -1;

        // ══════════════════════════════════════════════
        // 字段
        // ══════════════════════════════════════════════
        private readonly ObservableCollection<TodoItem> _items = new();
        private readonly MarkdownStorage _storage;

        // 拖拽排序
        private WinPoint  _dragStart;
        private TodoItem? _dragItem;

        // 窗口调整大小
        private bool _isResizing;
        private WinPoint _resizeStart;
        private double   _resizeStartW, _resizeStartH;

        // 置顶穿透
        private bool _isPinned;

        // 托盘引用（用于同步置顶菜单状态）
        private TrayHelper? _tray;
        public bool IsPinned => _isPinned;
        public void SetTray(TrayHelper tray) => _tray = tray;

        // Win32 结构与接口定义
        private const int WM_MOVING = 0x0216;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        // ══════════════════════════════════════════════
        // 构造函数
        // ══════════════════════════════════════════════
        public MainWindow()
        {
            InitializeComponent();

            _storage = new MarkdownStorage();
            foreach (var item in _storage.LoadTodos())
                _items.Add(item);
            TodoList.ItemsSource = _items;

            // 监听集合变化，同步空列表占位符
            _items.CollectionChanged += (s, e) => UpdateEmptyPlaceholder();

            // 初始位置：右上角
            var area = SystemParameters.WorkArea;
            Left   = area.Right - Width - 20;
            Height = Width * 1.3;
            Top    = area.Top + 20;

            // 初始化占位符状态
            Loaded += (s, e) => UpdateEmptyPlaceholder();
        }

        // ══════════════════════════════════════════════
        // 窗口初始化：挂钩 WndProc（用于穿透 hit-test）
        // ══════════════════════════════════════════════
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST && _isPinned)
            {
                // 将鼠标屏幕坐标转换为相对窗口坐标
                int lp   = lParam.ToInt32();
                int sx   = (short)(lp & 0xFFFF);
                int sy   = (short)((lp >> 16) & 0xFFFF);
                var ptWin = PointFromScreen(new WinPoint(sx, sy));

                // TitleBar 区域（前 36px）保留交互
                if (ptWin.Y <= 36)
                    return IntPtr.Zero;

                // 内容区穿透
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }
            else if (msg == WM_MOVING)
            {
                // 获取当前鼠标所在的屏幕工作区（物理像素）
                POINT mousePos;
                GetCursorPos(out mousePos);
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(mousePos.X, mousePos.Y));
                var area = screen.WorkingArea;

                var rect = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT))!;
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                // 限制窗口范围在当前屏幕工作区内
                if (rect.Left < area.Left)
                {
                    rect.Left = area.Left;
                    rect.Right = rect.Left + width;
                }
                else if (rect.Right > area.Right)
                {
                    rect.Right = area.Right;
                    rect.Left = rect.Right - width;
                }

                if (rect.Top < area.Top)
                {
                    rect.Top = area.Top;
                    rect.Bottom = rect.Top + height;
                }
                else if (rect.Bottom > area.Bottom)
                {
                    rect.Bottom = area.Bottom;
                    rect.Top = rect.Bottom - height;
                }

                Marshal.StructureToPtr(rect, lParam, true);
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        // ══════════════════════════════════════════════
        // 监听尺寸变化（已取消黄金比例联动）
        // ══════════════════════════════════════════════
        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
        }

        // ══════════════════════════════════════════════
        // TitleBar 区域悬停：控制 TitleBar 按钮显示
        // ══════════════════════════════════════════════
        private void TitleBar_MouseEnter(object sender, WinMouse e) => SetTitleButtonsOpacity(1);
        private void TitleBar_MouseLeave(object sender, WinMouse e)
        {
            // 置顶状态下 TitleBar 按钮保持可见
            if (!_isPinned) SetTitleButtonsOpacity(0);
        }

        private void SetTitleButtonsOpacity(double opacity)
        {
            PinBtn.Opacity   = opacity;
            CloseBtn.Opacity = opacity;
        }

        // ResizeGrip 区域悬停：控制 Grip 显示
        private void ResizeGrip_MouseEnter(object sender, WinMouse e)
        {
            if (!_isPinned) ResizeGripArea.Opacity = 1;
        }
        private void ResizeGrip_MouseLeave(object sender, WinMouse e) => ResizeGripArea.Opacity = 0;

        // ══════════════════════════════════════════════
        // 标题栏拖动
        // ══════════════════════════════════════════════
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) DragMove();
        }

        // ══════════════════════════════════════════════
        // 标题栏按钮
        // ══════════════════════════════════════════════
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

        private void PinBtn_Click(object sender, RoutedEventArgs e)
        {
            TogglePinState();
            _tray?.SyncPinMenuItem();
        }

        // 由托盘菜单触发（避免循环调用 SyncPinMenuItem）
        internal void TogglePinFromTray()
        {
            TogglePinState();
        }

        private void TogglePinState()
        {
            _isPinned    = !_isPinned;
            this.Topmost = true;

            if (_isPinned)
            {
                PinBtn.Content      = "📍";
                PinBtn.ToolTip      = "已置顶，点击取消";
                // 置顶时 TitleBar 按钮固定可见
                SetTitleButtonsOpacity(1);
                ResizeGripArea.Opacity = 0;
            }
            else
            {
                PinBtn.Content = "📌";
                PinBtn.ToolTip = "置顶 / 取消置顶";
                SetTitleButtonsOpacity(0);
            }
        }

        // ══════════════════════════════════════════════
        // 右下角自定义 ResizeGrip
        // ══════════════════════════════════════════════
        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isResizing   = true;
            _resizeStart  = e.GetPosition(null);
            _resizeStartW = Width;
            _resizeStartH = Height;
            ((UIElement)sender).CaptureMouse();
            ((UIElement)sender).MouseMove        += ResizeGrip_MouseMove;
            ((UIElement)sender).MouseLeftButtonUp += ResizeGrip_MouseLeftButtonUp;
            e.Handled = true;
        }

        private void ResizeGrip_MouseMove(object sender, WinMouse e)
        {
            if (!_isResizing) return;
            var pos   = e.GetPosition(null);
            var delta = pos - _resizeStart;
            
            double newW = Math.Max(MinWidth, _resizeStartW + delta.X);
            double newH = Math.Max(MinHeight, _resizeStartH + delta.Y);

            // 限制最大缩放范围，防止超出当前屏幕的工作区
            var area = GetCurrentScreenWorkArea();
            if (Left + newW > area.Right)
            {
                newW = area.Right - Left;
            }
            if (Top + newH > area.Bottom)
            {
                newH = area.Bottom - Top;
            }

            Width  = newW;
            Height = newH;
        }

        // 获取当前窗口所在的屏幕工作区，并转换为 WPF 逻辑像素 (DIPs)
        private Rect GetCurrentScreenWorkArea()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var area = screen.WorkingArea;

            // 获取当前 DPI 比例以实现高分屏自适应
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double dpiScaleX = dpi.DpiScaleX;
            double dpiScaleY = dpi.DpiScaleY;

            return new Rect(
                area.Left / dpiScaleX,
                area.Top / dpiScaleY,
                area.Width / dpiScaleX,
                area.Height / dpiScaleY
            );
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isResizing = false;
            ((UIElement)sender).ReleaseMouseCapture();
            ((UIElement)sender).MouseMove        -= ResizeGrip_MouseMove;
            ((UIElement)sender).MouseLeftButtonUp -= ResizeGrip_MouseLeftButtonUp;
        }

        // ══════════════════════════════════════════════
        // 双击空白处：显示内联输入框
        // ══════════════════════════════════════════════
        private void TodoList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 如果点击源是按钮或按钮的子元素（防止连续删除误触双击）
            var src = e.OriginalSource as DependencyObject;
            while (src != null)
            {
                if (src is System.Windows.Controls.Button) return;
                src = System.Windows.Media.VisualTreeHelper.GetParent(src);
            }

            // 双击在条目文本上 → 编辑该条目
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is TodoItem item)
            {
                ShowEditDialog(item);
                return;
            }

            // 双击空白处 → 显示内联输入框
            ShowInlineInput();
        }

        private void ShowInlineInput()
        {
            InlineInputArea.Visibility = Visibility.Visible;
            InlineEditBox.Text         = string.Empty;
            InlineEditBox.Focus();
        }

        private void HideInlineInput()
        {
            InlineInputArea.Visibility = Visibility.Collapsed;
            InlineEditBox.Text         = string.Empty;
        }

        private void InlineEditBox_KeyDown(object sender, WinKey e)
        {
            if (e.Key == Key.Enter)
            {
                CommitInlineInput();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HideInlineInput();
                e.Handled = true;
            }
        }

        private void InlineEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 失焦时自动取消（若未回车确认）
            HideInlineInput();
        }

        private void CommitInlineInput()
        {
            var text = InlineEditBox.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                // 已移除字数限制，支持任意长度内容
                _items.Add(new TodoItem { Title = text });
                _storage.SaveTodos(_items);
                if (_items.Count > 0)
                    TodoList.ScrollIntoView(_items[^1]);
            }
            HideInlineInput();
        }

        private void UpdateEmptyPlaceholder()
        {
            EmptyPlaceholder.Visibility =
                _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══════════════════════════════════════════════
        // 编辑 / 删除
        // ══════════════════════════════════════════════
        private void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            var id   = (Guid)((WinButton)sender).Tag;
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null) ShowEditDialog(item);
        }

        private void ShowEditDialog(TodoItem item)
        {
            var dlg = new EditDialog(item.Title) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
            {
                item.Title = dlg.ResultText;
                var idx = _items.IndexOf(item);
                _items.RemoveAt(idx);
                _items.Insert(idx, item);
                _storage.SaveTodos(_items);
            }
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var id   = (Guid)((WinButton)sender).Tag;
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null) { _items.Remove(item); _storage.SaveTodos(_items); }
        }

        // ══════════════════════════════════════════════
        // 拖拽排序
        // ══════════════════════════════════════════════
        private void TodoList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragItem  = (e.OriginalSource as FrameworkElement)?.DataContext as TodoItem;
        }

        private void TodoList_PreviewMouseMove(object sender, WinMouse e)
        {
            if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed) return;
            var pos   = e.GetPosition(null);
            var delta = pos - _dragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            DragDrop.DoDragDrop(TodoList, _dragItem, WinDropEffects.Move);
            _dragItem = null;
        }

        private void TodoList_Drop(object sender, WinDrag e)
        {
            if (_dragItem == null) return;
            var target = (e.OriginalSource as FrameworkElement)?.DataContext as TodoItem;
            if (target == null || target == _dragItem) return;
            var oldIdx = _items.IndexOf(_dragItem);
            var newIdx = _items.IndexOf(target);
            if (oldIdx >= 0 && newIdx >= 0) { _items.Move(oldIdx, newIdx); _storage.SaveTodos(_items); }
        }
    }
}
