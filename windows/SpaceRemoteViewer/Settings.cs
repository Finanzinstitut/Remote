using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpaceRemoteViewer;

/// <summary>Remembered connection values. The password is encrypted with Windows DPAPI for the current user.</summary>
public class Settings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 47800;
    public string PasswordProtected { get; set; } = "";
    public string Mac { get; set; } = "";
    public string WakeUrl { get; set; } = "";
    public bool Remember { get; set; } = true;
    public bool SystemKeys { get; set; } = true;

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceRemoteViewer");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public string GetPassword()
    {
        if (string.IsNullOrEmpty(PasswordProtected)) return "";
        try
        {
            var raw = ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        }
        catch { return ""; }
    }

    public void SetPassword(string password)
    {
        PasswordProtected = string.IsNullOrEmpty(password)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
