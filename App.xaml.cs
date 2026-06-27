using System.Windows;
using PinToDesk.Helpers;
using WinApp = System.Windows.Application;

namespace PinToDesk
{
    public partial class App : WinApp
    {
        private TrayHelper? _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var win = new MainWindow();
            win.Show();

            // 传入置顶和穿透回调，建立 TrayHelper 后反向注入给 MainWindow
            _tray = new TrayHelper(
                win,
                this,
                win.TogglePinFromTray,          // 托盘点击时调用
                () => win.IsPinned,             // 托盘读取当前状态
                win.TogglePassThroughFromTray,  // 托盘点击穿透时调用
                () => win.IsPassThrough         // 托盘读取当前穿透状态
            );
            win.SetTray(_tray);                 // MainWindow 持有 tray 引用，可主动同步菜单
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
