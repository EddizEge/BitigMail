using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Execution;
using BitigMail.Engine.Recovery;
using BitigMail.Engine.Storage;
using MimeKit;
using Xunit;
using Xunit.Abstractions;

namespace BitigMail.Engine.Tests;
public sealed class Task036RecoveryWorkerOracleTests(ITestOutputHelper log)
{
    [Fact] public async Task HealthyFixtureMatchesIndependentFolderDateAndAttachmentOracle()
    {
        using var fixture = new OracleFixture(); string before = DamagedStoreResultValidator.HashFile(fixture.Healthy);
        var (process, request, result) = await fixture.RunAsync(fixture.Healthy);
        Assert.Equal("completed", process.Outcome);
        var verified = new DamagedStoreResultValidator().Validate(request, result, fixture.Healthy);
        Assert.Equal("healthy_extraction", verified.Outcome); Assert.Equal(fixture.Expected.Count, verified.RecoveredCount);
        fixture.AssertMessages(request.OutputRoot, verified.Messages);
        Assert.Equal(before, DamagedStoreResultValidator.HashFile(fixture.Healthy));
    }

    [Fact] public async Task ControlledBlockDamageProducesVerifiedPartialAndExplicitUnreadable()
    {
        using var fixture = new OracleFixture(); byte[] original = File.ReadAllBytes(fixture.Healthy);
        string originalHash = DamagedStoreResultValidator.HashFile(fixture.Healthy);
        // Bounded discovery is recorded in root036-controlled-damage-probe.trx. Freeze that mutation here.
        const int offset = 28672, length = 512;
        Assert.True(original.Length > offset + length);
        byte[] bytes = original.ToArray(); Array.Fill<byte>(bytes, 0xff, offset, length);
        string damaged = Path.Combine(fixture.Root, "damaged-block.pst"); File.WriteAllBytes(damaged, bytes);
        string damagedHash = DamagedStoreResultValidator.HashFile(damaged);
        var (process, request, result) = await fixture.RunAsync(damaged);
        Assert.Equal("completed", process.Outcome); Assert.True(process.TerminationConfirmed);
        var partial = new DamagedStoreResultValidator().Validate(request, result, damaged);
        Assert.Equal("partial_recovered", partial.Outcome); Assert.True(partial.RecoveredCount >= 1);
        Assert.True(partial.FailedBoundaryCount >= 1); Assert.Null(partial.OriginalTotal);
        fixture.AssertMessages(request.OutputRoot, partial.Messages);
        Assert.Equal(damagedHash, DamagedStoreResultValidator.HashFile(damaged));
        var qualification = new NormalizedSourceQualificationReader().Read(request.OutputRoot)!;
        Assert.True(qualification.IsPartial); Assert.True(qualification.DateFilterBlocked);
        Assert.Equal(partial.RecoveredCount, qualification.VerifiedOutputCount);
        log.WriteLine($"Mutation offset={offset}, bytes={length}; recovered={partial.RecoveredCount}; failed boundaries={partial.FailedBoundaryCount}; original total unknown.");

        string unreadable = Path.Combine(fixture.Root, "unreadable.pst"); File.WriteAllBytes(unreadable, original[..4096]);
        string unreadableHash = DamagedStoreResultValidator.HashFile(unreadable);
        var (failedProcess, failedRequest, failedResult) = await fixture.RunAsync(unreadable);
        Assert.Equal("completed", failedProcess.Outcome);
        var failure = new DamagedStoreResultValidator().Validate(failedRequest, failedResult, unreadable);
        Assert.Equal("unreadable_source", failure.Outcome); Assert.Equal(0, failure.RecoveredCount);
        Assert.True(failure.FailedBoundaryCount >= 1); Assert.Null(failure.OriginalTotal);
        Assert.Equal(unreadableHash, DamagedStoreResultValidator.HashFile(unreadable));
        Assert.Equal(originalHash, DamagedStoreResultValidator.HashFile(fixture.Healthy));
    }

    [Fact] public async Task NonMailIsExcludedAndRecoveryQualificationRejectsTamperedReuse()
    {
        using var fixture = new OracleFixture();
        using (var store = PersonalStorage.FromFile(fixture.Healthy))
        using (var contact = new MapiMessage("a@example.test", "b@example.test", "not-mail", "contact") { MessageClass = "IPM.Contact" })
            store.RootFolder.AddMessage(contact);
        var (process, request, result) = await fixture.RunAsync(fixture.Healthy);
        Assert.Equal("completed", process.Outcome);
        var verified = new DamagedStoreResultValidator().Validate(request, result, fixture.Healthy);
        Assert.Equal(1, verified.NonMailItemCount); Assert.Equal(4, verified.RecoveredCount);
        fixture.AssertMessages(request.OutputRoot, verified.Messages);
        string message = Path.Combine(request.OutputRoot, verified.Messages[0].RelativePath);
        var qualification = new NormalizedSourceQualificationReader().Read(message)!;
        Assert.True(qualification.DateFilterBlocked);
        File.AppendAllText(message, "changed");
        Assert.Throws<InvalidDataException>(() => new NormalizedSourceQualificationReader().Read(message));
    }

    private sealed class OracleFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "bitigmail-036-oracle-" + Guid.NewGuid().ToString("N"));
        public string Healthy { get; }
        public Dictionary<string, (string Folder, DateTime Date, string AttachmentSha)> Expected { get; } = [];
        public OracleFixture()
        {
            Directory.CreateDirectory(Root); Healthy = Path.Combine(Root, "known.pst");
            using var store = PersonalStorage.Create(Healthy, FileFormatVersion.Unicode);
            var inbox = store.CreatePredefinedFolder("Inbox", StandardIpmFolder.Inbox);
            var archive = store.RootFolder.AddSubFolder("Archive-2024");
            Add(inbox, "known-inbox-1", new DateTime(2024,1,2,3,4,5,DateTimeKind.Utc), "payload-a");
            Add(inbox, "known-inbox-2", new DateTime(2024,2,3,4,5,6,DateTimeKind.Utc), "payload-b");
            Add(archive, "known-archive-1", new DateTime(2023,3,4,5,6,7,DateTimeKind.Utc), "payload-c");
            Add(archive, "known-archive-2", new DateTime(2022,4,5,6,7,8,DateTimeKind.Utc), "payload-d");
        }
        private void Add(FolderInfo folder, string subject, DateTime date, string payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            using var message = new MapiMessage("sender@example.test", "receiver@example.test", subject, "known-body") { ClientSubmitTime = date, DeliveryTime = date };
            message.Attachments.Add(subject + ".txt", bytes); folder.AddMessage(message);
            Expected[subject] = (folder.DisplayName, date, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        public void AssertMessages(string output, IReadOnlyList<RecoveredMimeFile> messages)
        {
            foreach (var item in messages)
            {
                Assert.Matches("^[0-9a-f]{64}$", item.FolderEntryIdSha256!);
                Assert.Matches("^[0-9a-f]{64}$", item.SourceEntryIdSha256!);
                Assert.Equal("LICENSED_OUTPUT_ACCEPTANCE_PENDING", item.SdkQualification);
                using var mime = MimeMessage.Load(Path.Combine(output, item.RelativePath));
                string subject = Assert.Single(Expected.Keys, key => mime.Subject == key || mime.Subject == key + "(Aspose.Email Evaluation)");
                var expected = Expected[subject]; Assert.Equal(expected.Folder, item.FolderDisplayName);
                Assert.EndsWith(expected.Folder, item.FolderPath!); Assert.Equal(expected.Date, mime.Date.UtcDateTime);
                Assert.Contains("known-body", mime.TextBody); // Trial body additions are disclosed, never stripped from output.
                var attachment = Assert.IsType<MimePart>(Assert.Single(mime.Attachments));
                Assert.Equal(subject + ".txt", attachment.FileName);
                using var payload = new MemoryStream(); attachment.Content!.DecodeTo(payload);
                Assert.Equal(expected.AttachmentSha, Convert.ToHexString(SHA256.HashData(payload.ToArray())).ToLowerInvariant());
            }
        }
        public async Task<(OwnedWorkerResult, DamagedStoreWorkerRequest, string)> RunAsync(string source)
        {
            var timeout = TimeSpan.FromSeconds(30); string invocation = Guid.NewGuid().ToString("N"), output = Path.Combine(Root, "run-" + invocation);
            Directory.CreateDirectory(output); string requestPath = Path.Combine(output, "request.json"), result = Path.Combine(output, "bitigmail-recovery-manifest.json");
            var request = new DamagedStoreWorkerRequest(1, invocation, "job-oracle", source, DamagedStoreResultValidator.HashFile(source), output, 100, 100, 16, DateTime.UtcNow.Add(timeout).Ticks);
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request)); string repo = Directory.GetCurrentDirectory();
            while (!Directory.Exists(Path.Combine(repo, "engine"))) repo = Directory.GetParent(repo)!.FullName;
            string executable = Path.Combine(repo, "engine", "BitigMail.RecoveryWorker", "bin", "Release", "net8.0-windows", "BitigMail.RecoveryWorker.exe");
            var process = await new OwnedWorkerProcessRunner().RunAsync(executable, [requestPath, result], output, timeout);
            File.Delete(requestPath); return (process, request, result);
        }
        public void Dispose()
        {
            string full = Path.GetFullPath(Root);
            if (!full.StartsWith(Path.Combine(Path.GetTempPath(), "bitigmail-036-oracle-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            Directory.Delete(full, true); // Exclusively owned generated fixture tree; no user input or links.
        }
    }
}
