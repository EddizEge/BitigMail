using System.Net;
using System.Net.Sockets;
using System.Text;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Pop;
using BitigMail.LocalHost.Security;
using BitigMail.LocalHost.Jobs;
using BitigMail.Engine.Models;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class PopSnapshotTests
{
    [Fact]
    public async Task OwnedLocalPopServerDownloadsTwelveMessagesWithoutDeleAndPreservesDuplicates()
    {
        var messages = Enumerable.Range(1, 12).Select(CreateMessage).ToArray();
        await using var server = new SyntheticPopServer(messages); await server.StartAsync(); string root = Temp();
        var store = new PopAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector());
        var account = store.Create(new("company", "project", "Local POP", "user@example.test", "127.0.0.1", server.Port, "none", "user", "password", AllowUnencryptedConnection: true));
        Assert.DoesNotContain("password", File.ReadAllText(Directory.GetFiles(Path.Combine(root, "accounts"), "*.json").Single()), StringComparison.Ordinal);
        var service = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity());
        var plan = await service.PreviewAsync(account.AccountId, "company", "project", CancellationToken.None); Assert.Equal(12, plan.Items.Count);
        string output = Path.Combine(root, "output"); Directory.CreateDirectory(output); var report = await service.DownloadAsync(plan, output, "test", CancellationToken.None);
        Assert.Equal(12, report.WrittenItems); Assert.Equal(12, Directory.GetFiles(report.OutputPath, "*.eml", SearchOption.AllDirectories).Length);
        Assert.Equal(0, server.DeleCommands); Assert.Equal(12, server.MessageCount); Assert.Equal(12, server.Uids.Count);
        Assert.Equal(12, report.Items.Select(x => x.Sha256).Distinct(StringComparer.Ordinal).Count());
        var expectedHashes = messages.Select(bytes => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedHashes, report.Items.Select(x => x.Sha256).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(4, Directory.GetFiles(report.OutputPath, "*.eml", SearchOption.AllDirectories).Sum(path => MimeKit.MimeMessage.Load(path).Attachments.Count()));
        Assert.All(report.Items, item => Assert.Contains("POP_INTERNALDATE_AND_READFLAG_UNAVAILABLE", item.MetadataQualification));
    }

    [Fact]
    public void SnapshotValidationRejectsMissingDuplicateAndReorderedIdentity()
    {
        Assert.Throws<InvalidDataException>(() => PopSnapshotService.ValidateSnapshot(["", "b"], [1, 2]));
        Assert.Throws<InvalidDataException>(() => PopSnapshotService.ValidateSnapshot(["a", "a"], [1, 2]));
        var plan = new[] { new PopSnapshotItem(1, "a", 1), new PopSnapshotItem(2, "b", 2) };
        Assert.False(PopSnapshotService.SnapshotMatches(plan, ["b", "a"], [2, 1]));
        Assert.False(PopSnapshotService.SnapshotMatches(plan, ["a"], [1]));
        Assert.False(PopSnapshotService.SnapshotMatches(plan, ["a", "b", "c"], [1, 2, 3]));
    }

    [Fact]
    public async Task ChangedUidlPlanFailsClosedWithoutFinalPublicationOrDeletion()
    {
        var messages = Enumerable.Range(1, 3).Select(CreateMessage).ToArray(); await using var server = new SyntheticPopServer(messages) { ReorderAfterFirstRetrieval = true }; await server.StartAsync(); string root = Temp();
        var store = new PopAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector(), true);
        var account = store.Create(new("company", "project", "Local POP", "user@example.test", "127.0.0.1", server.Port, "none", "user", "password"));
        var service = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity()); var plan = await service.PreviewAsync(account.AccountId, "company", "project", CancellationToken.None);
        string output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(plan, output, "changed", CancellationToken.None));
        Assert.Empty(Directory.GetDirectories(output, "POP-snapshot-*")); Assert.Equal(0, server.DeleCommands);
        server.ReorderAfterFirstRetrieval = false;
        var restarted = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity());
        var restoredPlan = restarted.GetPlan(plan.PlanId, "company", "project");
        var completed = await restarted.DownloadAsync(restoredPlan, output, "resume", CancellationToken.None);
        Assert.Equal(3, completed.WrittenItems); Assert.Equal(3, Directory.GetFiles(completed.OutputPath, "*.eml", SearchOption.AllDirectories).Length); Assert.Equal(0, server.DeleCommands);
    }

    [Fact]
    public void ProductionPopStoreRejectsPlaintextTransport()
    {
        string root = Temp(); var store = new PopAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector());
        Assert.Throws<ArgumentException>(() => store.Create(new("company", "project", "POP", "u@example.test", "127.0.0.1", 110, "none", "u", "p")));
        Assert.Throws<InvalidDataException>(() => PopSnapshotService.ValidateMessageCount(PopSnapshotService.MaxMessageCount + 1));
    }

    [Fact]
    public async Task AuthenticationFailureIsExplicitAndNeverDeletes()
    {
        await using var server = new SyntheticPopServer([CreateMessage(1)]) { ExpectedPassword = "correct" }; await server.StartAsync(); string root = Temp();
        var store = new PopAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector(), true);
        var account = store.Create(new("company", "project", "POP", "u@example.test", "127.0.0.1", server.Port, "none", "u", "wrong"));
        var service = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity());
        await Assert.ThrowsAnyAsync<MailKit.Security.AuthenticationException>(() => service.PreviewAsync(account.AccountId, "company", "project", CancellationToken.None)); Assert.Equal(0, server.DeleCommands);
    }

    [Fact]
    public async Task PopSnapshotRunsThroughGlobalQueueWithImmutableOwnerAndQualifiedReport()
    {
        await using var server = new SyntheticPopServer(Enumerable.Range(1, 2).Select(CreateMessage).ToArray()); await server.StartAsync(); string root = Temp();
        var store = new PopAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector(), true);
        var account = store.Create(new("company", "project", "POP", "u@example.test", "127.0.0.1", server.Port, "none", "u", "p"));
        var service = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity()); var plan = await service.PreviewAsync(account.AccountId, "company", "project", CancellationToken.None);
        string output = Path.Combine(root, "output"); Directory.CreateDirectory(output); var manager = new JobManager(Path.Combine(root, "runtime"), new UnlimitedCapacity());
        var owner = new ClientProjectContext { CompanyId = "company", ProjectId = "project", CompanyName = "Original" };
        var started = manager.StartPopSnapshotJob(plan, output, "idempotent-pop", owner, service, false); owner.CompanyName = "mutated";
        LocalJobRecord? done = null; for (int i = 0; i < 200; i++) { done = manager.GetJob(started.JobId); if (done?.Status is "completed" or "failed") break; await Task.Delay(25); }
        Assert.Equal("completed", done?.Status); Assert.Equal("Original", done?.ClientContext.CompanyName); Assert.Equal(2, done?.ItemsWritten);
        var report = manager.GetReport(started.JobId); Assert.NotNull(report); Assert.True(report!.ConversionSuccess); Assert.Contains("POP_SERVER_METADATA_UNAVAILABLE", report.FidelityStatus); Assert.Equal(0, server.DeleCommands);
    }

    [Fact]
    public async Task ResumeReconcilesCrashAfterEmlCommitAndBeforeDirectoryPublication()
    {
        await using var server = new SyntheticPopServer(Enumerable.Range(1, 2).Select(CreateMessage).ToArray()); await server.StartAsync(); string root = Temp();
        var store = new PopAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector(), true); var account = store.Create(new("company", "project", "POP", "u@example.test", "127.0.0.1", server.Port, "none", "u", "p"));
        var crashing = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity()) { AfterEmlCommitHook = ordinal => { if (ordinal == 1) throw new IOException("crash after eml commit"); } };
        var plan = await crashing.PreviewAsync(account.AccountId, "company", "project", CancellationToken.None); string output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        await Assert.ThrowsAsync<IOException>(() => crashing.DownloadAsync(plan, output, "crash-one", CancellationToken.None));
        var resumed = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity()) { BeforePublicationHook = () => throw new IOException("crash before move") };
        await Assert.ThrowsAsync<IOException>(() => resumed.DownloadAsync(resumed.GetPlan(plan.PlanId, "company", "project"), output, "crash-two", CancellationToken.None));
        var finalService = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity()); var report = await finalService.DownloadAsync(finalService.GetPlan(plan.PlanId, "company", "project"), output, "resume", CancellationToken.None);
        Assert.Equal(2, report.WrittenItems); Assert.Equal(2, Directory.GetFiles(report.OutputPath, "*.eml", SearchOption.AllDirectories).Length); Assert.Equal(0, server.DeleCommands);
    }

    [Fact]
    public async Task PublishedFastPathRejectsTamperedAndMissingEml()
    {
        await using var server = new SyntheticPopServer(Enumerable.Range(1, 2).Select(CreateMessage).ToArray()); await server.StartAsync(); string root = Temp();
        var store = new PopAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector(), true); var account = store.Create(new("company", "project", "POP", "u@example.test", "127.0.0.1", server.Port, "none", "u", "p"));
        var service = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity()); var plan = await service.PreviewAsync(account.AccountId, "company", "project", CancellationToken.None);
        string tamperedRoot = Path.Combine(root, "tampered"), missingRoot = Path.Combine(root, "missing"); Directory.CreateDirectory(tamperedRoot); Directory.CreateDirectory(missingRoot);
        var tampered = await service.DownloadAsync(plan, tamperedRoot, "first", CancellationToken.None); File.AppendAllText(Path.Combine(tampered.OutputPath, tampered.Items[0].RelativePath), "tamper");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(plan, tamperedRoot, "tampered", CancellationToken.None));
        var missing = await service.DownloadAsync(plan, missingRoot, "second", CancellationToken.None); File.Delete(Path.Combine(missing.OutputPath, missing.Items[0].RelativePath));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(plan, missingRoot, "missing", CancellationToken.None));
    }

    [Fact]
    public async Task ResumeRejectsAndPreservesUnknownTempFile()
    {
        await using var server = new SyntheticPopServer(Enumerable.Range(1, 2).Select(CreateMessage).ToArray()); await server.StartAsync(); string root = Temp();
        var store = new PopAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector(), true); var account = store.Create(new("company", "project", "POP", "u@example.test", "127.0.0.1", server.Port, "none", "u", "p"));
        var crashing = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity()) { AfterEmlCommitHook = _ => throw new IOException("crash") };
        var plan = await crashing.PreviewAsync(account.AccountId, "company", "project", CancellationToken.None); string output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        await Assert.ThrowsAsync<IOException>(() => crashing.DownloadAsync(plan, output, "crash", CancellationToken.None));
        string unknown = Path.Combine(output, ".pop-" + plan.PlanId + ".partial", "POP", "unknown.eml.tmp"); File.WriteAllText(unknown, "must remain");
        var resumed = new PopSnapshotService(store, new MailKitPopSnapshotClientFactory(), new UnlimitedCapacity()); await Assert.ThrowsAsync<InvalidDataException>(() => resumed.DownloadAsync(resumed.GetPlan(plan.PlanId, "company", "project"), output, "resume", CancellationToken.None));
        Assert.True(File.Exists(unknown));
    }

    private static byte[] CreateMessage(int i)
    {
        string headers = $"From: sender@example.test\r\nTo: receiver@example.test\r\nSubject: POP {i}\r\nMessage-Id: <same@example.test>\r\nDate: Mon, 1 Jan 2024 00:00:00 +0000\r\n";
        string body = i <= 4
            ? "Content-Type: multipart/mixed; boundary=pop-boundary\r\n\r\n--pop-boundary\r\nContent-Type: text/plain\r\n\r\nbody\r\n--pop-boundary\r\nContent-Type: application/octet-stream; name=file.bin\r\nContent-Disposition: attachment; filename=file.bin\r\nContent-Transfer-Encoding: base64\r\n\r\nAQID\r\n--pop-boundary--\r\n"
            : $"\r\nbody-{i}\r\n";
        return Encoding.ASCII.GetBytes(headers + body);
    }

    private static string Temp() { string path = Path.Combine(Path.GetTempPath(), "bitigmail-pop", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private sealed class UnlimitedCapacity : IDiskCapacityProbe { public DiskCapacityResult Probe(string directoryPath) => new(true, long.MaxValue, null); }

    private sealed class SyntheticPopServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly byte[][] _messages; private CancellationTokenSource _stop = new(); private Task? _loop;
        public int DeleCommands; public int MessageCount => _messages.Length; public IReadOnlyList<string> Uids { get; }
        public bool ReorderAfterFirstRetrieval { get; set; } private int _retrievals;
        public string? ExpectedPassword { get; init; }
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public SyntheticPopServer(byte[][] messages) { _messages = messages; Uids = messages.Select((_, i) => "uid-" + (i + 1)).ToArray(); }
        public Task StartAsync() { _listener.Start(); _loop = Task.Run(AcceptLoop); return Task.CompletedTask; }
        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient socket; try { socket = await _listener.AcceptTcpClientAsync(_stop.Token); } catch { break; }
                _ = Task.Run(() => Serve(socket));
            }
        }
        private async Task Serve(TcpClient socket)
        {
            using (socket) using (var stream = socket.GetStream()) using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true)) using (var writer = new StreamWriter(stream, Encoding.ASCII, 4096, true) { NewLine = "\r\n", AutoFlush = true })
            {
                await writer.WriteLineAsync("+OK BitigMail synthetic POP"); string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    string[] parts = line.Split(' ', 2); string command = parts[0].ToUpperInvariant(); string arg = parts.Length > 1 ? parts[1] : "";
                    if (command == "CAPA") { await writer.WriteLineAsync("+OK"); await writer.WriteLineAsync("USER"); await writer.WriteLineAsync("UIDL"); await writer.WriteLineAsync("."); }
                    else if (command == "USER") await writer.WriteLineAsync("+OK");
                    else if (command == "PASS") await writer.WriteLineAsync(ExpectedPassword is null || arg == ExpectedPassword ? "+OK" : "-ERR authentication failed");
                    else if (command == "STAT") await writer.WriteLineAsync($"+OK {_messages.Length} {_messages.Sum(x => x.Length)}");
                    else if (command == "UIDL") { var current = ReorderAfterFirstRetrieval && Volatile.Read(ref _retrievals) > 0 ? Uids.Reverse().ToArray() : Uids; await writer.WriteLineAsync("+OK"); for (int i = 0; i < _messages.Length; i++) await writer.WriteLineAsync($"{i + 1} {current[i]}"); await writer.WriteLineAsync("."); }
                    else if (command == "LIST") { await writer.WriteLineAsync("+OK"); for (int i = 0; i < _messages.Length; i++) await writer.WriteLineAsync($"{i + 1} {_messages[i].Length}"); await writer.WriteLineAsync("."); }
                    else if (command == "RETR" && int.TryParse(arg, out int number) && number >= 1 && number <= _messages.Length)
                    { Interlocked.Increment(ref _retrievals); await writer.WriteLineAsync($"+OK {_messages[number - 1].Length} octets"); var lines = Encoding.ASCII.GetString(_messages[number - 1]).Split("\r\n"); int count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length; for (int i = 0; i < count; i++) await writer.WriteLineAsync(lines[i].StartsWith('.') ? "." + lines[i] : lines[i]); await writer.WriteLineAsync("."); }
                    else if (command == "DELE") { Interlocked.Increment(ref DeleCommands); await writer.WriteLineAsync("-ERR deletion forbidden"); }
                    else if (command == "QUIT") { await writer.WriteLineAsync("+OK bye"); break; }
                    else if (command == "NOOP") await writer.WriteLineAsync("+OK"); else await writer.WriteLineAsync("-ERR unsupported");
                }
            }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); if (_loop is not null) try { await _loop; } catch { } _stop.Dispose(); }
    }
}
