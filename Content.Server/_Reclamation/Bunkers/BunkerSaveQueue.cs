using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._Reclamation.Bunkers;

/// <summary>
/// Only detached serialization nodes cross into the worker. Queue operations and completion
/// handling run on the game thread; a single writer owns manifest publication and pruning.
/// </summary>
internal sealed class BunkerSaveQueue
{
    internal sealed record Result(int Count, TimeSpan Duration, Exception? Error, string? CleanupWarning);

    private readonly Dictionary<string, MappingDataNode> _pending = new();
    private HashSet<string> _writingKeys = new();
    private Task<Result>? _writing;
    private readonly Func<string, IReadOnlyDictionary<string, MappingDataNode>, Result> _publish;

    public BunkerSaveQueue() : this(Publish) { }

    internal BunkerSaveQueue(Func<string, IReadOnlyDictionary<string, MappingDataNode>, Result> publish)
    {
        _publish = publish;
    }

    public bool Contains(string key) => _pending.ContainsKey(key) || _writingKeys.Contains(key);
    public bool HasWork => _writing != null || _pending.Count > 0;

    public void Enqueue(string key, MappingDataNode snapshot)
    {
        // Keep only the latest waiting snapshot for a room, without changing an in-flight write.
        _pending[key] = snapshot;
    }

    public Result? Update(string folder)
    {
        if (_writing is { IsCompleted: false })
            return null;

        var result = Complete();
        Start(folder);
        return result;
    }

    /// <summary>Used only at explicit save/round-end/shutdown boundaries, never in Update.</summary>
    public List<Result> Flush(string folder)
    {
        var results = new List<Result>();
        while (_writing != null || _pending.Count > 0)
        {
            Start(folder);
            if (Complete() is { } result)
                results.Add(result);
        }
        return results;
    }

    private void Start(string folder)
    {
        if (_writing != null || _pending.Count == 0)
            return;

        var batch = new Dictionary<string, MappingDataNode>(_pending);
        _pending.Clear();
        _writingKeys = batch.Keys.ToHashSet();
        _writing = Task.Run(() => _publish(folder, batch));
    }

    private Result? Complete()
    {
        if (_writing == null)
            return null;

        // Update checks IsCompleted first. Flush deliberately waits during shutdown/safety saves.
#pragma warning disable RA0004
        var result = _writing.GetAwaiter().GetResult();
#pragma warning restore RA0004
        _writing = null;
        _writingKeys.Clear();
        return result;
    }

    internal static Result Publish(string folder, IReadOnlyDictionary<string, MappingDataNode> snapshots)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(folder);
            var manifest = ReadManifest(folder);
            foreach (var (key, snapshot) in snapshots)
            {
                var filename = key + "_" + Guid.NewGuid().ToString("N") + ".yml";
                var path = SafeFile(folder, filename);
                BunkerSnapshotReferences.Clean(snapshot);
                // Emit directly to disk once; no full-size YAML string or second parse is needed.
                // DataNode.Write uses the engine's YAML emitter, including explicit type tags.
                using (var writer = new StreamWriter(path, false, Encoding.UTF8))
                    snapshot.Write(writer);
                manifest[key] = filename;
            }

            var target = Path.Combine(folder, "manifest.json");
            var temporary = Path.Combine(folder, "manifest.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest), Encoding.UTF8);
            if (File.Exists(target))
            {
                var previousTemporary = Path.Combine(folder, "manifest.previous.tmp");
                File.Copy(target, previousTemporary, true);
                File.Move(previousTemporary, Path.Combine(folder, "manifest.previous.json"), true);
            }
            File.Move(temporary, target, true);

            // Failure here must not report an already-published save as lost. The old current
            // manifest is retained, so game-thread reads remain valid during this publication.
            string? cleanupWarning = null;
            try
            {
                var retained = manifest.Values.ToHashSet();
                var backup = Path.Combine(folder, "manifest.previous.json");
                if (File.Exists(backup))
                    retained.UnionWith(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(backup))!.Values);
                foreach (var file in Directory.EnumerateFiles(folder, "*.yml"))
                {
                    var name = Path.GetFileName(file);
                    if (IsSnapshotName(name) && !retained.Contains(name))
                        File.Delete(file);
                }
            }
            catch (Exception e)
            {
                cleanupWarning = e.Message;
            }
            return new Result(snapshots.Count, timer.Elapsed, null, cleanupWarning);
        }
        catch (Exception e)
        {
            // No retry loop. A later explicit save/exit can try again; the old manifest is intact
            // unless publication already succeeded (post-publication cleanup is handled above).
            return new Result(snapshots.Count, timer.Elapsed, e, null);
        }
    }

    internal static Dictionary<string, string> ReadManifest(string folder)
    {
        var path = Path.Combine(folder, "manifest.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
              ?? throw new IOException("Invalid bunker manifest.")
            : new();
    }

    internal static string SafeFile(string folder, string filename)
    {
        if (!IsSnapshotName(filename))
            throw new IOException("Invalid bunker snapshot filename.");
        return Path.Combine(folder, filename);
    }

    private static bool IsSnapshotName(string filename) => filename.Length == 101
        && filename[64] == '_' && filename.EndsWith(".yml", StringComparison.Ordinal)
        && filename.Take(64).Concat(filename.Skip(65).Take(32)).All(Uri.IsHexDigit);
}
