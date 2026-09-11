using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpaceRemote;

static class Power
{
    [DllImport("user32.dll")] static extern bool LockWorkStation();

    public static void Do(int action)
    {
        switch (action)
        {
            case 0: Run("shutdown", "/s /t 0"); break;   // komplett herunterfahren (kein Schnellstart)
            case 1: Run("shutdown", "/r /t 0"); break;
            case 2:
                new Thread(() =>
                {
                    Thread.Sleep(800); // Handy kurz trennen lassen
                    Application.SetSuspendState(PowerState.Suspend, false, false);
                }) { IsBackground = true }.Start();
                break;
            case 3: LockWorkStation(); break;
        }
    }

    static void Run(string file, string args)
    {
        try { Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false }); }
        catch { }
    }
}
