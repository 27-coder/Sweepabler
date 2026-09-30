using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using ProperAppUpdater.Models;
using ProperAppUpdater.Services;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater.Tests;

internal static class UpdatePipelineTests
{
    public static async Task ParallelQueueAsync()
    {
        var root = Scratch();
        try
        {
            using var handler = new GatedHandler();
            using var client = new HttpClient(handler);
            var executor = new UpdateExecutor(client, downloadsRoot: root);
            var candidates = new[] { Candidate("large", 500), Candidate("small", 10), Candidate("medium", 100), Candidate("unknown", null) };
            await using var queue = new UpdateDownloadQueue(candidates, executor, (_, _) => { }, CancellationToken.None);
            await handler.ThreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(handler.Started.SequenceEqual(new[] { "small", "medium", "large" }), "the smallest three did not start first");
            Check(handler.Started.Count == 3, "a fourth download exceeded the three-slot limit");
            handler.Release.TrySetResult();
            var records = await Task.WhenAll(candidates.Select(candidate => queue.GetPrepared(candidate)!));
            Check(handler.Started.Count == 4, "the queued fourth download did not start");
            Check(records.All(record => record.Error.Length == 0 && record.ExitCode is null), "preparation tried to install or failed");
            Check(records.All(record => File.Exists(record.InstallerPath)), "a completed installer was missing");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task CancellationAsync()
    {
        var root = Scratch();
        try
        {
            using var client = new HttpClient(new StallHandler()) { Timeout = Timeout.InfiniteTimeSpan };
            using var cancellation = new CancellationTokenSource();
            var candidates = Enumerable.Range(0, 6).Select(index => Candidate($"cancel-{index}", index + 1)).ToArray();
            await using var queue = new UpdateDownloadQueue(candidates, new UpdateExecutor(client, downloadsRoot: root), (_, _) => { }, cancellation.Token);
            var timeout = Stopwatch.StartNew();
            while (!Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any())
            {
                Check(timeout.Elapsed < TimeSpan.FromSeconds(5), "no transfer started");
                await Task.Delay(10);
            }
            cancellation.Cancel();
            try { await Task.WhenAll(candidates.Select(candidate => queue.GetPrepared(candidate)!)); }
            catch (OperationCanceledException) { }
            Check(candidates.All(candidate => queue.GetPrepared(candidate)!.IsCanceled), "queued or active work survived cancellation");
            Check(!Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any(), "cancellation left partial files");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task BandwidthReleaseAsync()
    {
        var coordinator = new DownloadBandwidthCoordinator(new[] { ("large", (long?)200_000_000), ("small", (long?)1_000_000) });
        var elapsed = Stopwatch.StartNew();
        await coordinator.PaceAsync("large", 128 * 1024, CancellationToken.None);
        Check(elapsed.ElapsedMilliseconds >= 100, "a large transfer did not yield bandwidth");
        var pending = coordinator.PaceAsync("large", 2 * 1024 * 1024, CancellationToken.None);
        await Task.Delay(40);
        coordinator.Complete("small");
        await pending.WaitAsync(TimeSpan.FromMilliseconds(500));
        elapsed.Restart();
        await coordinator.PaceAsync("large", 2 * 1024 * 1024, CancellationToken.None);
        Check(elapsed.ElapsedMilliseconds < 100, "the large transfer stayed limited after small transfers finished");
        Check(!coordinator.ShouldLimit("large"), "the throttle did not release");
    }

    public static async Task ReadyOrderAsync()
    {
        var root = Scratch();
        try
        {
            using var handler = new GatedHandler();
            using var client = new HttpClient(handler);
            var candidates = new[] { Candidate("one", 1), Candidate("two", 2), Candidate("three", 3) };
            await using var queue = new UpdateDownloadQueue(candidates, new UpdateExecutor(client, downloadsRoot: root), (_, _) => { }, CancellationToken.None);
            await handler.ThreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var managed = Candidate("managed", null, "winget");
            Check(await queue.NextAsync(new[] { candidates[0], managed }) == managed, "package-manager work stalled behind a download");
            handler.Release.TrySetResult();
            await Task.WhenAll(candidates.Select(candidate => queue.GetPrepared(candidate)!));
            Check(await queue.NextAsync(candidates) == candidates[0], "the smallest ready installer was not selected");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task ChangedPreparedFileAsync()
    {
        var root = Scratch();
        try
        {
            var bytes = new byte[] { 1, 2, 3, 4 };
            using var client = new HttpClient(new BytesHandler(bytes));
            var executor = new UpdateExecutor(client, downloadsRoot: root);
            var candidate = Candidate("integrity", 4);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var record = await executor.PrepareAsync(candidate.Catalog,
                new UpdateRecord { ToVersion = "2.0", DownloadUrl = candidate.DownloadUrl }, "", hash, new Progress<string>(), CancellationToken.None);
            Check(record.Error.Length == 0 && record.VerifiedBy == "sha256", "matching bytes failed preparation");
            await File.WriteAllBytesAsync(record.InstallerPath, new byte[] { 9, 8, 7, 6 });
            var installed = await executor.InstallAsync(candidate.Catalog, "integrity", "1", "2", candidate.DownloadUrl,
                "", hash, "test", "test", new Progress<string>(), CancellationToken.None, record);
            Check(installed.ExitCode is null && installed.Error.Contains("checksum", StringComparison.OrdinalIgnoreCase), "changed prepared bytes reached installer execution");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task IncompleteDownloadAsync()
    {
        var root = Scratch();
        try
        {
            using var client = new HttpClient(new BytesHandler(new byte[] { 1, 2 }, advertisedSize: 10));
            var candidate = Candidate("incomplete", 10);
            var record = await new UpdateExecutor(client, downloadsRoot: root).PrepareAsync(candidate.Catalog,
                new UpdateRecord { ToVersion = "2.0", DownloadUrl = candidate.DownloadUrl }, "", "", new Progress<string>(), CancellationToken.None);
            Check(record.Error.Contains("incomplete", StringComparison.OrdinalIgnoreCase) && record.ExitCode is null,
                "truncated content was accepted");
            Check(!Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(), "truncated download left an installer");
        }
        finally { Directory.Delete(root, true); }
    }

    public static Task LocalizationAsync()
    {
        LocalizationService.SetCurrent("en");
        var tr = new LocalizationService("tr");
        var en = new LocalizationService("en");
        var dictionaries = typeof(LocalizationService).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(IReadOnlyDictionary<string, string>))
            .Select(field => (IReadOnlyDictionary<string, string>)field.GetValue(null)!).ToArray();
        Check(dictionaries.Length == 2 && dictionaries[0].Keys.ToHashSet().SetEquals(dictionaries[1].Keys), "languages have different keys");
        Check(en.AppName == "Sweepable’r" && tr.AppName == "Süpürücü", "brand names are wrong");
        Check(dictionaries.All(dictionary => dictionary.Values.All(value => !string.IsNullOrWhiteSpace(value))), "a translation is blank");
        var failed = new UpdaterMaintenanceResult(new[] { new UpdaterMaintenanceCheck("test", null, false, "", "Could not start") });
        Check(failed.Summary.Contains("error", StringComparison.OrdinalIgnoreCase), "failed maintenance was reported as a missing tool");
        var tools = UpdaterMaintenanceService.ParseChocolateySelfPackages("chocolatey|2.5.0|2.5.0|false\nchocolatey-core.extension|1.0.0|1.1.0|false\ngit|1.0.0|2.0.0|false");
        Check(tools.SequenceEqual(new[] { "chocolatey-core.extension" }), "tool maintenance included a current or unrelated package");
        return Task.CompletedTask;
    }

    private static UpdateCandidateViewModel Candidate(string id, long? size, string provider = "github") =>
        new(new CatalogEntry { Id = id, Name = id, Provider = provider, OfficialDomains = new() { "example.com" } }, new InstalledApp { DisplayName = id, DisplayVersion = "1" })
        { DownloadUrl = $"https://example.com/{id}.exe", LatestVersion = "2", DownloadSizeBytes = size };

    private static string Scratch()
    {
        var root = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class GatedHandler : HttpMessageHandler
    {
        public List<string> Started { get; } = new();
        public TaskCompletionSource ThreeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Started)
            {
                Started.Add(Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath));
                if (Started.Count == 3) ThreeStarted.TrySetResult();
            }
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }), RequestMessage = request };
        }
    }
    private sealed class BytesHandler(byte[] bytes, long? advertisedSize = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(bytes);
            if (advertisedSize is not null) content.Headers.ContentLength = advertisedSize;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
    private sealed class StallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallStream()), RequestMessage = request });
    }
    private sealed class StallStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
