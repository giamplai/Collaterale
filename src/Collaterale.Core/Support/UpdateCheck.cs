using System.Text.Json;
using Collaterale.Core.Settings;

namespace Collaterale.Core.Support;

public sealed record UpdateResult(bool Available, string Version, string Url, string Message);

/// <summary>Asks a small JSON feed (<c>{"version": "1.2.3", "url": "https://…"}</c>) whether a newer version exists.</summary>
public static class UpdateCheck
{
    private static bool IsWebAddress(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    private static bool TryVersion(string text, out Version v)
        => Version.TryParse(text.Trim().TrimStart('v', 'V'), out v!);

    public static bool IsNewer(string current, string offered)
        => TryVersion(current, out var c) && TryVersion(offered, out var o) && o > c;

    public static bool IsDue(UpdatePreferences p, DateTime nowUtc)
        => p.Check && IsWebAddress(p.FeedUrl) && (p.LastCheckUtc == default || nowUtc - p.LastCheckUtc >= TimeSpan.FromDays(Math.Max(1, p.EveryDays)));

    public static async Task<UpdateResult> RunAsync(HttpClient http, string feedUrl, string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(feedUrl, ct);
            if (!response.IsSuccessStatusCode) { return new UpdateResult(false, "", "", $"The update check could not read the feed ({(int)response.StatusCode})."); }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            if (!IsNewer(currentVersion, version)) { return new UpdateResult(false, version, "", "You have the latest version."); }
            if (!IsWebAddress(url)) { url = ""; }
            return new UpdateResult(true, version, url, url.Length > 0 ? $"Version {version} is available: {url}" : $"Version {version} is available.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            return new UpdateResult(false, "", "", "The update check could not reach the feed.");
        }
    }
}
