using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AdapterToggle;

internal static class ControllerMonitor
{
    private const uint ERROR_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XINPUT_STATE state);

    // true if any XInput controller (slots 0-3) is connected
    public static bool AnyConnected()
    {
        for (uint i = 0; i < 4; i++)
        {
            if (XInputGetState(i, out _) == ERROR_SUCCESS)
                return true;
        }
        return false;
    }

    public static bool AnyProcessRunning(IEnumerable<string> names)
    {
        foreach (var raw in names)
        {
            var name = raw.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            if (name.Length == 0)
                continue;

            var procs = Process.GetProcessesByName(name);
            bool found = procs.Length > 0;
            foreach (var p in procs)
                p.Dispose();
            if (found)
                return true;
        }
        return false;
    }
}
