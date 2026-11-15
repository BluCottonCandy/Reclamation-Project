using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Content.Server._Reclamation.Bunkers;
using NUnit.Framework;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Server.Bunkers;

[TestFixture]
public sealed class BunkerSaveQueueTest
{
    private static readonly string KeyA = new('a', 64);
    private static readonly string KeyB = new('b', 64);
    private string _folder = default!;

    [SetUp]
    public void SetUp() => _folder = Path.Combine(Path.GetTempPath(), "bunker-save-test-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static MappingDataNode Snapshot(string value) => new() { { "value", value } };
    private string ReadRoom(string key)
    {
        var name = BunkerSaveQueue.ReadManifest(_folder)[key];
        return File.ReadAllText(BunkerSaveQueue.SafeFile(_folder, name));
    }

    [Test]
    public void FlushPublishesLatestWaitingSnapshotAndPreservesOtherRooms()
    {
        var saves = new BunkerSaveQueue();
        saves.Enqueue(KeyA, Snapshot("old"));
        saves.Enqueue(KeyB, Snapshot("other"));
        saves.Enqueue(KeyA, Snapshot("new"));
        Assert.That(saves.Contains(KeyA), Is.True);
        var results = saves.Flush(_folder);
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Error, Is.Null);
        Assert.That(results[0].Count, Is.EqualTo(2));
        Assert.That(ReadRoom(KeyA), Does.Contain("new"));
        Assert.That(ReadRoom(KeyB), Does.Contain("other"));
        Assert.That(saves.Contains(KeyA), Is.False);
        Assert.That(saves.Flush(_folder), Is.Empty);
    }

    [Test]
    public void SlowWriterDoesNotBlockUpdatesOrOverlapANewerPublication()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var gameThread = Environment.CurrentManagedThreadId;
        var workerThread = gameThread;
        var saves = new BunkerSaveQueue((folder, batch) =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.Set();
                if (!release.Wait(TimeSpan.FromSeconds(20)))
                    return new BunkerSaveQueue.Result(0, TimeSpan.Zero, new TimeoutException(), null);
            }
            return BunkerSaveQueue.Publish(folder, batch);
        });
        saves.Enqueue(KeyA, Snapshot("first"));
        saves.Update(_folder);
        try
        {
            Assert.That(started.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(workerThread, Is.Not.EqualTo(gameThread));
            saves.Enqueue(KeyA, Snapshot("second"));
            saves.Enqueue(KeyA, Snapshot("latest"));
            Assert.That(saves.Update(_folder), Is.Null, "must return while the writer is blocked");
            Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));
            Assert.That(saves.Contains(KeyA), Is.True, "room must not unload while writing");
        }
        finally
        {
            release.Set();
            var results = saves.Flush(_folder);
            Assert.That(results.All(result => result.Error == null), Is.True);
        }
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(ReadRoom(KeyA), Does.Contain("latest"));
        var previous = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(_folder, "manifest.previous.json")))!;
        Assert.That(File.ReadAllText(BunkerSaveQueue.SafeFile(_folder, previous[KeyA])), Does.Contain("first"));
    }

    [Test]
    public void FailedPublicationKeepsCurrentSaveAndDoesNotAutomaticallyRetry()
    {
        var saves = new BunkerSaveQueue();
        saves.Enqueue(KeyA, Snapshot("good"));
        Assert.That(saves.Flush(_folder)[0].Error, Is.Null);
        var manifest = File.ReadAllText(Path.Combine(_folder, "manifest.json"));
        // A directory at the temporary file path deterministically fails the transaction
        // after writing the new snapshot but before publishing its manifest.
        Directory.CreateDirectory(Path.Combine(_folder, "manifest.tmp"));
        saves.Enqueue(KeyA, Snapshot("must-not-publish"));
        var result = saves.Flush(_folder).Single();
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(File.ReadAllText(Path.Combine(_folder, "manifest.json")), Is.EqualTo(manifest));
        Assert.That(ReadRoom(KeyA), Does.Contain("good"));
        Assert.That(saves.Contains(KeyA), Is.False);
        Directory.Delete(Path.Combine(_folder, "manifest.tmp"));
        for (var i = 0; i < 10; i++) Assert.That(saves.Update(_folder), Is.Null);
        Assert.That(File.ReadAllText(Path.Combine(_folder, "manifest.json")), Is.EqualTo(manifest));
        saves.Enqueue(KeyA, Snapshot("explicit-new-attempt"));
        Assert.That(saves.Flush(_folder).Single().Error, Is.Null);
        Assert.That(ReadRoom(KeyA), Does.Contain("explicit-new-attempt"));
    }

    [Test]
    public void FailureInOneRoomDoesNotPublishHalfOfABatch()
    {
        var saves = new BunkerSaveQueue();
        saves.Enqueue(KeyA, Snapshot("good"));
        saves.Flush(_folder);
        saves.Enqueue(KeyA, Snapshot("partial"));
        saves.Enqueue("../invalid", Snapshot("invalid"));
        Assert.That(saves.Flush(_folder).Single().Error, Is.Not.Null);
        Assert.That(ReadRoom(KeyA), Does.Contain("good"));
        Assert.That(BunkerSaveQueue.ReadManifest(_folder), Has.Count.EqualTo(1));
    }

    [Test]
    public void PruningRetainsTwoGenerationsAndLeavesUnrelatedFilesAlone()
    {
        var saves = new BunkerSaveQueue();
        saves.Enqueue(KeyA, Snapshot("one"));
        saves.Flush(_folder);
        var oldest = BunkerSaveQueue.ReadManifest(_folder)[KeyA];
        File.WriteAllText(Path.Combine(_folder, "manual-backup.yml"), "keep");
        saves.Enqueue(KeyA, Snapshot("two"));
        saves.Flush(_folder);
        var previous = BunkerSaveQueue.ReadManifest(_folder)[KeyA];
        saves.Enqueue(KeyA, Snapshot("three"));
        saves.Flush(_folder);
        Assert.That(File.Exists(BunkerSaveQueue.SafeFile(_folder, oldest)), Is.False);
        Assert.That(File.Exists(BunkerSaveQueue.SafeFile(_folder, previous)), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(_folder, "manual-backup.yml")), Is.EqualTo("keep"));
        Assert.That(ReadRoom(KeyA), Does.Contain("three"));
    }

    [Test]
    public void WorkerPreservesTypeTagsAndSanitizesRuntimeReferences()
    {
        var tagged = new MappingDataNode { Tag = "!type:Example" };
        tagged.Add("value", "kept");
        var component = new MappingDataNode
        {
            { "type", "UserInterface" },
            { "actors", new SequenceDataNode(new[] { new ValueDataNode("123") }) },
            { "custom", tagged },
        };
        var entity = new MappingDataNode { { "components", new SequenceDataNode(new[] { component }) } };
        var group = new MappingDataNode { { "entities", new SequenceDataNode(new[] { entity }) } };
        var data = new MappingDataNode { { "entities", new SequenceDataNode(new[] { group }) } };
        var saves = new BunkerSaveQueue();
        saves.Enqueue(KeyA, data);
        Assert.That(saves.Flush(_folder).Single().Error, Is.Null);
        var yaml = ReadRoom(KeyA);
        Assert.That(yaml, Does.Not.Contain("actors:"));
        Assert.That(yaml, Does.Contain("!type:Example"));
        var parsed = new YamlStream();
        Assert.DoesNotThrow(() => parsed.Load(new StringReader(yaml)));
    }

    [Test]
    public void InvalidExistingManifestIsPreserved()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "manifest.json");
        File.WriteAllText(path, "invalid-json");
        var saves = new BunkerSaveQueue();
        saves.Enqueue(KeyA, Snapshot("new"));
        Assert.That(saves.Flush(_folder).Single().Error, Is.Not.Null);
        Assert.That(File.ReadAllText(path), Is.EqualTo("invalid-json"));
    }

    [Test]
    public void DirectSnapshotCleanupMatchesLegacyCleanupWithoutChangingSavedContents()
    {
        const string yaml = """
            entities:
            - proto: Example
              entities:
              - uid: 1
                components:
                - type: Jukebox
                  audioStream: 50
                  songs: [one, two]
                - type: Store
                  startingMap: 42
                  boughtEntities: [invalid, 7, invalid, 8]
                  balance: 123
                - type: NanoChatCartridge
                  station: 2
                  card: 3
                  messages: [hello]
                - type: UserInterface
                  actors: [4, 5]
                  custom: !type:Example {value: kept}
                - type: Storage
                  contents: [6, 9]
            """;
        static MappingDataNode Parse(string text)
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return new MappingDataNode((YamlMappingNode) stream.Documents[0].RootNode);
        }
        var expected = Parse(BunkerSnapshotReferences.Clean(yaml));
        var actual = Parse(yaml);
        BunkerSnapshotReferences.Clean(actual);
        Assert.That(actual, Is.EqualTo(expected));
    }
}
