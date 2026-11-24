using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfTextBox = System.Windows.Controls.TextBox;
using Application = System.Windows.Application;

namespace PinToDesk.Common
{
    /// <summary>
    /// PinToDesk 偏好设置中心窗口
    /// </summary>
    public partial class PreferencesWindow : Window
    {
        private readonly SettingsManager _settingsManager;

        // 当前正在捕获按键的输入框及进入捕获前的原始文本
        private WpfTextBox? _capturingBox = null;
        private string _capturingOriginalText = string.Empty;
        private bool _isInitializing = false;
        private bool _isSaved = false;

        // 用于通知 App 重新加载快捷键与配置的回调
        public event Action? HotkeysChanged;

        // 请求重置待办窗口位置与大小的回调
        public event Action? ResetTodoBoundsRequested;

        public PreferencesWindow(SettingsManager settingsManager)
        {
            InitializeComponent();
            _settingsManager = settingsManager;

            if (Application.Current is App app && app.KeyboardHookService != null)
            {
                app.KeyboardHookService.KeyCaptured += OnHookKeyCaptured;
            }

            LoadFromSettings();
        }

        private void OnHookKeyCaptured(string formattedHotkey)
        {
            if (_capturingBox == null) return;
            _capturingBox.Text = formattedHotkey;
            _capturingBox.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["ThemeTextPrimaryBrush"];
            _capturingBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        // ══════════════════════════════════════════════
        // 从配置加载到 UI
        // ══════════════════════════════════════════════
        private void LoadFromSettings()
        {
            _isInitializing = true;
            try
            {
                var s = _settingsManager.Settings;

                // 1. TodoList
                HkTodoTop.Text = s.HotkeyTodoTopToggle;
                ChkTodoExpandedDisplay.IsChecked = s.TodoExpandedDisplay;
                ChkTodoPassThrough.IsChecked = s.TodoIsPassThrough;
                ChkTrayDoubleClick.IsChecked = s.TrayDoubleClickTodoEnabled;
                SelectComboByTag(CmbRetentionDays, s.RecycleBinRetentionDays, 1); // 默认 60 天

                // 2. General
                SelectComboByStringTag(CmbThemeMode, s.ThemeMode ?? "System", 0); // 默认跟随系统
                SelectComboByTag(CmbOpacity, s.WindowOpacity, 5); // 默认 100%
                ChkAutoStart.IsChecked = AutoStartHelper.IsAutoStartEnabled();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private static void SelectComboByTag(System.Windows.Controls.ComboBox combo, int targetVal, int defaultIndex)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item &&
                    int.TryParse(item.Tag?.ToString(), out int val) && val == targetVal)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > defaultIndex) combo.SelectedIndex = defaultIndex;
        }

        private static void SelectComboByStringTag(System.Windows.Controls.ComboBox combo, string targetTag, int defaultIndex)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), targetTag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > defaultIndex) combo.SelectedIndex = defaultIndex;
        }

        private static void SetHookCapturing(bool capturing)
        {
            if (Application.Current is App app && app.KeyboardHookService != null)
            {
                app.KeyboardHookService.IsCapturing = capturing;
            }
        }

        // ══════════════════════════════════════════════
        // 主题模式与不透明度即时预览响应
        // ══════════════════════════════════════════════
        private void CmbThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyLivePreview();
        }

        private void CmbOpacity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyLivePreview();
        }

        private void ApplyLivePreview()
        {
            string mode = (CmbThemeMode?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "System";
            int opacity = 100;
            if (CmbOpacity?.SelectedItem is ComboBoxItem opItem && int.TryParse(opItem.Tag?.ToString(), out int val))
            {
                opacity = val;
            }
            ThemeManager.ApplyTheme(mode, opacity);
        }

        // ══════════════════════════════════════════════
        // 快捷键输入框：获得焦点 -> 进入捕获状态
        // ══════════════════════════════════════════════
        private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is WpfTextBox tb)
            {
                _capturingBox = tb;
                _capturingOriginalText = tb.Text;
                SetHookCapturing(true);
                tb.Text = "请按下快捷键...";
                tb.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is WpfTextBox tb && _capturingBox == tb)
            {
                if (tb.Text == "请按下快捷键...")
                {
                    tb.Text = _capturingOriginalText;
                }
                tb.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["ThemeTextPrimaryBrush"];
                _capturingBox = null;
                SetHookCapturing(false);
            }
        }

        // ══════════════════════════════════════════════
        // 快捷键输入框：物理按键捕获（兜底通道，主通道由低级键盘钩子直通拦截）
        // ══════════════════════════════════════════════
        private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_capturingBox == null) return;
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt  || key == Key.RightAlt  ||
                key == Key.LeftShift|| key == Key.RightShift||
                key == Key.LWin     || key == Key.RWin)
            {
                return;
            }

            if (key == Key.Escape)
            {
                _capturingBox.Text = _capturingOriginalText;
                _capturingBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            }

            int vk = KeyInterop.VirtualKeyFromKey(key);
            bool win   = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;
            bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt   = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            string? formatted = HotkeyDescriptor.FormatFromVk(vk, win, ctrl, alt, shift);
            if (string.IsNullOrEmpty(formatted)) return;

            _capturingBox.Text = formatted;
            _capturingBox.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["ThemeTextPrimaryBrush"];
            _capturingBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        // ══════════════════════════════════════════════
        // 全局 Esc：关闭偏好设置窗口
        // ══════════════════════════════════════════════
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_capturingBox != null) return;

            if (e.Key == Key.Escape)
            {
                CancelAndClose();
                e.Handled = true;
            }
        }

        // ══════════════════════════════════════════════
        // 保存全部配置
        // ══════════════════════════════════════════════
        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string todoHotkey = HkTodoTop.Text.Trim();

            var todoDesc = HotkeyDescriptor.Parse(todoHotkey);
            if (todoDesc == null || !todoDesc.HasValidModifierOrFunctionKey)
            {
                System.Windows.MessageBox.Show(this, "待办清单快捷键无效：请使用修饰键（Ctrl/Alt/Shift/Win）组合或功能键（F1-F12）。", "快捷键无效",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = _settingsManager.Settings;

            // 1. TodoList（严格遵循“穿透必置顶”联动规范）
            settings.HotkeyTodoTopToggle        = todoHotkey;
            settings.TodoExpandedDisplay        = ChkTodoExpandedDisplay.IsChecked ?? false;
            settings.TodoIsPassThrough          = ChkTodoPassThrough.IsChecked ?? false;
            if (settings.TodoIsPassThrough)
            {
                settings.TodoIsPinned = true;
            }
            settings.TrayDoubleClickTodoEnabled = ChkTrayDoubleClick.IsChecked ?? true;

            if (CmbRetentionDays.SelectedItem is ComboBoxItem retItem &&
                int.TryParse(retItem.Tag?.ToString(), out int retDays))
            {
                settings.RecycleBinRetentionDays = retDays;
            }

            // 2. General
            if (CmbThemeMode.SelectedItem is ComboBoxItem themeItem)
            {
                settings.ThemeMode = themeItem.Tag?.ToString() ?? "System";
            }

            if (CmbOpacity.SelectedItem is ComboBoxItem opItem &&
                int.TryParse(opItem.Tag?.ToString(), out int opVal))
            {
                settings.WindowOpacity = opVal;
            }

            AutoStartHelper.SetAutoStart(ChkAutoStart.IsChecked ?? false);

            _settingsManager.SaveSettings(settings);

            _isSaved = true;
            HotkeysChanged?.Invoke();
            Close();
        }

        // ══════════════════════════════════════════════
        // 恢复默认配置
        // ══════════════════════════════════════════════
        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("确定恢复所有偏好设置为默认值吗？", "提示",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var settings = _settingsManager.Settings;
            settings.HotkeyTodoTopToggle        = "Ctrl+D";
            settings.TodoIsPinned               = false;
            settings.TodoIsPassThrough          = false;
            settings.TodoExpandedDisplay        = false;
            settings.TrayDoubleClickTodoEnabled = true;
            settings.RecycleBinRetentionDays    = 60;
            settings.ThemeMode                  = "System";
            settings.WindowOpacity              = 100;
            settings.TodoWindowLeft             = null;
            settings.TodoWindowTop              = null;
            settings.TodoWindowWidth            = null;
            settings.TodoWindowHeight           = null;

            _settingsManager.SaveSettings(settings);
            AutoStartHelper.SetAutoStart(false);

            LoadFromSettings();
            HotkeysChanged?.Invoke();
            ResetTodoBoundsRequested?.Invoke();
        }

        private void BtnResetTodoBounds_Click(object sender, RoutedEventArgs e)
        {
            ResetTodoBoundsRequested?.Invoke();
            if (BtnResetTodoBounds != null)
            {
                var originalText = BtnResetTodoBounds.Content;
                BtnResetTodoBounds.Content = "已重置";
                BtnResetTodoBounds.IsEnabled = false;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    BtnResetTodoBounds.Content = originalText;
                    BtnResetTodoBounds.IsEnabled = true;
                };
                timer.Start();
            }
        }

        private void CancelAndClose()
        {
            Close();
        }

        private void TitleBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => CloseBtn.Opacity = 1;
        private void TitleBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => CloseBtn.Opacity = 0;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) NativeMethods.DragMoveWithBounds(this);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => CancelAndClose();

        protected override void OnClosed(EventArgs e)
        {
            if (Application.Current is App app && app.KeyboardHookService != null)
            {
                app.KeyboardHookService.KeyCaptured -= OnHookKeyCaptured;
            }
            if (_capturingBox != null)
            {
                _capturingBox = null;
                SetHookCapturing(false);
            }
            if (!_isSaved)
            {
                // 恢复已保存的配置主题（取消未保存的临时预览）
                ThemeManager.ApplyTheme();
            }
            base.OnClosed(e);
        }
    }
}
