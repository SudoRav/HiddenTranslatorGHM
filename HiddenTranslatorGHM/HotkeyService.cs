using HiddenTranslatorGHM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WindowsInput;
using WindowsInput.Native;

public class HotkeyService : IDisposable
{
    private readonly GlobalKeyboardHook _hook = new();
    private readonly Dispatcher _dispatcher;
    private const string SAVE_FILE = "hotkeys.json";
    private CancellationTokenSource _typingCts;

    public OverlayWordWindow overlayWordWindow;
    public OverlayTransferWindow overlayTransterWindow;

    public ObservableCollection<HotkeyItem> Hotkeys { get; } = new ObservableCollection<HotkeyItem>();

    public HotkeyService() : this(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher) { }
    public HotkeyService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        LoadSavedHotkeys();

        // подписка на "сырые" хуки
        _hook.RawKeyEvent += Hook_RawKeyEvent;
    }

    public List<string> AvailableActions { get; } = ["— Не выбрано —", "ПерключениеТекстовогоПоля", "ОтобразитьПеревод", "ПереводИзБуфера", "Сим.ВводаТекстаИзБуфера", "ОстановитьПечать", "ПереносНаДисплей1", "ПереносНаДисплей2", "ПереносНаДисплей3"];

    public void AddHotkey(List<string> keys, string action, bool passThrough = false)
    {
        if (keys == null || keys.Count == 0) return;

        var item = new HotkeyItem
        {
            Id = Guid.Empty,
            Keys = keys,
            Display = string.Join("+", keys),
            PassThrough = passThrough,
            ActionName = string.IsNullOrWhiteSpace(action) ? "— Не выбрано —" : action,
        };

        Guid id = _hook.RegisterHotkey(item.Keys, () =>
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                switch (item.ActionName)
                {
                    case "ПерключениеТекстовогоПоля": ToggleTextOverlay(); break;
                    case "ОтобразитьПеревод": DisplayTemporaryTransferText(); break;
                    case "ПереводИзБуфера": DisplayTemporaryClipdoardTransferText(); break;
                    //case "Сим.ВводаТекстаИзБуфера": SimulateTextInput(Clipboard.GetText().Trim()); break;
                    case "Сим.ВводаТекстаИзБуфера":
                        {
                            _typingCts?.Cancel();
                            _typingCts = new CancellationTokenSource();
                            _ = SimulateTextInput(
                                Clipboard.GetText().Trim(),
                                _typingCts.Token
                            );
                            break;
                        }
                    case "ОстановитьПечать": { _typingCts?.Cancel(); } break;
                    case "ПереносНаДисплей1": TransferToDispalyA(); break;
                    case "ПереносНаДисплей2": TransferToDispalyB(); break;
                    case "ПереносНаДисплей3": TransferToDispalyC(); break;
                    case "— Не выбрано —": break;
                    default: MessageBox.Show("Неизвестное действие"); break;
                }
            }));

            return item.PassThrough; // true = пропустить, false = поглотить
        });

        item.Id = id;
        Hotkeys.Add(item);
        SaveAllToFile();
    }

    void ToggleTextOverlay()
    {
        overlayWordWindow.Dispatcher.BeginInvoke(() =>
        {
            if (overlayWordWindow == null)
                return;

            if (!overlayWordWindow.IsVisible)
            {
                overlayWordWindow.ClearText();
                overlayWordWindow.Show();
                overlayWordWindow.Activate();
                EnableGlobalTyping();
                return;
            }

            if (overlayWordWindow.TBx_OverlayWord.IsFocused)
            {
                // В фокусе TextBox — глобальный ввод не нужен
                DisableGlobalTyping();
                return;
            }

            // Окно видно, но не в фокусе — включаем глобальный ввод
            EnableGlobalTyping();
        });
    }

    void DisplayTemporaryTransferText()
    {
        overlayTransterWindow.Dispatcher.BeginInvoke(async () =>
        {
            string text = Clipboard.GetText();
            await overlayTransterWindow.TemporaryDisplayTransferText(text);
        });
    }
    void DisplayTemporaryClipdoardTransferText()
    {
        overlayWordWindow.Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(400); // задержка
            string clipboardText = Clipboard.GetText();
            await overlayTransterWindow.TemporaryDisplayTransferText(clipboardText);
        });
    }

    private readonly InputSimulator sim = new();
    private static readonly Random rnd = new();

    // --- Карта соседних клавиш (QWERTY) ---
    private static readonly Dictionary<char, string> NeighborKeys = new()
    {
        ['1'] = "qw2",
        ['2'] = "1qwe3",
        ['3'] = "2wer4",
        ['4'] = "3ert5",
        ['5'] = "4rty6",
        ['6'] = "5tyu7",
        ['7'] = "6yui8",
        ['8'] = "7uio9",
        ['9'] = "8iop0",
        ['0'] = "9op[-",
        ['-'] = "0p[]=",
        ['='] = "-[]",

        ['q'] = "asw12",
        ['w'] = "qasde123",
        ['e'] = "wsdfr234",
        ['r'] = "edfgt345",
        ['t'] = "rfghy456",
        ['y'] = "tghju567",
        ['u'] = "yhjki",
        ['i'] = "ujklo",
        ['o'] = "ikl;p",
        ['p'] = "ol;'[",

        ['a'] = "qwsxz",
        ['s'] = "aqwedcxz",
        ['d'] = "swerfvcx",
        ['f'] = "dertgbvc",
        ['g'] = "frtyhnbv",
        ['h'] = "gtyujmnb",
        ['j'] = "hyuik,mn",
        ['k'] = "juiol.,m",
        ['l'] = "kiop;/.,",

        ['z'] = "asx",
        ['x'] = "zasdc",
        ['c'] = "xsdfv",
        ['v'] = "cdfgb",
        ['b'] = "vfghn",
        ['n'] = "bghjm",
        ['m'] = "nhjk,"
    };

    // Таблица символов, требующих Shift (US-раскладка)
    private static readonly Dictionary<char, (VirtualKeyCode key, bool shift)> KeyMap =
        new()
        {
            ['!'] = (VirtualKeyCode.VK_1, true),
            ['@'] = (VirtualKeyCode.VK_2, true),
            ['#'] = (VirtualKeyCode.VK_3, true),
            ['$'] = (VirtualKeyCode.VK_4, true),
            ['%'] = (VirtualKeyCode.VK_5, true),
            ['^'] = (VirtualKeyCode.VK_6, true),
            ['&'] = (VirtualKeyCode.VK_7, true),
            ['*'] = (VirtualKeyCode.VK_8, true),
            ['('] = (VirtualKeyCode.VK_9, true),
            [')'] = (VirtualKeyCode.VK_0, true),
            ['_'] = (VirtualKeyCode.OEM_MINUS, true),
            ['+'] = (VirtualKeyCode.OEM_PLUS, true),

            ['{'] = (VirtualKeyCode.OEM_4, true),
            ['}'] = (VirtualKeyCode.OEM_6, true),
            ['|'] = (VirtualKeyCode.OEM_5, true),

            [':'] = (VirtualKeyCode.OEM_1, true),
            ['"'] = (VirtualKeyCode.OEM_7, true),
            ['<'] = (VirtualKeyCode.OEM_COMMA, true),
            ['>'] = (VirtualKeyCode.OEM_PERIOD, true),
            ['?'] = (VirtualKeyCode.OEM_2, true)
        };

    public async Task SimulateTextInput(string text, CancellationToken token)
    {
        await Task.Delay(rnd.Next(150, 300));

        int length = text.Length;

        int baseDelay = length switch
        {
            < 30 => rnd.Next(110, 160),
            < 100 => rnd.Next(80, 130),
            _ => rnd.Next(60, 110)
        };

        int typedCount = 0;

        foreach (char c in text)
        {
            token.ThrowIfCancellationRequested();

            // Ошибка
            if (ShouldMakeMistake(c))
                await TypeMistake(c);

            // "Задумался"
            if (rnd.NextDouble() < 0.01)
                await Task.Delay(rnd.Next(600, 1200));

            // Печать символа с учётом Shift
            TypeCharacter(c);
            typedCount++;

            int delay = CalculateDelay(c, baseDelay, typedCount);
            await Task.Delay(delay);
        }
    }

    public async Task StopSimulateTextInput()
    {

    }

    // ---- ПЕЧАТЬ СИМВОЛА С SHIFT ИЛИ БЕЗ ----
    //private void TypeCharacter(char c)
    //{
    //    // Заглавные буквы → Shift + буква
    //    if (char.IsLetter(c) && char.IsUpper(c))
    //    {
    //        var key = (VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), "VK_" + char.ToUpper(c));
    //        sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.SHIFT, key);
    //        return;
    //    }

    //    // Спецсимволы requiring Shift
    //    if (KeyMap.TryGetValue(c, out var mapped))
    //    {
    //        if (mapped.shift)
    //            sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.SHIFT, mapped.key);
    //        else
    //            sim.Keyboard.KeyPress(mapped.key);

    //        return;
    //    }

    //    // Обычные клавиши — используем TextEntry
    //    sim.Keyboard.TextEntry(c);
    //}
    //private void TypeCharacter(char c)
    //{
    //    if (char.IsLetter(c))
    //    {
    //        sim.Keyboard.TextEntry(c);
    //        return;
    //    }

    //    if (KeyMap.TryGetValue(c, out var mapped))
    //    {
    //        if (mapped.shift)
    //            sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.SHIFT, mapped.key);
    //        else
    //            sim.Keyboard.KeyPress(mapped.key);

    //        return;
    //    }

    //    sim.Keyboard.TextEntry(c);
    //}
    private void TypeCharacter(char c)
    {
        // Вводим символ как Unicode-пакет, чтобы результат не зависел от текущей раскладки
        // (RU/EN и т.д.) и точно соответствовал тексту из буфера обмена.
        if (c == '\r' || c == '\n')
        {
            sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
            return;
        }

        sim.Keyboard.TextEntry(c);
    }


    private int CalculateDelay(char c, int baseDelay, int typedCount)
    {
        double speedFactor = 1 + (rnd.NextDouble() * 0.08 - 0.04);
        speedFactor = Clamp(speedFactor, 0.7, 1.3);

        int delay = (int)(baseDelay * speedFactor);

        if (char.IsWhiteSpace(c))
            delay += rnd.Next(80, 180);

        if (".,!?:;()\"'".Contains(c))
            delay += rnd.Next(120, 260);

        delay += rnd.Next(-40, 40);

        if (typedCount % rnd.Next(10, 16) == 0)
            delay += rnd.Next(150, 350);

        return Math.Max(delay, 20);
    }

    private bool ShouldMakeMistake(char c)
    {
        return char.IsLetter(c) && rnd.NextDouble() < 0.025;
    }

    private async Task TypeMistake(char correct)
    {
        char wrong = GetNeighborKey(correct);

        TypeCharacter(wrong);
        await Task.Delay(rnd.Next(150, 250));

        sim.Keyboard.KeyPress(VirtualKeyCode.BACK);
        await Task.Delay(rnd.Next(120, 200));
    }

    private char GetNeighborKey(char c)
    {
        char lower = char.ToLower(c);
        if (NeighborKeys.TryGetValue(lower, out var neighbors))
            return neighbors[rnd.Next(neighbors.Length)];

        return c;
    }

    private double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    void TransferToDispalyA()
    {
        overlayWordWindow.Dispatcher.BeginInvoke(new Action(() =>
        {
            WindowHelper.MoveToScreen(overlayWordWindow, 0);
            WindowHelper.MoveToScreen(overlayTransterWindow, 0);
        }));
    }

    void TransferToDispalyB()
    {
        overlayWordWindow.Dispatcher.BeginInvoke(new Action(() =>
        {
            WindowHelper.MoveToScreen(overlayWordWindow, 1);
            WindowHelper.MoveToScreen(overlayTransterWindow, 1);
        }));
    }

    void TransferToDispalyC()
    {
        overlayWordWindow.Dispatcher.BeginInvoke(new Action(() =>
        {
            WindowHelper.MoveToScreen(overlayWordWindow, 2);
            WindowHelper.MoveToScreen(overlayTransterWindow, 2);
        }));
    }

    public void ResetHotKey()
    {
        Hotkeys.Clear();

        AddHotkey(["Alt", "Q"],  "ПерключениеТекстовогоПоля", false);
        AddHotkey(["Alt", "W"],  "ОтобразитьПеревод", false);
        AddHotkey(["Ctrl", "C"], "ПереводИзБуфера", true);
        AddHotkey(["Ctrl", "Alt", "E"],  "Сим.ВводаТекстаИзБуфера", false);
        AddHotkey(["Ctrl", "Alt", "F1"], "ПереносНаДисплей1", false);
        AddHotkey(["Ctrl", "Alt", "F2"], "ПереносНаДисплей2", false);
        AddHotkey(["Ctrl", "Alt", "F3"], "ПереносНаДисплей3", false);
    }

    public void RemoveHotkey(Guid id)
    {
        _hook.RemoveHotkey(id);
        var found = Hotkeys.FirstOrDefault(x => x.Id == id);
        if (found != null) Hotkeys.Remove(found);
        SaveAllToFile();
    }

    public void ClearHotkeys()
    {
        _hook.ClearHotkeys();
        Hotkeys.Clear();
        SaveAllToFile();
    }

    public void SaveHotkeys() => SaveAllToFile();

    public void DeleteAllHotKey()
    {
        ClearHotkeys();
        //MessageBox.Show("Все хоткеи удалены.");
    }

    public void Dispose()
    {
        _hook.RawKeyEvent -= Hook_RawKeyEvent;
        _hook.Dispose();
    }

    #region Save/Load
    private void SaveAllToFile()
    {
        try
        {
            var list = Hotkeys.Select(x => new SavedHotkey
            {
                Id = x.Id,
                Keys = x.Keys,
                PassThrough = x.PassThrough,
                ActionName = string.IsNullOrWhiteSpace(x.ActionName) ? "— Не выбрано —" : x.ActionName
            }).ToList();

            var opt = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SAVE_FILE, JsonSerializer.Serialize(list, opt));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка сохранения: " + ex.Message);
        }
    }

    private void LoadSavedHotkeys()
    {
        if (!File.Exists(SAVE_FILE)) return;

        try
        {
            var json = File.ReadAllText(SAVE_FILE);
            var list = JsonSerializer.Deserialize<List<SavedHotkey>>(json) ?? new List<SavedHotkey>();

            foreach (var sh in list)
            {
                var keys = sh.Keys?.Select(k => k.Trim())
                    .Where(k => !string.IsNullOrWhiteSpace(k)).ToList();

                if (keys == null || keys.Count == 0) continue;

                var action = string.IsNullOrWhiteSpace(sh.ActionName)
                    ? "— Не выбрано —"
                    : sh.ActionName;

                AddHotkey(keys, action, sh.PassThrough);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка загрузки: " + ex.Message);
        }
    }
    #endregion

    #region Raw typing + mapping to Cyrillic

    private bool _globalTypingActive = false;

    public void EnableGlobalTyping()
    {
        if (_globalTypingActive) return;
        _globalTypingActive = true;
        // мы уже подписались глобально на RawKeyEvent в конструкторе; флаг нужен, чтобы игнорировать когда не в режиме ввода
    }

    public void DisableGlobalTyping()
    {
        if (!_globalTypingActive) return;
        _globalTypingActive = false;
    }

    // P/Invoke для состояния клавиш
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_CAPITAL = 0x14;
    private const int VK_BACK = 0x08;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_DELETE = 0x2E;
    private const int VK_SPACE = 0x20;

    // Основная точка входа для сырых событий
    private void Hook_RawKeyEvent(int vk, int scan, bool isDown)
    {
        // Нас интересуют только события KeyDown и только когда глобальный режим ввода включён
        if (!isDown) return;
        if (!_globalTypingActive) return;

        // Если нет окна — выключаем режим
        if (overlayWordWindow == null) return;

        overlayWordWindow.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (!overlayWordWindow.IsVisible)
                {
                    DisableGlobalTyping();
                    return;
                }

                // если textbox сейчас в фокусе — выключаем глобальный ввод (чтобы стандартный ввод работал)
                var tb = overlayWordWindow.TBx_OverlayWord;
                if (tb.IsFocused)
                {
                    DisableGlobalTyping();
                    return;
                }

                // Обработка спец. клавиш
                if (vk == VK_ESCAPE)
                {
                    overlayWordWindow.Hide();
                    DisableGlobalTyping();
                    return;
                }

                if (vk == VK_RETURN)
                {
                    await overlayWordWindow.ProcessTranslationAsync();
                    DisableGlobalTyping();
                    return;
                }

                if (vk == VK_BACK)
                {
                    HandleBackspace(tb);
                    return;
                }

                if (vk == VK_DELETE)
                {
                    HandleDelete(tb);
                    return;
                }

                // пробел
                if (vk == VK_SPACE)
                {
                    InsertTextAtCaret(tb, " ");
                    return;
                }

                // Попытка сопоставить VK -> кириллица (учитывает Shift / CapsLock)
                bool shift = IsShiftPressed();
                bool caps = IsCapsLockOn();

                char? ruChar = MapVirtualKeyToRussianChar(vk, shift, caps);
                if (ruChar.HasValue)
                {
                    InsertTextAtCaret(tb, ruChar.Value.ToString());
                }
            }
            catch { /* подавляем ошибки внутри диспетчера */ }
        });
    }

    private static bool IsShiftPressed()
    {
        return ((GetKeyState(VK_SHIFT) & 0x8000) != 0)
               || ((GetKeyState(VK_LSHIFT) & 0x8000) != 0)
               || ((GetKeyState(VK_RSHIFT) & 0x8000) != 0);
    }

    private static bool IsCapsLockOn()
    {
        return (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
    }

    // Вставка/удаление: корректно учитываем caret и выделение
    private void InsertTextAtCaret(System.Windows.Controls.TextBox tb, string text)
    {
        int selStart = tb.SelectionStart;
        int selLen = tb.SelectionLength;
        if (selLen > 0)
        {
            tb.Text = tb.Text.Remove(selStart, selLen);
        }
        tb.Text = tb.Text.Insert(selStart, text);
        tb.SelectionStart = selStart + text.Length;
        tb.SelectionLength = 0;
    }

    private void HandleBackspace(System.Windows.Controls.TextBox tb)
    {
        int selStart = tb.SelectionStart;
        int selLen = tb.SelectionLength;
        if (selLen > 0)
        {
            tb.Text = tb.Text.Remove(selStart, selLen);
            tb.SelectionStart = selStart;
            tb.SelectionLength = 0;
            return;
        }

        if (selStart > 0)
        {
            tb.Text = tb.Text.Remove(selStart - 1, 1);
            tb.SelectionStart = Math.Max(0, selStart - 1);
            tb.SelectionLength = 0;
        }
    }

    private void HandleDelete(System.Windows.Controls.TextBox tb)
    {
        int selStart = tb.SelectionStart;
        int selLen = tb.SelectionLength;
        if (selLen > 0)
        {
            tb.Text = tb.Text.Remove(selStart, selLen);
            tb.SelectionStart = selStart;
            tb.SelectionLength = 0;
            return;
        }

        if (selStart < tb.Text.Length)
        {
            tb.Text = tb.Text.Remove(selStart, 1);
            tb.SelectionStart = selStart;
            tb.SelectionLength = 0;
        }
    }

    // Карта стандартной русской раскладки (QWERTY физические клавиши -> русские буквы)
    // Используем VK-коды (буквы 0x41..0x5A, OEM-* для знаков)
    private static readonly Dictionary<int, char> RuLower = new Dictionary<int, char>
    {
        // top row Q W E R T Y U I O P [ ]
        [0x51] = 'й', // Q
        [0x57] = 'ц', // W
        [0x45] = 'у', // E
        [0x52] = 'к', // R
        [0x54] = 'е', // T
        [0x59] = 'н', // Y
        [0x55] = 'г', // U
        [0x49] = 'ш', // I
        [0x4F] = 'щ', // O
        [0x50] = 'з', // P
        [0xDB] = 'х', // [  VK_OEM_4
        [0xDD] = 'ъ', // ]  VK_OEM_6

        // middle row A S D F G H J K L ; '
        [0x41] = 'ф', // A
        [0x53] = 'ы', // S
        [0x44] = 'в', // D
        [0x46] = 'а', // F
        [0x47] = 'п', // G
        [0x48] = 'р', // H
        [0x4A] = 'о', // J
        [0x4B] = 'л', // K
        [0x4C] = 'д', // L
        [0xBA] = 'ж', // ;  VK_OEM_1
        [0xDE] = 'э', // '  VK_OEM_7

        // bottom row Z X C V B N M , . /
        [0x5A] = 'я', // Z
        [0x58] = 'ч', // X
        [0x43] = 'с', // C
        [0x56] = 'м', // V
        [0x42] = 'и', // B
        [0x4E] = 'т', // N
        [0x4D] = 'ь', // M
        [0xBC] = 'б', // , VK_OEM_COMMA
        [0xBE] = 'ю', // . VK_OEM_PERIOD
        [0xBF] = '.', // / VK_OEM_2 -> on RU often maps to '.' (approx)
        [0xC0] = 'ё'  // ` ~ VK_OEM_3 (in many RU layouts)
    };

    // Функция сопоставления VK -> кириллица (учитывает Shift/CapsLock, для букв даёт регистр)
    private static char? MapVirtualKeyToRussianChar(int vk, bool shift, bool caps)
    {
        if (RuLower.TryGetValue(vk, out var ch))
        {
            if (char.IsLetter(ch))
            {
                // CapsLock + Shift -> invert (стандартное поведение)
                bool upper = caps ^ shift;
                return upper ? char.ToUpper(ch, CultureInfo.InvariantCulture) : ch;
            }
            else
            {
                // для небуквенных символов (б,ю,э,ж и т.п.) Shift обычно делает верхний регистр букв, так что тоже используем ToUpper
                bool upper = caps ^ shift;
                return upper ? char.ToUpper(ch, CultureInfo.InvariantCulture) : ch;
            }
        }

        // Если не нашли в карте — попытка получить обычный символ из латиницы (буквы A..Z)
        if (vk >= 0x41 && vk <= 0x5A)
        {
            // латинская буква, но нет явной ру-карты — пробуем вернуть латинскую букву (с учётом регистра)
            char basic = (char)vk; // 'A'..'Z'
            bool upper = caps ^ shift;
            return upper ? basic : char.ToLower(basic, CultureInfo.InvariantCulture);
        }

        // цифры
        if (vk >= 0x30 && vk <= 0x39)
        {
            // '0'..'9' (не обрабатываем shift-варианты)
            return (char)vk;
        }

        return null;
    }

    #endregion

    #region Models
    private class SavedHotkey
    {
        public Guid Id { get; set; }
        public List<string> Keys { get; set; } = new List<string>();
        public bool PassThrough { get; set; } = true;
        public string ActionName { get; set; } = "— Не выбрано —";
    }
    public class HotkeyItem
    {
        public Guid Id { get; set; }
        public List<string> Keys { get; set; } = new List<string>();
        public string Display { get; set; } = string.Empty;
        public bool PassThrough { get; set; } = true;
        public string ActionName { get; set; } = "— Не выбрано —";
    }
    #endregion
}
