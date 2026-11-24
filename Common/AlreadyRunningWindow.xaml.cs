using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PinToDesk.Common
{
    public partial class AlreadyRunningWindow : Window
    {
        private readonly GlobalMouseClickMonitor _mouseMonitor = new();

        public AlreadyRunningWindow()
        {
            InitializeComponent();
            _mouseMonitor.ClickedOutside += () => Dispatcher.BeginInvoke(Close);
            Loaded += (s, e) =>
            {
                Activate();
                Focus();
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    _mouseMonitor.Start(new[] { hwnd });
                }
            };
            Closed += (s, e) => _mouseMonitor.Dispose();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) NativeMethods.DragMoveWithBounds(this);
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
