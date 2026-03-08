using System.Text.Json;

namespace backend.Services;

public class WorldTimeApiService : ITimeService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WorldTimeApiService> _logger;
    // Try TimeAPI.io first (WorldTimeAPI is often unreachable)
    private static readonly string[] TimeApiUrls =
    [
        "https://timeapi.io/api/time/current/zone?timeZone=Europe/Zurich",
        "https://worldtimeapi.org/api/timezone/Europe/Zurich"
    ];

    // Cache: store the offset between server UTC and Zurich API time
    private static TimeSpan _cachedOffset;
    private static DateTime _cachedAtUtc;
    private static bool _hasCachedOffset;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeZoneInfo ZurichZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");

    public WorldTimeApiService(HttpClient httpClient, ILogger<WorldTimeApiService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
        _logger = logger;
    }

    public async Task<DateTime> GetZurichTimeAsync()
    {
        // Return cached calculation if recent
        if (_hasCachedOffset && (DateTime.UtcNow - _cachedAtUtc) < CacheDuration)
        {
            // SpecifyKind(Unspecified) so JSON serialization omits "Z" suffix —
            // the frontend relies on all Zurich-local timestamps having no TZ marker.
            return DateTime.SpecifyKind(DateTime.UtcNow.Add(_cachedOffset), DateTimeKind.Unspecified);
        }

        // Try external APIs
        foreach (var url in TimeApiUrls)
        {
            try
            {
                var zurichTime = await TryFetchFromApi(url);
                if (zurichTime.HasValue)
                {
                    // Cache the offset for fast subsequent calls
                    _cachedOffset = zurichTime.Value - DateTime.UtcNow;
                    _cachedAtUtc = DateTime.UtcNow;
                    _hasCachedOffset = true;
                    return zurichTime.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch time from {Url}", url);
            }
        }

        // Fallback: convert UTC to Zurich time using .NET TimeZoneInfo
        _logger.LogWarning(
            "All external time APIs failed. Falling back to server-side timezone conversion.");
        var zurichFallback = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZurichZone);

        // Cache the fallback offset too so subsequent calls are instant
        _cachedOffset = zurichFallback - DateTime.UtcNow;
        _cachedAtUtc = DateTime.UtcNow;
        _hasCachedOffset = true;

        return zurichFallback;
    }

    /// <summary>Reset cached offset (for test isolation).</summary>
    public static void ResetCache()
    {
        _hasCachedOffset = false;
        _cachedOffset = default;
        _cachedAtUtc = default;
    }

    private async Task<DateTime?> TryFetchFromApi(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // WorldTimeAPI format
        if (doc.RootElement.TryGetProperty("datetime", out var datetimeProp))
        {
            var dateTimeString = datetimeProp.GetString()
                ?? throw new Exception("datetime field is null");
            var zurichTime = DateTimeOffset.Parse(dateTimeString).DateTime;
            _logger.LogInformation("Fetched Zurich time from {Url}: {ZurichTime}", url, zurichTime);
            return zurichTime;
        }

        // TimeAPI.io format
        if (doc.RootElement.TryGetProperty("dateTime", out var dateTimeProp))
        {
            var dateTimeString = dateTimeProp.GetString()
                ?? throw new Exception("dateTime field is null");
            var zurichTime = DateTime.Parse(dateTimeString);
            _logger.LogInformation("Fetched Zurich time from {Url}: {ZurichTime}", url, zurichTime);
            return zurichTime;
        }

        return null;
    }
}
