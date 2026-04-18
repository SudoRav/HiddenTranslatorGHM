using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;

public class GlobalKeyboardHook : IDisposable
{
    #region WinAPI
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
    #endregion

    // Событие: vkCode, scanCode, isKeyDown
    public event Action<int, int, bool> RawKeyEvent;

    private readonly object _sync = new();
    private readonly HashSet<string> _pressedLabels = new(StringComparer.OrdinalIgnoreCase);

    private class HotkeyInfo
    {
        public Guid Id;
        public HashSet<string> Keys;
        public Func<bool> Callback;
        public bool IsTriggered;
    }

    private readonly Dictionary<Guid, HotkeyInfo> _hotkeys = new();

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    public Guid RegisterHotkey(IEnumerable<string> keys, Func<bool> callback)
    {
        var set = new HashSet<string>(keys.Select(NormalizeLabel), StringComparer.OrdinalIgnoreCase);
        var id = Guid.NewGuid();
        var info = new HotkeyInfo { Id = id, Keys = set, Callback = callback, IsTriggered = false };
        lock (_sync) { _hotkeys[id] = info; }
        return id;
    }

    public bool RemoveHotkey(Guid id)
    {
        lock (_sync) { return _hotkeys.Remove(id); }
    }

    public void ClearHotkeys()
    {
        lock (_sync) { _hotkeys.Clear(); }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)data.vkCode;
            int scan = (int)data.scanCode;
            bool isDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);

            // Сначала уведомляем подписчиков "сырым" событием (включая и keyup)
            try
            {
                RawKeyEvent?.Invoke(vk, scan, isDown);
            }
            catch
            {
                // подписчики должны быть быстрыми; защищаем хук от исключений
            }

            // Дальше — обычная логика зарегистрированных хоткеев
            string label = VkToLabel(vk);

            if (isDown)
            {
                bool added;
                lock (_sync)
                {
                    added = _pressedLabels.Add(label);
                    if (added)
                    {
                        foreach (var kv in _hotkeys.Values)
                        {
                            if (!kv.IsTriggered && kv.Keys.IsSubsetOf(_pressedLabels))
                            {
                                bool passThrough = true;
                                try { passThrough = kv.Callback?.Invoke() ?? true; }
                                catch { passThrough = true; }

                                kv.IsTriggered = true;

                                if (!passThrough)
                                    return (IntPtr)1; // блокируем дальше
                            }
                        }
                    }
                }
            }
            else // key up
            {
                lock (_sync)
                {
                    _pressedLabels.Remove(label);

                    foreach (var kv in _hotkeys.Values.Where(h => h.IsTriggered).ToArray())
                    {
                        if (!kv.Keys.IsSubsetOf(_pressedLabels))
                            kv.IsTriggered = false;
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static string VkToLabel(int vk)
    {
        // Модификаторы в нормализованном виде
        if (vk == 0xA2 || vk == 0xA3 || vk == 0x11) return "Ctrl";
        if (vk == 0xA0 || vk == 0xA1 || vk == 0x10) return "Shift";
        if (vk == 0xA4 || vk == 0xA5 || vk == 0x12) return "Alt";
        if (vk == 0x5B || vk == 0x5C) return "Win";

        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key.ToString();
    }

    private static string NormalizeLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
        string r = raw.Trim();

        if (r.IndexOf("ctrl", StringComparison.OrdinalIgnoreCase) >= 0) return "Ctrl";
        if (r.IndexOf("shift", StringComparison.OrdinalIgnoreCase) >= 0) return "Shift";
        if (r.IndexOf("alt", StringComparison.OrdinalIgnoreCase) >= 0
            || r.Equals("menu", StringComparison.OrdinalIgnoreCase)
            || r.Equals("system", StringComparison.OrdinalIgnoreCase)) return "Alt";
        if (r.IndexOf("win", StringComparison.OrdinalIgnoreCase) >= 0) return "Win";

        return r;
    }

    public void Dispose()
    {
        try { UnhookWindowsHookEx(_hookId); } catch { }
    }
}
