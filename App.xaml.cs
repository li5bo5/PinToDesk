using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PinToDesk.Common;

namespace PinToDesk
{
    public partial class App : System.Windows.Application
    {
        public static bool IsExiting { get; set; } = false;
        private static Mutex? _singleInstanceMutex;

        private SettingsManager?     _settingsManager;
        private KeyboardHookService? _keyboardHookService;
        private TrayHelper?          _trayHelper;
        private MainWindow?          _todoWindow;

        public KeyboardHookService? KeyboardHookService => _keyboardHookService;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 0. 全局未捕获异常守卫与日志记录
            SetupExceptionHandling();
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 0.1 系统级单实例互斥锁
            const string mutexName = "PinToDesk_SingleInstance_Mutex_li5bo5";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                // 加载配置以确保提示窗匹配用户的主题设置（浅色/深色）
                try
                {
                    _settingsManager = new SettingsManager();
                    ThemeManager.Initialize(_settingsManager);
                }
                catch { }

                var alreadyRunningDialog = new AlreadyRunningWindow();
                alreadyRunningDialog.ShowDialog();
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // 0.2 启动异常诊断与崩溃前操作轨迹服务
            DiagnosticLogger.Initialize();
            SessionEnding += (s, ev) =>
            {
                IsExiting = true;
                _todoWindow?.FlushAndDispose();
                DiagnosticLogger.Shutdown("SessionEnding");
            };

            // 1. 初始化统一配置管理器、全局主题引擎与开机自启路径自校准
            _settingsManager = new SettingsManager();
            ThemeManager.Initialize(_settingsManager);
            AutoStartHelper.CheckAndSyncPath();

            // 2. 实例化待办主窗口与 Fluent 系统托盘
            _todoWindow = new MainWindow(_settingsManager);
            _trayHelper = new TrayHelper(this, _settingsManager, _todoWindow);
            _todoWindow.SetTray(_trayHelper);

            // 3. 开启低级键盘钩子，注册全局快捷键并监听偏好设置热重载
            _keyboardHookService = new KeyboardHookService();
            RegisterAllHotkeys();
            _trayHelper.SubscribeHotkeysChanged(ReloadHotkeys);

            // 4. 默认展现待办主窗口
            _todoWindow.Show();
            _todoWindow.Activate();

            // 5. 在主消息泵进入空闲态后，二次确保托盘图标稳定显现
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                _trayHelper?.EnsureVisibleWithRetry();
            });
        }

        // ══════════════════════════════════════════════
        // 注册 / 热重载全局快捷键
        // ══════════════════════════════════════════════
        private void RegisterAllHotkeys()
        {
            if (_keyboardHookService == null || _settingsManager == null) return;

            _keyboardHookService.UnregisterAll();
            var settings = _settingsManager.Settings;

            // 唤出 / 隐藏待办清单（默认 Ctrl+D）
            _keyboardHookService.RegisterHotkey(settings.HotkeyTodoTopToggle, () =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _todoWindow?.ToggleVisibility();
                });
            });
        }

        private void ReloadHotkeys()
        {
            Dispatcher.BeginInvoke(RegisterAllHotkeys);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IsExiting = true;
            DiagnosticLogger.RecordStep("App", "OnExit 正常退出");

            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            ThemeManager.Dispose();
            _keyboardHookService?.Dispose();
            _trayHelper?.Dispose();
            _todoWindow?.FlushAndDispose();
            DiagnosticLogger.Shutdown("OnExit");

            base.OnExit(e);
        }

        private static void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }

        private long _uiExceptionWindowStartMs;
        private int _uiExceptionCountInWindow;

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash("DispatcherUnhandledException", e.Exception);

            // 致命内存异常不强行吞掉，避免进入不可恢复状态
            if (e.Exception is OutOfMemoryException)
            {
                e.Handled = false;
                return;
            }

            // 频次熔断：若 5 秒内连续爆发超过 10 次未处理 UI 异常，停止吞异常以防陷入无限报错死循环
            long nowMs = Environment.TickCount64;
            if (nowMs - _uiExceptionWindowStartMs > 5000)
            {
                _uiExceptionWindowStartMs = nowMs;
                _uiExceptionCountInWindow = 1;
            }
            else
            {
                _uiExceptionCountInWindow++;
            }

            e.Handled = _uiExceptionCountInWindow <= 10;
        }

        private static void LogCrash(string source, Exception? ex)
        {
            if (ex == null) return;
#if PTD_DIAG
            DiagnosticLogger.LogException(source, ex);
#else
            try
            {
                AppPaths.EnsureDirectories();
                string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().FullName}: {ex.Message}\r\n{ex.StackTrace}\r\n";
                if (ex.InnerException != null)
                {
                    content += $"InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\r\n{ex.InnerException.StackTrace}\r\n";
                }
                content += new string('-', 80) + "\r\n";
                File.AppendAllText(AppPaths.CrashLog, content);
            }
            catch
            {
                // 静默保底
            }
#endif
        }
    }
}
