using System.Diagnostics;

namespace AdapterToggle;

internal static class AppLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AdapterToggle", "AdapterToggle.log");

    public static void WriteError(string context, Exception exception)
    {
        string entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context}{Environment.NewLine}" +
                       $"{exception}{Environment.NewLine}" +
                       $"OS: {Environment.OSVersion}; Process: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}; " +
                       $".NET: {Environment.Version}{Environment.NewLine}" +
                       $"{new string('-', 80)}{Environment.NewLine}";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, entry);
        }
        catch (Exception logException)
        {
            Debug.WriteLine($"Failed to write diagnostic log to \"{FilePath}\": {logException}");
            Debug.WriteLine(entry);
        }
    }
}
