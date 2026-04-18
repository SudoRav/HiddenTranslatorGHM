using HiddenTranslatorGHM;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace HiddenTranslatorGHM
{
    public partial class OverlayWordWindow : Window
    {
        private MainWindow mainWindow;

        public OverlayWordWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            this.mainWindow = mainWindow;
        }

        private CancellationTokenSource _cts;
        public async Task OnUpdateMessageAsync(string message)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                //TB_OverlayText.Text = "...";
                await Task.Delay(800, token);
                TB_OverlayText.Text = await mainWindow.client.TranslateAsync(message, "Translate into English");
            }
            catch (TaskCanceledException) { }
        }

        public void AddStrToStrOverlay(string s)
        {
            TBx_OverlayWord.Text += s;
        }

        public void OpenWindow()
        {
            this.Show();
            //TBx_OverlayWord.Focus();
        }

        public void ClearText()
        {
            TBx_OverlayWord.Text = "";
            TB_OverlayText.Text = "";
        }

        // Win32 API флаги
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        //[DllImport("user32.dll")]
        //private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        //[DllImport("user32.dll")]
        //private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // x86 варианты
        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        // x64 варианты
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        // Универсальные обёртки
        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, unchecked((int)dwNewLong.ToInt64())));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            var extendedStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var newStyle = new IntPtr(extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);

            SetWindowLongPtr(hwnd, GWL_EXSTYLE, newStyle);

            BRD_Background.Opacity = 0;
        }

        // Поле класса
        private CancellationTokenSource _debounceCts;

        private async void TBx_OverlayWord_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (IsPlainEnterPressed())
                {
                    await ProcessTranslationAsync();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(TBx_OverlayWord.Text))
                {
                    // Отменяем предыдущий токен, если пользователь печатает быстро
                    _debounceCts?.Cancel();
                    _debounceCts = new CancellationTokenSource();
                    var token = _debounceCts.Token;

                    try
                    {
                        TB_OverlayText.Text = "...";
                        await Task.Delay(400, token);
                        await OnUpdateMessageAsync(TBx_OverlayWord.Text);
                    }
                    catch (TaskCanceledException) { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private bool IsPlainEnterPressed()
        {
            return Keyboard.IsKeyDown(Key.Enter)
                   && !Keyboard.IsKeyDown(Key.LeftShift)
                   && !Keyboard.IsKeyDown(Key.LeftAlt)
                   && !Keyboard.IsKeyDown(Key.LeftCtrl)
                   && !Keyboard.IsKeyDown(Key.RightShift)
                   && !Keyboard.IsKeyDown(Key.RightAlt)
                   && !Keyboard.IsKeyDown(Key.RightCtrl);
        }

        public async Task ProcessTranslationAsync()
        {
            var text = TB_OverlayText.Text;
            string result = string.IsNullOrEmpty(text) || text == "..."
                ? await mainWindow.client.TranslateAsync(TBx_OverlayWord.Text, "Translate into English")
                : text;

            Clipboard.SetText(result);
            this.Hide();
        }

        private void BRD_Background_MouseEnter(object sender, MouseEventArgs e)
        {
            //TBx_OverlayWord.Focus();
            //TBx_OverlayWord.SelectionStart = TBx_OverlayWord.Text.Length;
        }
    }
}