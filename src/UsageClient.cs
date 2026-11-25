using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeUsageBar;

public sealed record UsageWindow(double Percent, DateTimeOffset? ResetsAt);
public sealed record UsageSnapshot(UsageWindow FiveHour, UsageWindow SevenDay, DateTimeOffset FetchedAt);

/// <summary>画面に出してよい短い理由だけを持つ例外。</summary>
public sealed class UsageException(string reason, TimeSpan? retryAfter = null) : Exception(reason)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public interface IUsageSource
{
    Task<UsageSnapshot> FetchAsync();
}

/// <summary>Claude Code のトークンを読み取り専用で使い、使用量だけを取得する。</summary>
public sealed class OAuthUsageSource : IUsageSource
{
    static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/usage");
    static readonly TimeSpan DefaultBackoff = TimeSpan.FromMinutes(15);

    // トークンが別の宛先へ渡らないよう、リダイレクトは追わない。
    readonly HttpClient http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<UsageSnapshot> FetchAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken());
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        try
        {
            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var header = response.Headers.RetryAfter;
                var wait = header?.Delta ?? (header?.Date - DateTimeOffset.UtcNow) ?? DefaultBackoff;
                if (wait < TimeSpan.FromMinutes(5)) wait = DefaultBackoff;
                throw new UsageException("回数制限中", wait);
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UsageException("トークン期限切れ");
            if (!response.IsSuccessStatusCode)
                throw new UsageException($"HTTP {(int)response.StatusCode}");
            return Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new UsageException("通信エラー");
        }
    }

    static string ReadToken()
    {
        var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        try
        {
            // Claude Code の書き換えを妨げない共有モードで開く。
            using var stream = new FileStream(Path.Combine(dir, ".credentials.json"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var oauth = doc.RootElement.GetProperty("claudeAiOauth");
            var expires = DateTimeOffset.FromUnixTimeMilliseconds(oauth.GetProperty("expiresAt").GetInt64());
            if (expires <= DateTimeOffset.UtcNow) throw new UsageException("トークン期限切れ");
            var token = oauth.GetProperty("accessToken").GetString();
            if (string.IsNullOrWhiteSpace(token) || token.Any(c => c < '!' || c > '~'))
                throw new UsageException("認証情報なし");
            return token;
        }
        catch (Exception e) when (e is not UsageException)
        {
            throw new UsageException("認証情報なし");
        }
    }

    static UsageSnapshot Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return new UsageSnapshot(Window(doc.RootElement, "five_hour"), Window(doc.RootElement, "seven_day"),
                DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            // 仕様変更などで読めない値を 0% と見せない。
            throw new UsageException("応答形式エラー");
        }
    }

    static UsageWindow Window(JsonElement root, string name)
    {
        var w = root.GetProperty(name);
        var reset = w.GetProperty("resets_at");
        return new UsageWindow(w.GetProperty("utilization").GetDouble(),
            reset.ValueKind == JsonValueKind.Null ? null : reset.GetDateTimeOffset());
    }
}
