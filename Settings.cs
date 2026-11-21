using System.Text.Json;

namespace AdapterToggle;

internal sealed class Settings
{
    // substring matched against the device's friendly name/description.
    public string DeviceNameMatch { get; set; } = "Xbox Wireless Adapter";

    // disable the adapter after this many minutes without a connected controller. 0 = never.
    public int AutoOffMinutes { get; set; } = 15;

    // enable the adapter automatically while one of the added processes is running.
    public bool AutoOnEnabled { get; set; } = false;

    // process names on which auto on is triggered
    public string[] AutoOnProcesses { get; set; } = [];

    public int PollSeconds { get; set; } = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdapterToggle", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
        }
        catch (Exception ex)
        {
            AppLog.WriteError($"Failed to load settings from \"{FilePath}\"; using defaults.", ex);
        }

        var defaults = new Settings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
