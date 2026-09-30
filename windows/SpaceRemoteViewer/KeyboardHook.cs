using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpaceRemoteViewer;

/// <summary>
/// Low-level keyboard hook, so keys Windows normally keeps for itself (Win, Alt+Tab,
/// Ctrl+Esc, Alt+F4 …) can be sent to the remote PC while the viewer has focus.
/// Ctrl+Alt+Del can never be caught; Windows always handles it locally.
/// </summary>
sealed class KeyboardHook : IDisposable
{
    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    const uint LLKHF_INJECTED = 0x10;

    readonly LowLevelKeyboardProc proc; // held in a field so the GC can't collect it
    IntPtr hook;

    /// <summary>(virtual key, is key down) → return true to swallow the key locally.</summary>
    public Func<int, bool, bool> Handler { get; set; }

    public KeyboardHook()
    {
        proc = Callback;
        using var module = Process.GetCurrentProcess().MainModule;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(module?.ModuleName), 0);
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handler != null)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool injected = (info.flags & LLKHF_INJECTED) != 0;
            int msg = (int)wParam;
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            try
            {
                if (!injected && Handler((int)info.vkCode, down))
                    return (IntPtr)1;
            }
            catch { }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }
}
