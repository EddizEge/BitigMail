using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using Xunit;

namespace BitigMail.Engine.Tests;

public class FidelityAndMultisetVerificationTests
{
    private static string ResolveApprovedFixturePath()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "lab", "ost-spike", "input", "bitigmail-lab-full.ost");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "lab", "ost-spike", "input", "bitigmail-lab-full.ost");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new FileNotFoundException("Approved test fixture 'lab/ost-spike/input/bitigmail-lab-full.ost' was not found.");
    }

    [Fact]
    public void MultisetMatching_SameSubjectAndDuplicateOrBlankMessageId_MatchedConsumingly()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-multi-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("TestFolder");

                // Message 1: Same subject, BLANK MessageId, Date A, Body A
                var msg1 = new MapiMessage("user@test.com", "dest@test.com", "Ortak Rapor", "İçerik A");
                folder.AddMessage(msg1);

                // Message 2: Same subject, DUPLICATE MessageId "<dup@company.com>", Date B, Body B
                var msg2 = new MapiMessage("user@test.com", "dest@test.com", "Ortak Rapor", "İçerik B");
                msg2.SetProperty(KnownPropertyList.InternetMessageId, "<dup@company.com>");
                folder.AddMessage(msg2);

                // Message 3: Same subject, DUPLICATE MessageId "<dup@company.com>", Date C, Body C
                var msg3 = new MapiMessage("user@test.com", "dest@test.com", "Ortak Rapor", "İçerik C");
                msg3.SetProperty(KnownPropertyList.InternetMessageId, "<dup@company.com>");
                folder.AddMessage(msg3);
            }

            var converter = new OstToPstConverter();
            var snapshots = new List<OstToPstConverter.SourceItemSnapshot>();

            using (var stream = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var openedPst = PersonalStorage.FromStream(stream))
            {
                var folder = openedPst.RootFolder.GetSubFolder("TestFolder");
                var msgInfos = folder.EnumerateMessages().ToList();
                Assert.Equal(3, msgInfos.Count);

                foreach (var mi in msgInfos)
                {
                    using var msg = openedPst.ExtractMessage(mi);
                    snapshots.Add(OstToPstConverter.BuildItemSnapshot(msg, mi, "TestFolder"));
                }
            }

            // Production consuming multiset verification must succeed with 3 items matched
            var verification = converter.VerifyReopenedPst(tempPst, snapshots);
            Assert.True(verification.VerificationSuccess);
            Assert.Equal(3, verification.TotalPhysicalItemsFound);
            Assert.Empty(verification.VerificationNotes);

            // Negative control: If snapshots only contains 2 items (e.g. Msg1 omitted),
            // verifying against a PST with 3 items must fail due to count mismatch and unconsumed pool item
            var incompleteSnapshots = snapshots.Take(2).ToList();
            var failedVerification = converter.VerifyReopenedPst(tempPst, incompleteSnapshots);
            Assert.False(failedVerification.VerificationSuccess);
            Assert.Contains("Toplam öğe sayısı uyuşmazlığı", failedVerification.VerificationNotes[0]);
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
        }
    }

    [Fact]
    public void OstAnalyzer_CountsAttachmentsAcrossAllItems_Beyond10thSample()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-att-count-{Guid.NewGuid():N}.pst");
        try
        {
            // Create a store with 15 messages, each having 1 attachment
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("TopluEkler");
                for (int i = 1; i <= 15; i++)
                {
                    var msg = new MapiMessage("sender@test.com", "dest@test.com", $"İleti {i}", $"Gövde {i}");
                    msg.Attachments.Add($"ek_{i}.txt", Encoding.UTF8.GetBytes($"Ek içerik {i}"));
                    folder.AddMessage(msg);
                }
            }

            // Invoke real production OstAnalyzer analysis traversal
            var analyzer = new OstAnalyzer();
            using (var stream = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var storage = PersonalStorage.FromStream(new NonClosingStream(stream)))
            {
                var analysis = analyzer.AnalyzeStorageCore(
                    storage,
                    fileName: "test-att.ost",
                    sizeBytes: new FileInfo(tempPst).Length,
                    sha256: "dummy-hash");

                // Production OstAnalyzer must count all 15 attachments across all items, while sample messages are capped at 10
                Assert.Equal(15, analysis.TotalItems);
                Assert.Equal(15, analysis.TotalAttachments);
                Assert.Equal(10, analysis.SampleMessages.Count); // Verified capped at 10
                Assert.True(analysis.TotalAttachments > analysis.SampleMessages.Count);
            }
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
        }
    }

    [Fact]
    public void Storage_PhysicalItemsAtRoot_AreEnumeratedAndPreserved()
    {
        string tempSource = Path.Combine(Path.GetTempPath(), $"source-root-items-{Guid.NewGuid():N}.pst");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target-root-items-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempSource, FileFormatVersion.Unicode))
            {
                // Add message directly to root folder
                var rootMsg = new MapiMessage("root@test.com", "admin@test.com", "Kök Dizin İletisi", "Kök ileti gövdesi");
                pst.RootFolder.AddMessage(rootMsg);

                // Add subfolder with message
                var subFolder = pst.RootFolder.AddSubFolder("AltKlasor");
                var subMsg = new MapiMessage("sub@test.com", "admin@test.com", "Alt Klasör İletisi", "Alt klasör gövdesi");
                subFolder.AddMessage(subMsg);
            }

            // 1. Verify production OstAnalyzer discovers root physical items
            var analyzer = new OstAnalyzer();
            using (var srcStream = new FileStream(tempSource, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var srcStorage = PersonalStorage.FromStream(new NonClosingStream(srcStream)))
            {
                var analysis = analyzer.AnalyzeStorageCore(
                    srcStorage,
                    fileName: "root-test.ost",
                    sizeBytes: new FileInfo(tempSource).Length,
                    sha256: "dummy-hash");

                Assert.Equal(2, analysis.TotalItems);
                var rootFolderSummary = analysis.Folders.FirstOrDefault(f => f.FolderPath == "[Kök Klasör]");
                Assert.NotNull(rootFolderSummary);
                Assert.Equal(1, rootFolderSummary.ItemCount);
                Assert.Equal("Active", rootFolderSummary.Category);
            }

            // 2. Verify production OstToPstConverter preserves root physical items
            var converter = new OstToPstConverter();
            var snapshots = new List<OstToPstConverter.SourceItemSnapshot>();
            int totalRead = 0;
            int totalWritten = 0;
            int failedItems = 0;

            using (var srcStream = new FileStream(tempSource, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var srcStorage = PersonalStorage.FromStream(new NonClosingStream(srcStream)))
            using (var tgtStorage = PersonalStorage.Create(tempTarget, FileFormatVersion.Unicode))
            {
                // Execute production ProcessFolderItems on RootFolder
                converter.ProcessFolderItems(
                    srcStorage,
                    srcStorage.RootFolder,
                    tgtStorage.RootFolder,
                    folderPath: "[Kök Klasör]",
                    ref totalRead,
                    ref totalWritten,
                    ref failedItems,
                    snapshots,
                    CancellationToken.None);

                Assert.Equal(1, totalRead);
                Assert.Equal(1, totalWritten);
                Assert.Equal(0, failedItems);
                Assert.Single(snapshots);
                Assert.Equal("[Kök Klasör]", snapshots[0].FolderPath);
                Assert.StartsWith("Kök Dizin İletisi", snapshots[0].Subject);
            }

            // 3. Verify production VerifyReopenedPst validates root items
            var verification = converter.VerifyReopenedPst(tempTarget, snapshots);
            Assert.True(verification.VerificationSuccess);
            Assert.Equal(1, verification.TotalPhysicalItemsFound);
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void VerificationFailure_ModifiedBody_FailsVerificationAndDoesNotPublishFinalPst()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target-fail-body-{Guid.NewGuid():N}.pst");
        try
        {
            var converter = new OstToPstConverter();
            var context = new ClientProjectContext { CompanyName = "Test Co", ProjectName = "Test Proj" };

            // Inject modified body hash right before verification
            converter.BeforeVerificationHook = (partialPath, snapshots) =>
            {
                Assert.NotEmpty(snapshots);
                snapshots[0].NormalizedBodySha256 = "0000000000000000000000000000000000000000000000000000000000000000";
            };

            var report = converter.Convert(fixturePath, tempTarget, "job-mod-body", context);

            // Final PST must NEVER be published
            Assert.False(File.Exists(tempTarget));
            Assert.False(report.ConversionSuccess);
            Assert.Equal("FAILED", report.OverallStatus);
            Assert.False(report.ReopenedPstVerification.VerificationSuccess);
            Assert.Contains(report.ReopenedPstVerification.VerificationNotes, n => n.Contains("hash uyuşmazlığı"));
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void VerificationFailure_MissingAttachment_FailsVerificationAndDoesNotPublishFinalPst()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target-fail-att-{Guid.NewGuid():N}.pst");
        try
        {
            var converter = new OstToPstConverter();
            var context = new ClientProjectContext { CompanyName = "Test Co", ProjectName = "Test Proj" };

            // Inject missing attachment in source snapshot right before verification
            converter.BeforeVerificationHook = (partialPath, snapshots) =>
            {
                var itemWithAtt = snapshots.FirstOrDefault(s => s.Attachments.Count > 0);
                Assert.NotNull(itemWithAtt);
                itemWithAtt.Attachments.Clear(); // Source had 0, but PST has attachments -> discrepancy!
            };

            var report = converter.Convert(fixturePath, tempTarget, "job-missing-att", context);

            // Final PST must NEVER be published
            Assert.False(File.Exists(tempTarget));
            Assert.False(report.ConversionSuccess);
            Assert.Equal("FAILED", report.OverallStatus);
            Assert.False(report.ReopenedPstVerification.VerificationSuccess);
            Assert.Contains(report.ReopenedPstVerification.VerificationNotes, n => n.Contains("Ek sayısı uyuşmazlığı") || n.Contains("fazladan ekler"));
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void VerificationFailure_ChangedContentId_FailsVerificationAndDoesNotPublishFinalPst()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target-fail-cid-{Guid.NewGuid():N}.pst");
        try
        {
            var converter = new OstToPstConverter();
            var context = new ClientProjectContext { CompanyName = "Test Co", ProjectName = "Test Proj" };

            // Inject changed Content-ID right before verification
            converter.BeforeVerificationHook = (partialPath, snapshots) =>
            {
                var itemWithCid = snapshots.FirstOrDefault(s => s.Attachments.Any(a => !string.IsNullOrEmpty(a.ContentId)));
                Assert.NotNull(itemWithCid);
                var att = itemWithCid.Attachments.First(a => !string.IsNullOrEmpty(a.ContentId));
                att.ContentId = "tampered_fake_cid_value";
            };

            var report = converter.Convert(fixturePath, tempTarget, "job-changed-cid", context);

            // Final PST must NEVER be published
            Assert.False(File.Exists(tempTarget));
            Assert.False(report.ConversionSuccess);
            Assert.Equal("FAILED", report.OverallStatus);
            Assert.False(report.ReopenedPstVerification.VerificationSuccess);
            Assert.Contains(report.ReopenedPstVerification.VerificationNotes, n => n.Contains("Content-ID uyuşmazlığı"));
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void AttachmentHelper_ReadsTask009ContentIdWithoutSynthesizing()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-cid-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("CidTest");
                var msg = new MapiMessage("test@test.com", "dest@test.com", "CID İletisi", "Gövde");

                msg.Attachments.Add("resim.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                var att = msg.Attachments[0];
                att.SetProperty(KnownPropertyList.AttachContentId, "<logo@company.com>");
                folder.AddMessage(msg);
            }

            using (var stream = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var opened = PersonalStorage.FromStream(stream))
            {
                var folder = opened.RootFolder.GetSubFolder("CidTest");
                var mi = folder.EnumerateMessages().First();
                using var msg = opened.ExtractMessage(mi);

                Assert.Single(msg.Attachments);
                string? cid = AttachmentHelper.GetAttachmentContentId(msg.Attachments[0]);

                Assert.NotNull(cid);
                Assert.Equal("logo@company.com", cid.Trim('<', '>'));
            }
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
        }
    }
}
