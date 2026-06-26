using System.Windows;
using System.Windows.Input;
using WinKey   = System.Windows.Input.KeyEventArgs;
using WinInput = System.Windows.Input;

namespace PinToDesk
{
    public partial class EditDialog : Window
    {
        public string ResultText { get; private set; } = string.Empty;

        public EditDialog(string currentText)
        {
            InitializeComponent();
            InputBox.Text = currentText;
            InputBox.SelectAll();
            InputBox.Focus();
        }

        // 标题栏拖动
        private void DlgTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) DragMove();
        }

        private void InputBox_KeyDown(object sender, WinKey e)
        {
            if (e.Key == WinInput.Key.Enter)
            {
                var text = InputBox.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    ResultText   = text;   // 已移除字数限制
                    DialogResult = true;
                }
                e.Handled = true;
            }
            else if (e.Key == WinInput.Key.Escape)
            {
                DialogResult = false;
                e.Handled    = true;
            }
        }
    }
}
