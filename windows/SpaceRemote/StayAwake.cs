using System.Runtime.InteropServices;

namespace SpaceRemote;

/// <summary>
/// Keeps the machine from sleeping. Useful on laptops, which can't be woken by a
/// smart plug and often ignore Wake-on-LAN once they are fully powered down.
/// </summary>
static class StayAwake
{
    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint esFlags);

    const uint ES_CONTINUOUS = 0x80000000;
    const uint ES_SYSTEM_REQUIRED = 0x00000001;
    const uint ES_DISPLAY_REQUIRED = 0x00000002;

    static Thread holder;
    static volatile bool active;

    public static bool IsActive => active;

    public static void Set(bool on)
    {
        if (on == active) return;
        active = on;

        if (!on) return;

        // The flag is per thread, so a dedicated thread has to hold it.
        holder = new Thread(() =>
        {
            while (active)
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
                Thread.Sleep(30000);
            }
            SetThreadExecutionState(ES_CONTINUOUS);
        })
        { IsBackground = true, Name = "stay-awake" };
        holder.Start();
    }
}
