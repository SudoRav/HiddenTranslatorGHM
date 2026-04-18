using HiddenTranslatorGHM;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace HiddenTranslatorGHM
{
    public partial class OverlayTransferWindow : Window
    {
        private MainWindow mainWindow;
        private CancellationTokenSource _cts;
        public OverlayTransferWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            this.mainWindow = mainWindow;

            //translator = new Translator();
            //translator.LoadSession_EnRu();
        }

        string surcetext = null;
        string comparabletext = null;

        public async Task TemporaryDisplayTransferText(string message = "")
        {
            // Если не в UI-потоке — перескакиваем в него и продолжаем выполнение
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => TemporaryDisplayTransferText(message));
                return;
            }

            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                if (comparabletext != message)
                {
                    string translated = await mainWindow.client.TranslateAsync(message, "Translate into Russian");
                    TB_OverlayTemporaryText.Text = translated;
                    surcetext = translated;
                    comparabletext = message;
                }
                else
                {
                    TB_OverlayTemporaryText.Text = surcetext;
                }

                BRD_Background.Opacity = 1;

                await Task.Delay(12000, token);

                var fadeOut = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromSeconds(1),
                    FillBehavior = FillBehavior.Stop
                };

                BRD_Background.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                await Task.Delay(fadeOut.Duration.TimeSpan, token);

                // Финальное состояние
                BRD_Background.Opacity = 0;
            }
            catch (TaskCanceledException)
            {
                // игнорируем отмену
            }
        }

    }
}
