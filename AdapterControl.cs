using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AdapterToggle;

internal enum AdapterState { NotFound, Enabled, Disabled, Problem }

// finds and enables/disables a PnP device via SetupAPI
internal sealed class AdapterControl
{
    private string? _instanceId;
    public string NameMatch { get; set; } = "";
    public string? DeviceName { get; private set; }

    public AdapterState GetState()
    {
        if (_instanceId is null || !Native.TryGetStatus(_instanceId, out _, out _))
            Find();

        if (_instanceId is null || !Native.TryGetStatus(_instanceId, out uint status, out uint problem))
            return AdapterState.NotFound;

        if (problem == Native.CM_PROB_DISABLED)
            return AdapterState.Disabled;
        return (status & Native.DN_STARTED) != 0 ? AdapterState.Enabled : AdapterState.Problem;
    }

    public void SetEnabled(bool enable)
    {
        if (GetState() == AdapterState.NotFound || _instanceId is null)
            throw new InvalidOperationException($"Device matching \"{NameMatch}\" not found.");

        Native.ChangeState(_instanceId, enable);

        // device state changes can take a moment to settle.
        var expected = enable ? AdapterState.Enabled : AdapterState.Disabled;
        for (int i = 0; i < 20; i++)
        {
            if (GetState() == expected)
                return;
            Thread.Sleep(250);
        }
        throw new InvalidOperationException("Windows accepted the change, but the device did not switch. A restart may be required.");
    }

    private void Find()
    {
        _instanceId = null;
        DeviceName = null;
        if (string.IsNullOrWhiteSpace(NameMatch))
            return;

        foreach (var (id, name) in Native.EnumeratePresentDevices())
        {
            if (name.Contains(NameMatch, StringComparison.OrdinalIgnoreCase))
            {
                _instanceId = id;
                DeviceName = name;
                return;
            }
        }
    }

    private static class Native
    {
        public const uint DN_STARTED = 0x00000008;
        public const uint CM_PROB_DISABLED = 22;
        private const uint DIGCF_PRESENT = 0x2;
        private const uint DIGCF_ALLCLASSES = 0x4;
        private const uint SPDRP_DEVICEDESC = 0x0;
        private const uint SPDRP_FRIENDLYNAME = 0xC;
        private const uint DIF_PROPERTYCHANGE = 0x12;
        private const uint DICS_ENABLE = 1;
        private const uint DICS_DISABLE = 2;
        private const uint DICS_FLAG_GLOBAL = 1;
        private const int ERROR_NO_MORE_ITEMS = 259;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_CLASSINSTALL_HEADER
        {
            public uint cbSize;
            public uint InstallFunction;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_PROPCHANGE_PARAMS
        {
            public SP_CLASSINSTALL_HEADER ClassInstallHeader;
            public uint StateChange;
            public uint Scope;
            public uint HwProfile;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiOpenDeviceInfoW(IntPtr deviceInfoSet, string deviceInstanceId, IntPtr hwndParent, uint openFlags, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiSetClassInstallParamsW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll")]
        private static extern uint CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

        public static bool TryGetStatus(string instanceId, out uint status, out uint problem)
        {
            status = problem = 0;
            return CM_Locate_DevNodeW(out uint devInst, instanceId, 0) == 0
                && CM_Get_DevNode_Status(out status, out problem, devInst, 0) == 0;
        }

        public static IEnumerable<(string InstanceId, string Name)> EnumeratePresentDevices()
        {
            var results = new List<(string, string)>();
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == INVALID_HANDLE_VALUE)
                throw new Win32Exception();
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    string? name = GetStringProperty(set, ref data, SPDRP_FRIENDLYNAME)
                                   ?? GetStringProperty(set, ref data, SPDRP_DEVICEDESC);
                    if (name is null)
                        continue;
                    var id = new StringBuilder(512);
                    if (SetupDiGetDeviceInstanceIdW(set, ref data, id, id.Capacity, out _))
                        results.Add((id.ToString(), name));
                }
                if (Marshal.GetLastWin32Error() is var err && err != ERROR_NO_MORE_ITEMS && err != 0)
                    throw new Win32Exception(err);
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return results;
        }

        public static void ChangeState(string instanceId, bool enable)
        {
            IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (set == INVALID_HANDLE_VALUE)
                throw new Win32Exception();
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiOpenDeviceInfoW(set, instanceId, IntPtr.Zero, 0, ref data))
                    throw new Win32Exception();

                var p = new SP_PROPCHANGE_PARAMS
                {
                    ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                    {
                        cbSize = (uint)Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                        InstallFunction = DIF_PROPERTYCHANGE,
                    },
                    StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
                    Scope = DICS_FLAG_GLOBAL,
                    HwProfile = 0,
                };
                if (!SetupDiSetClassInstallParamsW(set, ref data, ref p, Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()))
                    throw new Win32Exception();
                if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, ref data))
                    throw new Win32Exception();
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static string? GetStringProperty(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
        {
            SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, null, 0, out int size);
            if (size <= 0)
                return null;
            var buffer = new byte[size];
            if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buffer, size, out _))
                return null;
            return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }
    }
}
