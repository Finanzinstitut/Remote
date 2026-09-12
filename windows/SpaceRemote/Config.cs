using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpaceRemote;

public class Config
{
    public int Port { get; set; } = 47800;
    public string Password { get; set; } = "";
    /// <summary>Maximum width of the streamed image (smaller = smoother).</summary>
    public int MaxWidth { get; set; } = 1280;
    /// <summary>JPEG quality, 1-100.</summary>
    public int JpegQuality { get; set; } = 60;
    public int Fps { get; set; } = 25;
    public bool FirstRunDone { get; set; }
    /// <summary>Keep the machine awake while Space Remote runs.</summary>
    public bool KeepAwake { get; set; }

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceRemote");

    static string FilePath => Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        Config cfg = null;
        try
        {
            if (File.Exists(FilePath))
                cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath));
        }
        catch { /* broken file -> start fresh */ }

        cfg ??= new Config();
        cfg.JpegQuality = Math.Clamp(cfg.JpegQuality, 10, 100);
        cfg.Fps = Math.Clamp(cfg.Fps, 1, 60);
        cfg.MaxWidth = Math.Clamp(cfg.MaxWidth, 320, 3840);
        if (string.IsNullOrWhiteSpace(cfg.Password))
            cfg.Password = GeneratePassword();
        cfg.Save();
        return cfg;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    static string GeneratePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I
        var sb = new StringBuilder();
        for (int i = 0; i < 8; i++) sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        return sb.ToString();
    }
}
