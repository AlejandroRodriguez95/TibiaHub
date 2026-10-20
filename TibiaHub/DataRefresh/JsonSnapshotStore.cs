using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TibiaHub.DataRefresh;

public sealed record DataSnapshot<T>(DateTimeOffset UpdatedAt, string SourceKey, T Data);

// Atomic file replacement keeps the last complete result available across restarts.
public sealed class JsonSnapshotStore<T>(
    IWebHostEnvironment environment, IOptions<RefreshOptions> options,
    ILogger<JsonSnapshotStore<T>> logger, string? key = null) where T : class
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DataSnapshot<T>? snapshot;
    private bool loaded;
    private string FilePath => Path.Combine(environment.ContentRootPath,
        options.Value.CacheDirectory, typeof(T).Name + (key is null ? "" : "." + Uri.EscapeDataString(key)) + ".json");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DataSnapshot<T>?> ReadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!loaded)
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        await using var stream = File.OpenRead(FilePath);
                        snapshot = await JsonSerializer.DeserializeAsync<DataSnapshot<T>>(stream, JsonOptions, token);
                    }
                }
                catch (Exception e) when (e is JsonException or IOException)
                {
                    logger.LogWarning(e, "Could not read cached {Snapshot}; a new refresh is required", typeof(T).Name);
                }
                loaded = true;
            }
            return snapshot;
        }
        finally { gate.Release(); }
    }

    public async Task WriteAsync(DataSnapshot<T> value, CancellationToken token)
    {
        await gate.WaitAsync(token);
        var temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions), token);
            File.Move(temporary, FilePath, overwrite: true);
            snapshot = value;
            loaded = true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }
}
