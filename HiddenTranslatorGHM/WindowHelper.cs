using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace HiddenTranslatorGHM
{
    public static class WindowHelper
    {
        private const int MONITOR_DEFAULTTONEAREST = 2;

        // Для GetDpiForMonitor
        private enum MonitorDpiType
        {
            MDT_EFFECTIVE_DPI = 0,
            MDT_ANGULAR_DPI = 1,
            MDT_RAW_DPI = 2
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

        // Получаем DPI для монитора (fallback 96 при ошибке)
        private static uint GetDpiForScreen(Screen screen)
        {
            try
            {
                var pt = new POINT(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
                IntPtr hmon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (hmon != IntPtr.Zero)
                {
                    if (GetDpiForMonitor(hmon, MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
                        return dpiX;
                }
            }
            catch
            {
                // shcore.dll может не быть на очень старых ОС или вызов может упасть — тогда fallback
            }

            return 96; // стандартный DPI
        }

        // Перевод пикселей -> WPF DIP
        private static double PixelsToDip(int pixels, uint dpi) => pixels * 96.0 / dpi;

        // Основной метод
        public static void MoveToScreen(Window window, int screenIndex)
        {
            var screens = Screen.AllScreens;
            if (screens.Length == 0) return;

            if (screenIndex < 0 || screenIndex >= screens.Length) screenIndex = 0;
            var screen = screens[screenIndex];

            uint dpiX = GetDpiForScreen(screen);
            uint dpiY = dpiX; // чаще всего одинаковы; можно отдельно вызывать, если нужно

            double screenLeftDip = PixelsToDip(screen.Bounds.Left, dpiX);
            double screenTopDip = PixelsToDip(screen.Bounds.Top, dpiY);
            double screenWidthDip = PixelsToDip(screen.Bounds.Width, dpiX);
            double screenHeightDip = PixelsToDip(screen.Bounds.Height, dpiY);

            window.WindowStartupLocation = WindowStartupLocation.Manual;

            Action positionWindow = () =>
            {
                // Это корректно работает и если Width/Height явно заданы, и если нет
                double winWidth = double.IsNaN(window.Width) || window.Width == 0 ? window.ActualWidth : window.Width;
                double winHeight = double.IsNaN(window.Height) || window.Height == 0 ? window.ActualHeight : window.Height;

                // Если всё ещё 0 (например, окно ещё не измерено), попробуем DesiredSize
                if (winWidth == 0 || winHeight == 0)
                {
                    window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (window.DesiredSize.Width > 0) winWidth = window.DesiredSize.Width;
                    if (window.DesiredSize.Height > 0) winHeight = window.DesiredSize.Height;
                }

                // Последняя страховка — не ставим NaN
                if (winWidth <= 0) winWidth = 300;
                if (winHeight <= 0) winHeight = 200;

                window.Left = screenLeftDip + (screenWidthDip - winWidth) / 2.0;
                window.Top = screenTopDip + (screenHeightDip - winHeight) / 2.0;
            };

            if (window.IsLoaded)
            {
                positionWindow();
            }
            else
            {
                // Если метод вызван до Show()/Loaded — повесим обработчик, чтобы позиционировать когда окно появится
                RoutedEventHandler loaded = null;
                loaded = (s, e) =>
                {
                    window.Loaded -= loaded;
                    // Используем BeginInvoke, чтобы наверняка дождаться финальной разметки
                    window.Dispatcher.BeginInvoke(positionWindow);
                };
                window.Loaded += loaded;
            }
        }
    }
}
