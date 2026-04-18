using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace HiddenTranslatorGHM
{
    public partial class MainWindow : Window
    {
        private readonly HotkeyService _service;
        private readonly List<string> _pressedOrder = [];
        private List<string> _selectedHotkey = [];
        public LmStudioClient client;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            _service = new HotkeyService(Dispatcher);
            HotkeysList.ItemsSource = _service.Hotkeys;

            _service.overlayWordWindow = new OverlayWordWindow(this);
            _service.overlayTransterWindow = new OverlayTransferWindow(this);

            WindowHelper.MoveToScreen(_service.overlayWordWindow, 0);
            WindowHelper.MoveToScreen(_service.overlayTransterWindow, 0);

            client = new LmStudioClient();

            //_service.overlayWordWindow.Show(); // по желанию
            _service.overlayTransterWindow.Show();
        }

        public List<string> AvailableActions => _service.AvailableActions;

        #region Capture input
        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            Key actual = (e.Key == Key.System) ? e.SystemKey : e.Key;
            string label = NormalizeKeyToLabel(actual);

            if (!_pressedOrder.Contains(label))
                _pressedOrder.Add(label);

            HotkeyBox.Text = string.Join("+", _pressedOrder);
            _selectedHotkey = _pressedOrder.ToList();
        }

        private void HotkeyBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            e.Handled = true;
        }

        private static string NormalizeKeyToLabel(Key key)
        {
            string s = key.ToString();
            if (s.Contains("Ctrl")) return "Ctrl";
            if (s.Contains("Shift")) return "Shift";
            if (s.Contains("Alt") || s == "System" || s == "Menu") return "Alt";
            if (s.Contains("Win")) return "Win";
            return s;
        }
        #endregion

        private void SaveHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHotkey == null || _selectedHotkey.Count == 0)
            {
                MessageBox.Show("Сначала выберите комбинацию в поле.");
                return;
            }

            _service.AddHotkey(_selectedHotkey.ToList(), "— Не выбрано —", false);

            _pressedOrder.Clear();
            _selectedHotkey.Clear();
            HotkeyBox.Text = "";
        }

        private void HotkeyPropertyChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                _service?.SaveHotkeys();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при автосохранении: " + ex.Message);
            }
        }

        private void ClearField_Click(object sender, RoutedEventArgs e)
        {
            _pressedOrder.Clear();
            _selectedHotkey = [];
            HotkeyBox.Text = "";
        }

        private void DeleteHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Guid id)
                _service.RemoveHotkey(id);
        }

        private void ClearHotkeys_Click(object sender, RoutedEventArgs e)
        {
            _service.DeleteAllHotKey();
        }
        private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
        {
            _service.ResetHotKey();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _service.Dispose();
        }
    }
}
