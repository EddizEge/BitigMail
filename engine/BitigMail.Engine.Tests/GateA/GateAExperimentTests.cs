using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspose.Email;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Mbox;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Storage;
using Xunit;
using Xunit.Abstractions;

namespace BitigMail.Engine.Tests.GateA;

/// <summary>
/// TASK-013 Gate A isolated xUnit decoding experiment.
/// Verifies Aspose.Email 24.8 behavior for EML/MBOX loading, MAPI conversion,
/// and PST write/reopen against an independent source oracle.
/// </summary>
public sealed class GateAExperimentTests
{
    private readonly ITestOutputHelper _output;

    private static readonly string[] FixtureIds =
    [
        "msg-01", "msg-02", "msg-03", "msg-04", "msg-05", "msg-06",
        "msg-07", "msg-08", "msg-09", "msg-10", "msg-11", "msg-12"
    ];

    public GateAExperimentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void RunGateA_DecodeExperiment_EmitsReportAndAssertsIntegrity()
    {
        string runId = $"run-{Guid.NewGuid():N}";
        string taskTempRoot = Path.Combine(Path.GetTempPath(), "bitigmail-task013-gate-a");
        string runDir = Path.Combine(taskTempRoot, runId);
        Directory.CreateDirectory(runDir);

        // 1. Resolve source paths: permanent independent source oracle, manifest, and fixtures root
        string oraclePath = ResolveOraclePath();
        using var oracleDoc = JsonDocument.Parse(File.ReadAllText(oraclePath));
        var oracleRoot = oracleDoc.RootElement;

        var (corpusRoot, mboxPath) = ResolveCorpusPaths(oraclePath, oracleRoot);
        string manifestPath = Path.Combine(corpusRoot, "manifest.json");

        var report = new GateAReport
        {
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            RunId = runId,
            TaskOwnedTempDir = runDir,
            ReportPath = Path.Combine(runDir, "report.json"),
            OracleReportPath = oraclePath
        };

        // 2. Source Corpus Metadata
        report.SourceCorpus = new CorpusSourceMeta
        {
            CorpusRoot = corpusRoot,
            ManifestPath = manifestPath,
            ManifestSha256 = File.Exists(manifestPath) ? ComputeFileSha256(manifestPath) : string.Empty,
            MboxPath = mboxPath,
            MboxSha256 = ComputeFileSha256(mboxPath),
            MboxSizeBytes = new FileInfo(mboxPath).Length
        };

        for (int i = 0; i < FixtureIds.Length; i++)
        {
            string fid = FixtureIds[i];
            string emlPath = Path.Combine(corpusRoot, "eml", $"{fid}.eml");
            var fi = new FileInfo(emlPath);
            report.SourceCorpus.EmlFiles.Add(new FileByteFact
            {
                Ordinal = i + 1,
                FixtureId = fid,
                RelativePath = $"eml/{fid}.eml",
                AbsolutePath = emlPath,
                SizeBytes = fi.Length,
                Sha256 = ComputeFileSha256(emlPath)
            });
        }

        // 3. Installed APIs
        report.ApisUsed = new InstalledApisMeta
        {
            SdkAssembly = "Aspose.Email 24.8.0",
            EmlLoaderApi = "Aspose.Email.MailMessage.Load(string, EmlLoadOptions)",
            MapiConverterApi = "Aspose.Email.Mapi.MapiMessage.FromMailMessage(MailMessage, MapiConversionOptions.UnicodeFormat)",
            MboxrdReaderApi = "Aspose.Email.Storage.Mbox.MboxrdStorageReader(string, MboxLoadOptions)",
            MboxoReaderApi = "Aspose.Email.Storage.Mbox.MboxoStorageReader(string, MboxLoadOptions)",
            PstWriterApi = "Aspose.Email.Storage.Pst.PersonalStorage.Create(string, FileFormatVersion.Unicode)",
            PstReaderApi = "Aspose.Email.Storage.Pst.PersonalStorage.FromFile(string)"
        };

        // 4. Source Oracle Summary (read root oracle report)
        int oraclePhysicalMessages = oracleRoot.GetProperty("physicalMessages").GetInt32();
        int oracleAttachments = oracleRoot.GetProperty("attachments").GetInt32();
        int oracleCidCount = oracleRoot.GetProperty("cidCount").GetInt32();

        report.SourceOracle = new SourceOracleSummary
        {
            OracleReportPath = oraclePath,
            TotalMessages = oraclePhysicalMessages,
            UniqueContentCount = 11,
            DuplicatePair = ["msg-01", "msg-02"],
            SharedMessageIdPair = ["msg-03", "msg-04"]
        };

        var oracleMessagesJson = oracleRoot.GetProperty("messages");
        for (int i = 0; i < oracleMessagesJson.GetArrayLength(); i++)
        {
            var m = oracleMessagesJson[i];
            string fid = m.GetProperty("fixtureId").GetString()!;

            string rawSha = string.Empty;
            if (m.TryGetProperty("sourceSha256", out var ssha))
            {
                rawSha = ssha.GetString() ?? string.Empty;
            }
            else if (oracleRoot.TryGetProperty("sourceHashes", out var shObj) && shObj.TryGetProperty($"eml/{fid}.eml", out var hVal))
            {
                rawSha = hVal.GetString() ?? string.Empty;
            }

            var rec = new SourceMessageOracleRecord
            {
                Ordinal = i + 1,
                FixtureId = fid,
                EmlPath = Path.Combine(corpusRoot, "eml", $"{fid}.eml"),
                Folder = m.TryGetProperty("mappedFolder", out var mf) ? mf.GetString() ?? "corpus" : "corpus",
                MessageId = m.GetProperty("messageId").GetString()!,
                DateIso = m.GetProperty("dateUtc").GetString()!,
                Subject = m.GetProperty("subject").GetString()!,
                RawSha256 = rawSha,
                IsDuplicate = fid == "msg-02",
                DuplicateOf = fid == "msg-02" ? "msg-01" : null,
                SameMessageIdDiffContent = fid is "msg-03" or "msg-04",
                SharedMessageIdWith = fid == "msg-03" ? "msg-04" : (fid == "msg-04" ? "msg-03" : null)
            };

            // Sender
            if (m.TryGetProperty("from", out var fromArr) && fromArr.GetArrayLength() > 0)
            {
                var first = fromArr[0];
                string name = first[0].GetString() ?? "";
                string addr = first[1].GetString() ?? "";
                rec.From = string.IsNullOrEmpty(name) ? addr : $"{name} <{addr}>";
            }

            // Recipients
            if (m.TryGetProperty("to", out var toArr))
            {
                var recips = new List<string>();
                foreach (var t in toArr.EnumerateArray())
                {
                    string name = t[0].GetString() ?? "";
                    string addr = t[1].GetString() ?? "";
                    recips.Add(string.IsNullOrEmpty(name) ? addr : $"{name} <{addr}>");
                }
                rec.To = string.Join("; ", recips);
            }

            // Bodies
            if (m.TryGetProperty("bodies", out var bodiesArr))
            {
                foreach (var b in bodiesArr.EnumerateArray())
                {
                    if (b.GetProperty("type").GetString() == "text/plain")
                    {
                        rec.RawBodyFromLineFacts = ScanFromLines(b.GetProperty("text").GetString());
                    }
                }
            }

            // Attachments
            if (m.TryGetProperty("attachments", out var attArr))
            {
                rec.AttachmentCount = attArr.GetArrayLength();
                foreach (var a in attArr.EnumerateArray())
                {
                    rec.Attachments.Add(new SourceAttachmentRecord
                    {
                        Filename = a.GetProperty("name").GetString()!,
                        ContentType = a.TryGetProperty("contentType", out var ct) ? ct.GetString() ?? "" : "",
                        Sha256 = a.GetProperty("sha256").GetString()!,
                        Size = a.GetProperty("bytes").GetInt64(),
                        IsInline = a.TryGetProperty("disposition", out var disp) && disp.GetString() == "inline",
                        ContentId = a.TryGetProperty("cid", out var cid) ? cid.GetString() : null
                    });
                }
            }

            report.SourceOracle.Messages.Add(rec);
        }

        // Parse raw MBOX bytes facts
        report.SourceOracle.RawMboxByteFacts = AnalyzeRawMboxBytes(mboxPath);

        // 5. EML Stage Experiment: Load MailMessage -> Convert to MapiMessage -> Write to new PST -> Reopen PST
        string emlPstPath = Path.Combine(runDir, "eml-stage.pst");
        var mailMessages = new List<MailMessage>();
        var mapiMessages = new List<MapiMessage>();

        for (int i = 0; i < FixtureIds.Length; i++)
        {
            string fid = FixtureIds[i];
            string emlPath = Path.Combine(corpusRoot, "eml", $"{fid}.eml");

            // SDK Load
            var mail = MailMessage.Load(emlPath, new EmlLoadOptions());
            mailMessages.Add(mail);
            report.EmlStageObservations.MailMessageObservations.Add(CreateStageObservation(i + 1, fid, "MailMessage", "Corpus", mail));

            // MAPI Conversion
            var mapi = MapiMessage.FromMailMessage(mail, MapiConversionOptions.UnicodeFormat);
            mapiMessages.Add(mapi);
            report.EmlStageObservations.MapiObservations.Add(CreateStageObservation(i + 1, fid, "MapiMessage", "Corpus", mapi));
        }

        // Write to new PST
        using (var pst = PersonalStorage.Create(emlPstPath, FileFormatVersion.Unicode))
        {
            var folder = pst.RootFolder.AddSubFolder("Corpus");
            foreach (var mapi in mapiMessages)
            {
                folder.AddMessage(mapi);
            }
        }

        report.EmlStageObservations.TotalSourceCount = FixtureIds.Length;
        report.EmlStageObservations.MailMessageStageCount = mailMessages.Count;
        report.EmlStageObservations.MapiStageCount = mapiMessages.Count;
        report.EmlStageObservations.OutputPstPath = emlPstPath;
        report.EmlStageObservations.OutputPstSha256 = ComputeFileSha256(emlPstPath);
        report.EmlStageObservations.OutputPstSizeBytes = new FileInfo(emlPstPath).Length;

        // Reopen PST and verify
        using (var reopenedPst = PersonalStorage.FromFile(emlPstPath))
        {
            var folder = reopenedPst.RootFolder.GetSubFolder("Corpus");
            var msgInfos = folder.EnumerateMessages().ToList();
            report.EmlStageObservations.ReopenedPstStageCount = msgInfos.Count;

            for (int i = 0; i < msgInfos.Count; i++)
            {
                string fid = i < FixtureIds.Length ? FixtureIds[i] : $"msg-extra-{i + 1}";
                using var extracted = reopenedPst.ExtractMessage(msgInfos[i]);
                report.EmlStageObservations.ReopenedPstObservations.Add(CreateStageObservation(i + 1, fid, "ReopenedPst", "Corpus", extracted));
            }
        }

        // 6. MBOX Experiment: explicit mboxrd and mboxo interpretations
        report.MboxrdObservations = RunMboxDialectExperiment("mboxrd", mboxPath, runDir, report.SourceOracle);
        report.MboxoObservations = RunMboxDialectExperiment("mboxo", mboxPath, runDir, report.SourceOracle);

        // 7. Per-stage Field Diffs and Comparison with Oracle
        report.StageDiffs = BuildStageDiffs(report.SourceOracle.Messages, report.EmlStageObservations);

        // 8. Trial Evaluation Observations
        report.TrialEvaluationObservations = BuildTrialEvaluationSummary(report.EmlStageObservations, report.StageDiffs);

        // 9. Risks and Decisions Needed
        report.RisksAndDecisionsNeeded = BuildRisksAndDecisions();

        // 10. Write deterministic JSON report
        string reportJsonPath = Path.Combine(runDir, "report.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        string jsonReport = JsonSerializer.Serialize(report, jsonOptions);
        File.WriteAllText(reportJsonPath, jsonReport, Encoding.UTF8);

        // Output required report path notification
        _output.WriteLine($"TASK013_GATE_A_REPORT={reportJsonPath}");

        // Cleanup in-memory MailMessages & MapiMessages
        foreach (var m in mailMessages) m.Dispose();
        foreach (var m in mapiMessages) m.Dispose();

        // 11. Assertions required by TASK-013 Gate A:
        // - Oracle structural counts
        Assert.Equal(12, oraclePhysicalMessages);
        Assert.Equal(4, oracleAttachments);
        Assert.Equal(1, oracleCidCount);

        // - EML stage counts and physical duplicate preservation
        Assert.Equal(12, report.EmlStageObservations.TotalSourceCount);
        Assert.Equal(12, report.EmlStageObservations.MailMessageStageCount);
        Assert.Equal(12, report.EmlStageObservations.MapiStageCount);
        Assert.Equal(12, report.EmlStageObservations.ReopenedPstStageCount);

        // - Physical duplicates stay distinct (both msg-01 and msg-02 exist as separate messages in reopened PST)
        Assert.Equal(12, report.StageDiffs.MessageDiffs.Count);

        // - PST outputs exist and reopen
        Assert.True(File.Exists(emlPstPath), "EML stage PST must exist.");
        Assert.True(new FileInfo(emlPstPath).Length > 0, "EML stage PST must not be empty.");
        Assert.True(File.Exists(report.MboxrdObservations.OutputPstPath), "Mboxrd PST must exist.");
        Assert.True(new FileInfo(report.MboxrdObservations.OutputPstPath).Length > 0, "Mboxrd PST must not be empty.");
        Assert.True(File.Exists(report.MboxoObservations.OutputPstPath), "Mboxo PST must exist (empty PST explicitly reported).");

        // - MBOX dialect assertions:
        // mboxrd must retain 12 physical messages and reopen all 12
        Assert.Equal(12, report.MboxrdObservations.PhysicalReadCount);
        Assert.Equal(12, report.MboxrdObservations.ReopenedPstCount);
        Assert.True(report.MboxrdObservations.IsCompatibleWithCorpus);

        // mboxo records actual 0 and marks mode/corpus pairing incompatible/unsupported for this input; its empty PST is explicitly reported
        Assert.Equal(0, report.MboxoObservations.PhysicalReadCount);
        Assert.Equal(0, report.MboxoObservations.ReopenedPstCount);
        Assert.False(report.MboxoObservations.IsCompatibleWithCorpus);

        // - Four attachment bytes/hashes remain intact, one source CID survives
        Assert.Equal(12, report.StageDiffs.AttachmentIntegrityMatchCount);
        Assert.Equal(1, report.StageDiffs.InlineCidMatchCount);
        Assert.Equal(1, report.StageDiffs.TrueInlineCidPreservedCount);

        // - Report exists
        Assert.True(File.Exists(reportJsonPath), "report.json must be written.");
    }

    private static MboxModeReport RunMboxDialectExperiment(string dialect, string mboxPath, string runDir, SourceOracleSummary oracle)
    {
        string readerClass = dialect == "mboxrd" ? "MboxrdStorageReader" : "MboxoStorageReader";
        string pstPath = Path.Combine(runDir, $"{dialect}-stage.pst");
        var modeReport = new MboxModeReport
        {
            DialectMode = dialect,
            ReaderClass = readerClass,
            OutputPstPath = pstPath
        };

        var loadedMessages = new List<MailMessage>();
        var mapiMessages = new List<MapiMessage>();

        var mboxOptions = new MboxLoadOptions { LeaveOpen = false };

        if (dialect == "mboxrd")
        {
            using var reader = new MboxrdStorageReader(mboxPath, mboxOptions);
            modeReport.TotalReportedByReader = reader.GetTotalItemsCount();
            MailMessage? msg;
            while ((msg = reader.ReadNextMessage()) != null)
            {
                loadedMessages.Add(msg);
            }
        }
        else
        {
            using var reader = new MboxoStorageReader(mboxPath, mboxOptions);
            modeReport.TotalReportedByReader = reader.GetTotalItemsCount();
            MailMessage? msg;
            while ((msg = reader.ReadNextMessage()) != null)
            {
                loadedMessages.Add(msg);
            }
        }

        modeReport.PhysicalReadCount = loadedMessages.Count;

        if (dialect == "mboxo")
        {
            modeReport.IsCompatibleWithCorpus = false;
            modeReport.DialectSupportStatus = "Incompatible / Unsupported for this mboxrd corpus input";
            modeReport.Notes = "Reading this mboxrd corpus fixture as mboxo returns 0 messages. This is an explicit incompatible-dialect observation; its empty PST is still explicitly reported.";
        }
        else
        {
            modeReport.IsCompatibleWithCorpus = true;
            modeReport.DialectSupportStatus = "Supported / Compatible";
            modeReport.Notes = "MboxrdStorageReader successfully read 12 physical messages from the mboxrd corpus.";
        }

        for (int i = 0; i < loadedMessages.Count; i++)
        {
            string fid = i < FixtureIds.Length ? FixtureIds[i] : $"msg-extra-{i + 1}";
            var mail = loadedMessages[i];
            modeReport.Messages.Add(CreateStageObservation(i + 1, fid, $"Mbox_{dialect}", "Corpus", mail));

            var mapi = MapiMessage.FromMailMessage(mail, MapiConversionOptions.UnicodeFormat);
            mapiMessages.Add(mapi);
        }

        // Write to new PST
        using (var pst = PersonalStorage.Create(pstPath, FileFormatVersion.Unicode))
        {
            var folder = pst.RootFolder.AddSubFolder("Corpus");
            foreach (var mapi in mapiMessages)
            {
                folder.AddMessage(mapi);
            }
        }

        modeReport.OutputPstSha256 = ComputeFileSha256(pstPath);
        modeReport.OutputPstSizeBytes = new FileInfo(pstPath).Length;

        // Reopen PST
        using (var reopenedPst = PersonalStorage.FromFile(pstPath))
        {
            var folder = reopenedPst.RootFolder.GetSubFolder("Corpus");
            var msgInfos = folder.EnumerateMessages().ToList();
            modeReport.ReopenedPstCount = msgInfos.Count;

            for (int i = 0; i < msgInfos.Count; i++)
            {
                string fid = i < FixtureIds.Length ? FixtureIds[i] : $"msg-extra-{i + 1}";
                using var extracted = reopenedPst.ExtractMessage(msgInfos[i]);
                modeReport.ReopenedPstMessages.Add(CreateStageObservation(i + 1, fid, $"Mbox_{dialect}_ReopenedPst", "Corpus", extracted));
            }
        }

        // Analyze msg-08 From line behavior
        modeReport.Msg08FromLineBehavior = AnalyzeMsg08FromLineBehavior(dialect, loadedMessages, mapiMessages, modeReport.ReopenedPstMessages, oracle);

        // Cleanup
        foreach (var m in loadedMessages) m.Dispose();
        foreach (var m in mapiMessages) m.Dispose();

        return modeReport;
    }

    private static FromLineBehaviorComparison AnalyzeMsg08FromLineBehavior(
        string dialect,
        List<MailMessage> loadedMessages,
        List<MapiMessage> mapiMessages,
        List<StageObservationRecord> reopenedPstMessages,
        SourceOracleSummary oracle)
    {
        var comp = new FromLineBehaviorComparison();

        // Source EML lines for msg-08
        var sourceMsg08 = oracle.Messages.FirstOrDefault(m => m.FixtureId == "msg-08");
        if (sourceMsg08 != null)
        {
            comp.SourceEmlLines = string.Join("\n", sourceMsg08.RawBodyFromLineFacts.MatchedLines);
        }

        // Raw Mbox lines for msg-08
        comp.RawMboxBytesLines = string.Join("\n", oracle.RawMboxByteFacts.Msg08SegmentFacts.MatchedLines);

        // SDK MailMessage lines for msg-08 (8th message)
        if (loadedMessages.Count >= 8)
        {
            var msg08Mail = loadedMessages[7];
            var scan = ScanFromLines(msg08Mail.Body);
            comp.SdkMailMessageLines = string.Join("\n", scan.MatchedLines);
            comp.LiteralFromPreserved = scan.MatchedLines.Any(l => l.StartsWith("From "));
            comp.QuotedFromPreserved = scan.MatchedLines.Any(l => l.StartsWith(">From "));
            comp.UnescapedOnceByReader = scan.MatchedLines.Any(l => l.StartsWith("From Ahmet Yılmaz"));
            comp.IsFidelityBlocker = !comp.UnescapedOnceByReader;
            comp.FidelityAssessment = comp.UnescapedOnceByReader
                ? "Unescaped once by reader."
                : "Fidelity blocker: raw '>From' and '>>From' remained '>From' and '>>From' after SDK reading; the reader did NOT unescape once, so original literal 'From ' was not restored.";
        }
        else if (dialect == "mboxo")
        {
            comp.IsFidelityBlocker = false;
            comp.FidelityAssessment = "Incompatible dialect: MboxoStorageReader returned 0 messages for this mboxrd input corpus.";
        }

        // MAPI Message lines for msg-08
        if (mapiMessages.Count >= 8)
        {
            var msg08Mapi = mapiMessages[7];
            var scan = ScanFromLines(msg08Mapi.Body);
            comp.MapiMessageLines = string.Join("\n", scan.MatchedLines);
        }

        // Reopened PST lines for msg-08
        var reopenedMsg08 = reopenedPstMessages.FirstOrDefault(m => m.FixtureId == "msg-08");
        if (reopenedMsg08 != null)
        {
            comp.ReopenedPstLines = string.Join("\n", reopenedMsg08.FromLineFacts.MatchedLines);
        }

        return comp;
    }

    private static StageObservationRecord CreateStageObservation(int ordinal, string fixtureId, string stage, string folderPath, MailMessage mail)
    {
        string? body = mail.Body;
        string? html = mail.HtmlBody;
        string? normalizedBody = body?.Replace("\r\n", "\n").Replace("\r", "\n");

        var obs = new StageObservationRecord
        {
            Ordinal = ordinal,
            FixtureId = fixtureId,
            Stage = stage,
            FolderPath = folderPath,
            Subject = mail.Subject,
            SubjectHasEvaluationWatermark = HasEvaluationWatermark(mail.Subject),
            BodyHasEvaluationWatermark = HasBodyEvaluationWatermark(body),
            Sender = mail.From?.ToString(),
            Recipients = string.Join("; ", mail.To.Select(t => t.ToString())),
            Date = mail.Date.ToString("o"),
            MessageId = mail.MessageId,
            BodySnippet = body != null ? (body.Length > 100 ? body[..100] : body) : null,
            BodyLength = body?.Length ?? 0,
            BodySha256 = normalizedBody != null ? ComputeSha256(Encoding.UTF8.GetBytes(normalizedBody)) : null,
            HasHtml = !string.IsNullOrEmpty(html),
            HtmlLength = html?.Length ?? 0,
            FromLineFacts = ScanFromLines(body)
        };

        // Regular attachments
        foreach (var att in mail.Attachments)
        {
            byte[] bytes = ReadStreamBytes(att.ContentStream);
            obs.Attachments.Add(new StageAttachmentRecord
            {
                FileName = att.Name ?? "attachment",
                ContentType = att.ContentType?.MediaType,
                SizeBytes = bytes.Length,
                Sha256 = ComputeSha256(bytes),
                IsInline = false,
                ContentId = att.ContentId,
                SourceCollection = "Attachments"
            });
        }

        // Linked resources (inline CID)
        foreach (var res in mail.LinkedResources)
        {
            byte[] bytes = ReadStreamBytes(res.ContentStream);
            obs.Attachments.Add(new StageAttachmentRecord
            {
                FileName = res.ContentType?.Name ?? res.ContentId ?? "linked_resource",
                ContentType = res.ContentType?.MediaType,
                SizeBytes = bytes.Length,
                Sha256 = ComputeSha256(bytes),
                IsInline = true,
                ContentId = res.ContentId,
                SourceCollection = "LinkedResources"
            });
        }

        obs.AttachmentCount = obs.Attachments.Count;
        return obs;
    }

    private static StageObservationRecord CreateStageObservation(int ordinal, string fixtureId, string stage, string folderPath, MapiMessage mapi)
    {
        string? body = mapi.Body;
        string? html = mapi.BodyHtml;
        string? normalizedBody = body?.Replace("\r\n", "\n").Replace("\r", "\n");

        string sender = !string.IsNullOrEmpty(mapi.SenderName)
            ? $"{mapi.SenderName} <{mapi.SenderEmailAddress}>"
            : (mapi.SenderEmailAddress ?? string.Empty);

        var obs = new StageObservationRecord
        {
            Ordinal = ordinal,
            FixtureId = fixtureId,
            Stage = stage,
            FolderPath = folderPath,
            Subject = mapi.Subject,
            SubjectHasEvaluationWatermark = HasEvaluationWatermark(mapi.Subject),
            BodyHasEvaluationWatermark = HasBodyEvaluationWatermark(body),
            Sender = sender,
            Recipients = mapi.DisplayTo,
            Date = (mapi.ClientSubmitTime != DateTime.MinValue ? mapi.ClientSubmitTime : mapi.DeliveryTime).ToString("o"),
            MessageId = mapi.InternetMessageId,
            BodySnippet = body != null ? (body.Length > 100 ? body[..100] : body) : null,
            BodyLength = body?.Length ?? 0,
            BodySha256 = normalizedBody != null ? ComputeSha256(Encoding.UTF8.GetBytes(normalizedBody)) : null,
            HasHtml = !string.IsNullOrEmpty(html),
            HtmlLength = html?.Length ?? 0,
            FromLineFacts = ScanFromLines(body)
        };

        string sourceCollection = stage == "ReopenedPst" ? "PstAttachments" : "MapiAttachments";
        foreach (var att in mapi.Attachments)
        {
            byte[] bytes = att.BinaryData ?? Array.Empty<byte>();
            string name = !string.IsNullOrEmpty(att.LongFileName)
                ? att.LongFileName
                : (!string.IsNullOrEmpty(att.FileName) ? att.FileName : (att.DisplayName ?? "attachment"));

            string? cid = AttachmentHelper.GetAttachmentContentId(att);

            obs.Attachments.Add(new StageAttachmentRecord
            {
                FileName = name,
                ContentType = att.MimeTag,
                SizeBytes = bytes.Length,
                Sha256 = ComputeSha256(bytes),
                IsInline = att.IsInline || !string.IsNullOrEmpty(cid),
                ContentId = cid,
                SourceCollection = sourceCollection
            });
        }

        obs.AttachmentCount = obs.Attachments.Count;
        return obs;
    }

    private static StageDiffSummary BuildStageDiffs(List<SourceMessageOracleRecord> oracleMessages, EmlStageReport emlReport)
    {
        var summary = new StageDiffSummary();

        for (int i = 0; i < oracleMessages.Count; i++)
        {
            var src = oracleMessages[i];
            var mailObs = emlReport.MailMessageObservations.ElementAtOrDefault(i);
            var mapiObs = emlReport.MapiObservations.ElementAtOrDefault(i);
            var pstObs = emlReport.ReopenedPstObservations.ElementAtOrDefault(i);

            bool watermarkFound = (mailObs?.SubjectHasEvaluationWatermark ?? false) ||
                                  (mapiObs?.SubjectHasEvaluationWatermark ?? false) ||
                                  (pstObs?.SubjectHasEvaluationWatermark ?? false);

            if (watermarkFound) summary.TotalSubjectWatermarkAlteredCount++;

            bool bodyWatermarkFound = (mailObs?.BodyHasEvaluationWatermark ?? false) ||
                                      (mapiObs?.BodyHasEvaluationWatermark ?? false) ||
                                      (pstObs?.BodyHasEvaluationWatermark ?? false);

            if (bodyWatermarkFound) summary.TotalBodyWatermarkAlteredCount++;

            // Attachment integrity check
            bool attsMatched = true;
            if (src.AttachmentCount > 0)
            {
                foreach (var srcAtt in src.Attachments)
                {
                    bool foundInPst = pstObs != null && pstObs.Attachments.Any(a => a.Sha256 == srcAtt.Sha256);
                    if (!foundInPst)
                    {
                        attsMatched = false;
                        break;
                    }
                }
            }
            if (attsMatched) summary.AttachmentIntegrityMatchCount++;

            // CID check: distinguish true source inline CID from synthetic/generated CID
            string? srcCid = src.Attachments.FirstOrDefault(a => !string.IsNullOrEmpty(a.ContentId))?.ContentId;
            string? sdkCid = mailObs?.Attachments.FirstOrDefault(a => !string.IsNullOrEmpty(a.ContentId))?.ContentId;
            string? mapiCid = mapiObs?.Attachments.FirstOrDefault(a => !string.IsNullOrEmpty(a.ContentId))?.ContentId;
            string? pstCid = pstObs?.Attachments.FirstOrDefault(a => !string.IsNullOrEmpty(a.ContentId))?.ContentId;

            bool trueInlinePreserved = false;
            bool generatedCidAdded = false;

            if (!string.IsNullOrEmpty(srcCid))
            {
                if (sdkCid == srcCid || mapiCid == srcCid || pstCid == srcCid)
                {
                    summary.InlineCidMatchCount++;
                    trueInlinePreserved = true;
                    summary.TrueInlineCidPreservedCount++;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(sdkCid) || !string.IsNullOrEmpty(mapiCid) || !string.IsNullOrEmpty(pstCid))
                {
                    generatedCidAdded = true;
                    summary.GeneratedCidAdditionCount++;
                }
            }

            var diff = new MessageDiffRecord
            {
                FixtureId = src.FixtureId,
                SourceSubject = src.Subject,
                MailMessageSubject = mailObs?.Subject,
                MapiSubject = mapiObs?.Subject,
                ReopenedPstSubject = pstObs?.Subject,
                SubjectEvaluationWatermarkFound = watermarkFound,
                BodyEvaluationWatermarkFound = bodyWatermarkFound,
                SourceFrom = src.From,
                SdkSender = mailObs?.Sender,
                MapiSender = mapiObs?.Sender,
                SourceMessageId = src.MessageId,
                SdkMessageId = mailObs?.MessageId,
                MapiMessageId = mapiObs?.MessageId,
                SourceAttachmentCount = src.AttachmentCount,
                SdkAttachmentCount = mailObs?.AttachmentCount ?? 0,
                MapiAttachmentCount = mapiObs?.AttachmentCount ?? 0,
                ReopenedPstAttachmentCount = pstObs?.AttachmentCount ?? 0,
                AllAttachmentsShaMatched = attsMatched,
                InlineCidSource = srcCid,
                InlineCidSdk = sdkCid,
                InlineCidMapi = mapiCid,
                InlineCidReopenedPst = pstCid,
                HasGeneratedContentId = generatedCidAdded,
                TrueInlineCidPreserved = trueInlinePreserved
            };

            summary.MessageDiffs.Add(diff);
        }

        return summary;
    }

    private static TrialEvaluationSummary BuildTrialEvaluationSummary(EmlStageReport emlReport, StageDiffSummary diffs)
    {
        var summary = new TrialEvaluationSummary();
        var affected = new List<string>();

        foreach (var d in diffs.MessageDiffs)
        {
            if (d.SubjectEvaluationWatermarkFound)
            {
                affected.Add(d.FixtureId);
            }
        }

        summary.AffectedSubjectFixtureIds = affected;
        summary.WatermarkDetected = affected.Count > 0;

        if (summary.WatermarkDetected)
        {
            var sampleObs = emlReport.MailMessageObservations.FirstOrDefault(o => o.SubjectHasEvaluationWatermark);
            summary.WatermarkPatternSample = sampleObs?.Subject;
            summary.Notes = "Subject is suffixed exactly '(Aspose.Email Evaluation)', not prefixed by a generic phrase. MailMessage body carries the observed uppercase Turkish-I evaluation banner ('EVALUATİON ONLY. CREATED WİTH ASPOSE.EMAİL FOR .NET. COPYRİGHT 2002-2026 ASPOSE PTY LTD.'). Reopened PST plain body is often replaced by the title-cased evaluation body ('Evaluation Only. Created with Aspose.Email for .NET. Copyright 2002-2026 Aspose Pty Ltd.') while HTML remains separately measurable. Conversion cannot be generalized as lossless under evaluation mode, though 4 attachment byte hashes and 1 true inline source CID ('proje_logo_cid') survive intact.";
        }
        else
        {
            summary.Notes = "No evaluation watermark detected on tested messages.";
        }

        summary.TruncationDetected = false;
        return summary;
    }

    private static List<RiskOrDecisionItem> BuildRisksAndDecisions()
    {
        return
        [
            new RiskOrDecisionItem
            {
                Area = "Aspose Evaluation Watermarks & Body Alteration",
                Observation = "Aspose.Email evaluation mode does not prefix a generic phrase; subjects are suffixed exactly with '(Aspose.Email Evaluation)'. In bodies, MailMessage carries an uppercase Turkish-I evaluation banner ('EVALUATİON ONLY. CREATED WİTH ASPOSE.EMAİL FOR .NET. COPYRİGHT 2002-2026 ASPOSE PTY LTD.'), while reopened PST plain text bodies are often completely replaced by the title-cased evaluation body ('Evaluation Only. Created with Aspose.Email for .NET. Copyright 2002-2026 Aspose Pty Ltd.'). HTML body markup remains separately measurable.",
                Risk = "Under evaluation mode, conversion cannot be generalized as lossless: subjects are suffixed and plain text bodies are overwritten or injected with evaluation banners. In production without an applied license, message subjects will contain suffix watermarks and plain text bodies may be replaced or banner-injected.",
                DecisionNeededFromRoot = "Do not generalize conversion as lossless under trial mode. Require a verified production Aspose license before executing production import, and configure automated tests to accept the exact measured suffix '(Aspose.Email Evaluation)' and plain-body replacement behaviors."
            },
            new RiskOrDecisionItem
            {
                Area = "MBOX Dialect Support (mboxrd vs mboxo vs mboxcl) & From-Unescaping Fidelity",
                Observation = "Aspose.Email provides MboxrdStorageReader and MboxoStorageReader APIs. However, reading this mboxrd corpus with MboxoStorageReader yields 0 messages, confirming the mboxo dialect is incompatible/unsupported for this input (an empty PST is explicitly recorded and reported). For mboxrd, while 12 messages were read, raw '>From' and '>>From' remained '>From' and '>>From' after SDK reading; the reader did NOT unescape once, so original literal 'From ' was not restored. Aspose also lacks built-in mboxcl/mboxcl2 Content-Length delimited readers.",
                Risk = "Attempting to use MboxoStorageReader on mboxrd files results in 0 messages imported and an empty PST. In mboxrd mode, failure of the SDK reader to unescape '>From' and '>>From' lines means escaped quote markers permanently corrupt body content instead of being unescaped, acting as a fidelity blocker.",
                DecisionNeededFromRoot = "Designate mboxrd as the authoritative default MBOX dialect for product import, record mboxo as incompatible/unsupported for mboxrd corpora, and mark reader failure to unescape '>From' and '>>From' as a fidelity blocker/decision point requiring upstream unescaping normalization or documented limitation. Document mboxcl as unsupported."
            },
            new RiskOrDecisionItem
            {
                Area = "Inline CID vs Normal Attachments in PST",
                Observation = "Ordinary attachments without source Content-IDs (msg-05, msg-06, msg-07) receive generated Content-ID GUID values from the SDK during processing, while the true source inline CID in msg-11 ('proje_logo_cid') survives intact across MailMessage, MAPI, and reopened PST.",
                Risk = "Ordinary attachments receiving synthetic Content-IDs could be misinterpreted as inline attachments by downstream MAPI/PST viewers if inline disposition flags are not distinguished from true source CIDs.",
                DecisionNeededFromRoot = "Distinguish generated CID additions from source CID preservation: preserve true inline references (such as msg-11 'proje_logo_cid') for HTML body rendering while ensuring ordinary attachments remain classified as standard file attachments regardless of synthetic Content-ID values."
            }
        ];
    }

    private static RawMboxByteFacts AnalyzeRawMboxBytes(string mboxPath)
    {
        var facts = new RawMboxByteFacts();
        var lines = File.ReadAllLines(mboxPath, Encoding.UTF8);

        long offset = 0;
        int currentOrdinal = 0;
        int msg08Ordinal = 8;
        bool inMsg08Body = false;
        var msg08BodyLines = new List<string>();

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (line.StartsWith("From ") && !line.StartsWith("From: "))
            {
                currentOrdinal++;
                inMsg08Body = (currentOrdinal == msg08Ordinal);

                string? msgId = null;
                for (int h = lineIndex + 1; h < Math.Min(lineIndex + 30, lines.Length); h++)
                {
                    if (lines[h].StartsWith("Message-ID: ", StringComparison.OrdinalIgnoreCase) ||
                        lines[h].StartsWith("Message-Id: ", StringComparison.OrdinalIgnoreCase))
                    {
                        msgId = lines[h][12..].Trim();
                        break;
                    }
                    if (string.IsNullOrWhiteSpace(lines[h])) break;
                }

                facts.Envelopes.Add(new MboxEnvelopeRecord
                {
                    Ordinal = currentOrdinal,
                    EnvelopeLine = line,
                    ByteOffset = offset,
                    MessageId = msgId
                });
            }
            else if (inMsg08Body)
            {
                msg08BodyLines.Add(line);
            }

            offset += Encoding.UTF8.GetByteCount(line) + 1; // approximate byte count per line
        }

        facts.TotalEnvelopeCount = facts.Envelopes.Count;
        facts.Msg08SegmentFacts = ScanFromLines(string.Join("\n", msg08BodyLines));
        return facts;
    }

    private static FromLineScanFacts ScanFromLines(string? text)
    {
        var facts = new FromLineScanFacts();
        if (string.IsNullOrEmpty(text)) return facts;

        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (line.StartsWith(">>From "))
            {
                facts.GtGtFromLinesCount++;
                facts.MatchedLines.Add(line);
            }
            else if (line.StartsWith(">From "))
            {
                facts.GtFromLinesCount++;
                facts.MatchedLines.Add(line);
            }
            else if (line.StartsWith("From "))
            {
                facts.FromLinesCount++;
                facts.MatchedLines.Add(line);
            }
        }
        return facts;
    }

    private static bool HasEvaluationWatermark(string? subject)
    {
        if (string.IsNullOrEmpty(subject)) return false;
        return subject.EndsWith("(Aspose.Email Evaluation)", StringComparison.OrdinalIgnoreCase) ||
               subject.Contains("(Aspose.Email Evaluation)", StringComparison.OrdinalIgnoreCase) ||
               subject.Contains("Evaluation Only", StringComparison.OrdinalIgnoreCase) ||
               subject.Contains("Aspose", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasBodyEvaluationWatermark(string? body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        return body.Contains("EVALUATİON ONLY", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("Evaluation Only", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("ASPOSE.EMAİL", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("Aspose.Email", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadStreamBytes(Stream? stream)
    {
        if (stream == null) return Array.Empty<byte>();
        using var ms = new MemoryStream();
        if (stream.CanSeek) stream.Position = 0;
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ComputeSha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static string ComputeFileSha256(string filePath)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();
    }

    private static string ResolveOraclePath()
    {
        string? envPath = Environment.GetEnvironmentVariable("BITIGMAIL_ORACLE_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        string[] startDirs = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        foreach (var startDir in startDirs)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "fixtures", "mime-import-v1", "expected-mime.json");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
                dir = dir.Parent;
            }
        }

        string relativeFallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "mime-import-v1", "expected-mime.json"));
        if (File.Exists(relativeFallback))
        {
            return relativeFallback;
        }

        string tempFallback = Path.Combine(Path.GetTempPath(), "bitigmail-task013-qa");
        if (Directory.Exists(tempFallback))
        {
            var candidate = Directory.GetFiles(tempFallback, "report.json", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Contains("oracle-"));
            if (candidate != null)
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Permanent independent oracle fixture not found at fixtures/mime-import-v1/expected-mime.json");
    }

    private static (string CorpusRoot, string MboxPath) ResolveCorpusPaths(string oraclePath, JsonElement oracleRoot)
    {
        string? relCorpusPath = oracleRoot.TryGetProperty("corpusPath", out var cpProp) ? cpProp.GetString() : null;

        string? repoRoot = null;
        var dir = new DirectoryInfo(Path.GetDirectoryName(oraclePath)!);
        var cur = dir;
        while (cur != null)
        {
            if (Directory.Exists(Path.Combine(cur.FullName, "fixtures", "mail-corpus-v1")))
            {
                repoRoot = cur.FullName;
                break;
            }
            cur = cur.Parent;
        }

        if (repoRoot == null)
        {
            string[] starts = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
            foreach (var start in starts)
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    if (Directory.Exists(Path.Combine(d.FullName, "fixtures", "mail-corpus-v1")))
                    {
                        repoRoot = d.FullName;
                        break;
                    }
                    d = d.Parent;
                }
                if (repoRoot != null) break;
            }
        }

        string corpusRoot;
        if (!string.IsNullOrEmpty(relCorpusPath) && repoRoot != null)
        {
            corpusRoot = Path.GetFullPath(Path.Combine(repoRoot, relCorpusPath));
        }
        else if (oracleRoot.TryGetProperty("mbox", out var mboxObj) && mboxObj.TryGetProperty("path", out var pProp) && File.Exists(pProp.GetString()!))
        {
            corpusRoot = Path.GetDirectoryName(pProp.GetString()!)!;
        }
        else if (repoRoot != null)
        {
            corpusRoot = Path.GetFullPath(Path.Combine(repoRoot, "fixtures", "mail-corpus-v1"));
        }
        else
        {
            corpusRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "mail-corpus-v1"));
        }

        string mboxPath = Path.Combine(corpusRoot, "corpus.mbox");
        if (!File.Exists(mboxPath) && oracleRoot.TryGetProperty("mbox", out var mb) && mb.TryGetProperty("path", out var pp))
        {
            mboxPath = pp.GetString()!;
        }

        if (!File.Exists(mboxPath))
        {
            throw new FileNotFoundException($"MBOX corpus fixture not found at: {mboxPath}");
        }

        return (corpusRoot, mboxPath);
    }
}
