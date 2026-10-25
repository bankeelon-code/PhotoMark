using System.Text.Json;

namespace PhotoMark;

public sealed record WatermarkSettings
{
    public int UiVersion { get; init; }
    public bool UseLogo { get; init; }
    public string Text { get; init; } = "拾印 PhotoMark";
    public string FontName { get; init; } = "Microsoft YaHei UI";
    public string LogoPath { get; init; } = "";
    public int ColorArgb { get; init; } = Color.White.ToArgb();
    public int SizePercent { get; init; } = 5;
    public int LogoPercent { get; init; } = 20;
    public int Opacity { get; init; } = 70;
    public int MarginPercent { get; init; } = 4;
    public int Position { get; init; } = 4;
    public int LayoutMode { get; init; }
    public int Rotation { get; init; }
    public int TileSpacingPercent { get; init; } = 34;
    public int PatternRadiusPercent { get; init; } = 30;
    public int PatternCount { get; init; } = 8;
    public string OutputFolder { get; init; } = "";

    public WatermarkSettings Normalized() => this with
    {
        Text = (Text ?? "")[..Math.Min((Text ?? "").Length, 200)],
        FontName = string.IsNullOrWhiteSpace(FontName) ? "Microsoft YaHei UI" : FontName,
        LogoPath = LogoPath ?? "",
        SizePercent = Math.Clamp(SizePercent, 1, 20),
        LogoPercent = Math.Clamp(LogoPercent, 5, 60),
        Opacity = Math.Clamp(Opacity, 0, 100),
        MarginPercent = Math.Clamp(MarginPercent, 0, 20),
        Position = Math.Clamp(Position, 0, 8),
        LayoutMode = Math.Clamp(LayoutMode, 0, 7),
        Rotation = Math.Clamp(Rotation, -90, 90),
        TileSpacingPercent = Math.Clamp(TileSpacingPercent, 15, 80),
        PatternRadiusPercent = Math.Clamp(PatternRadiusPercent, 15, 40),
        PatternCount = Math.Clamp(PatternCount, 6, 16),
        OutputFolder = OutputFolder ?? ""
    };
}

internal static class SettingsStore
{
    internal static bool TestMode;
    static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoMark");
    static readonly string FileName = Path.Combine(Folder, "settings.json");
    public static WatermarkSettings Load()
    {
        if (TestMode) return new();
        try
        {
            var settings=(JsonSerializer.Deserialize<WatermarkSettings>(File.ReadAllText(FileName)) ?? new()).Normalized();
            return settings.UiVersion<14 ? settings with { Text="拾印 PhotoMark", UseLogo=false, UiVersion=14 } : settings;
        }
        catch { return new(); }
    }
    public static void Save(WatermarkSettings settings)
    {
        if (TestMode) return;
        Directory.CreateDirectory(Folder);
        string temporary = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FileName, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

