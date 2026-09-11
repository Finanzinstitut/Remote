using Microsoft.Win32;

namespace SpaceRemote;

static class Autostart
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "SpaceRemote";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(ValueName, false);
        }
        catch { }
    }
}
