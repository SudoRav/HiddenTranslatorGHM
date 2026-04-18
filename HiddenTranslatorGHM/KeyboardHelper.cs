using System;
using System.Runtime.InteropServices;
using System.Text;

public static class KeyboardHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    public static string VkCodeToUnicode(uint vkCode, uint scanCode)
    {
        byte[] keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState))
            return "";

        StringBuilder sb = new StringBuilder(10);
        IntPtr hkl = GetKeyboardLayout(0);

        int result = ToUnicodeEx(vkCode, scanCode, keyboardState, sb, sb.Capacity, 0, hkl);
        if (result > 0)
            return sb.ToString();

        return "";
    }
}
