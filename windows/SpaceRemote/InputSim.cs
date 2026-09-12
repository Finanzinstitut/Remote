using System.Runtime.InteropServices;

namespace SpaceRemote;

/// <summary>Mouse and keyboard input via SendInput.</summary>
static class InputSim
{
    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;

    const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
               MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010,
               MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040,
               MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000, MOUSEEVENTF_ABSOLUTE = 0x8000;

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);

    static INPUT Mouse(int dx, int dy, uint data, uint flags) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } }
    };

    static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
    };

    static void Send(params INPUT[] inputs)
    {
        if (inputs.Length > 0) SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void MoveTo(float nx, float ny)
    {
        if (float.IsNaN(nx) || float.IsNaN(ny)) return;
        int x = (int)Math.Round(Math.Clamp(nx, 0f, 1f) * 65535f);
        int y = (int)Math.Round(Math.Clamp(ny, 0f, 1f) * 65535f);
        Send(Mouse(x, y, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE));
    }

    /// <summary>Trackpad-style movement: deltas arrive as a fraction of the screen size.</summary>
    public static void MoveRelative(float ndx, float ndy)
    {
        if (float.IsNaN(ndx) || float.IsNaN(ndy)) return;
        var b = Screen.PrimaryScreen.Bounds;
        int dx = (int)Math.Round(Math.Clamp(ndx, -1f, 1f) * b.Width);
        int dy = (int)Math.Round(Math.Clamp(ndy, -1f, 1f) * b.Height);
        if (dx == 0 && dy == 0) return;
        Send(Mouse(dx, dy, 0, MOUSEEVENTF_MOVE));
    }

    public static void Button(int button, bool down)
    {
        uint flags = button switch
        {
            1 => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            2 => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
        };
        Send(Mouse(0, 0, 0, flags));
    }

    public static void Scroll(int delta) => Send(Mouse(0, 0, unchecked((uint)delta), MOUSEEVENTF_WHEEL));

    public static void HScroll(int delta) => Send(Mouse(0, 0, unchecked((uint)delta), MOUSEEVENTF_HWHEEL));

    static bool IsExtended(ushort vk) =>
        (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C || vk == 0x5D || vk == 0x6F;

    static INPUT VkEvent(ushort vk, bool up)
    {
        uint flags = (up ? KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
        return Key(vk, (ushort)MapVirtualKey(vk, 0), flags);
    }

    public static void KeyPress(int mods, ushort vk)
    {
        var modKeys = new List<ushort>();
        if ((mods & 1) != 0) modKeys.Add(0xA2); // left Ctrl
        if ((mods & 2) != 0) modKeys.Add(0xA4); // left Alt
        if ((mods & 4) != 0) modKeys.Add(0xA0); // left Shift
        if ((mods & 8) != 0) modKeys.Add(0x5B); // Windows key

        var list = new List<INPUT>();
        foreach (var m in modKeys) list.Add(VkEvent(m, false));
        list.Add(VkEvent(vk, false));
        list.Add(VkEvent(vk, true));
        for (int i = modKeys.Count - 1; i >= 0; i--) list.Add(VkEvent(modKeys[i], true));
        Send(list.ToArray());
    }

    public static void Text(string text)
    {
        var list = new List<INPUT>();
        foreach (char c in text)
        {
            if (c == '\r') continue;
            if (c == '\n')
            {
                list.Add(VkEvent(0x0D, false));
                list.Add(VkEvent(0x0D, true));
                continue;
            }
            list.Add(Key(0, c, KEYEVENTF_UNICODE));
            list.Add(Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }
        Send(list.ToArray());
    }
}
