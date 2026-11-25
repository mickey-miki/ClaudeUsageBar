using System.Text.Json;

namespace ClaudeUsageBar;

/// <summary>位置・表示状態・最後の値。認証情報は保存しない。</summary>
public sealed class AppState
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageBar", "state.json");

    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Visible { get; set; } = true;
    public string? Theme { get; set; }
    public UsageSnapshot? Last { get; set; }

    public static AppState Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new AppState();
            if (s.Last is { } l && (l.FiveHour is null || l.SevenDay is null)) s.Last = null;
            return s;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppState();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, FilePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
