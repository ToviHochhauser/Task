using System.Text.Json;
using backend.Models;

namespace backend.Services;

public class OfflineQueueService
{
    private readonly string _filePath;
    private readonly ILogger<OfflineQueueService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public OfflineQueueService(IConfiguration configuration, ILogger<OfflineQueueService> logger)
    {
        _logger = logger;
        // Store the queue file next to the app (not in wwwroot)
        var dataDir = configuration["OfflineQueue:DataDir"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "offline-queue.json");

        // Ensure the file exists
        if (!File.Exists(_filePath))
            File.WriteAllText(_filePath, "[]");
    }

    /// <summary>Enqueue a pending clock action to the JSON file.</summary>
    public async Task EnqueueAsync(OfflineEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            var entries = await ReadFileAsync();
            entries.Add(entry);
            await WriteFileAsync(entries);
            _logger.LogWarning("Offline queue: saved {Action} for user {UserId} (queue size: {Count})",
                entry.Action, entry.UserId, entries.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Read all pending entries.</summary>
    public async Task<List<OfflineEntry>> GetPendingAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await ReadFileAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Remove successfully synced entries by their IDs.</summary>
    public async Task RemoveSyncedAsync(IEnumerable<string> syncedIds)
    {
        var idSet = syncedIds.ToHashSet();
        await _lock.WaitAsync();
        try
        {
            var entries = await ReadFileAsync();
            var remaining = entries.Where(e => !idSet.Contains(e.Id)).ToList();
            await WriteFileAsync(remaining);
            _logger.LogInformation("Offline queue: removed {Count} synced entries, {Remaining} remaining",
                entries.Count - remaining.Count, remaining.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Returns the number of pending entries.</summary>
    public async Task<int> GetPendingCountAsync()
    {
        var entries = await GetPendingAsync();
        return entries.Count;
    }

    private async Task<List<OfflineEntry>> ReadFileAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<OfflineEntry>>(json, JsonOptions)
            ?? new List<OfflineEntry>();
    }

    private async Task WriteFileAsync(List<OfflineEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        // Write to temp file then rename for atomic update
        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
