using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace AdapterToggle;

// registers an elevated "at logon" scheduled task so the app starts with windows
internal static class StartupTask
{
    private const string TaskName = "AdapterToggle";

    public static bool IsRegistered() => RunSchtasks($"/query /tn \"{TaskName}\"") == 0;

    public static void Register()
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name)!;
        string exe = SecurityElement.Escape(Environment.ProcessPath!)!;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts the AdapterToggle tray app at logon.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{exe}</Command></Exec></Actions>
            </Task>
            """;

        string file = Path.Combine(Path.GetTempPath(), "AdapterToggle.task.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            if (RunSchtasks($"/create /tn \"{TaskName}\" /xml \"{file}\" /f") != 0)
                throw new InvalidOperationException("schtasks failed to create the startup task.");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void Unregister()
    {
        if (RunSchtasks($"/delete /tn \"{TaskName}\" /f") != 0)
            throw new InvalidOperationException("schtasks failed to delete the startup task.");
    }

    private static int RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}
