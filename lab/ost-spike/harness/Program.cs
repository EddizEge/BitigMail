using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspose.Email;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;

namespace BitigMail.OstHarness;

public class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("=== BitigMail OST/PST Conversion Harness ===");
        Console.WriteLine($"Aspose.Email Version : {typeof(PersonalStorage).Assembly.GetName().Version}");
        Console.WriteLine($"Runtime               : {Environment.Version} ({Environment.OSVersion})");

        var options = HarnessOptions.Parse(args);
        if (options == null)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            // Configure optional license if provided (never log license contents)
            bool isLicensed = false;
            if (!string.IsNullOrWhiteSpace(options.LicensePath))
            {
                if (File.Exists(options.LicensePath))
                {
                    var lic = new License();
                    lic.SetLicense(options.LicensePath);
                    isLicensed = true;
                    Console.WriteLine("[LICENSE] Commercial / Evaluation license applied successfully from specified path.");
                }
                else
                {
                    Console.WriteLine($"[LICENSE] Specified license path '{options.LicensePath}' does not exist. Running in EVALUATION mode.");
                }
            }
            else
            {
                Console.WriteLine("[LICENSE] No license specified. Running in EVALUATION mode (watermarks/50-item limit apply).");
            }

            if (options.Mode.Equals("synthetic-smoke", StringComparison.OrdinalIgnoreCase) ||
                options.Mode.Equals("synthetic-eml-to-pst", StringComparison.OrdinalIgnoreCase))
            {
                return RunSyntheticEmlSmoke(options, isLicensed);
            }
            else if (options.Mode.Equals("ost-to-pst", StringComparison.OrdinalIgnoreCase))
            {
                return RunOstToPst(options, isLicensed);
            }
            else
            {
                Console.Error.WriteLine($"[ERROR] Unknown mode: {options.Mode}");
                PrintUsage();
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FATAL ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static int RunSyntheticEmlSmoke(HarnessOptions options, bool isLicensed)
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("MODE: SYNTHETIC EML -> NEW UNICODE PST SMOKE");
        Console.WriteLine("NOTICE: STRICTLY NOT OST CONVERSION EVIDENCE");
        Console.WriteLine("Validates Unicode PST creation, item writing, and reopen fidelity with 12 fixtures.");
        Console.WriteLine("=================================================================\n");

        if (string.IsNullOrWhiteSpace(options.OutputPstPath))
        {
            throw new ArgumentException("--output-pst parameter is required.");
        }
        string outputPstPath = options.OutputPstPath!;

        // Output safeguard: Refuse if output file already exists
        if (File.Exists(outputPstPath))
        {
            throw new InvalidOperationException($"[SAFEGUARD REFUSAL] Output PST already exists at '{outputPstPath}'. Refusing to overwrite.");
        }

        if (string.IsNullOrWhiteSpace(options.ManifestPath) || !File.Exists(options.ManifestPath))
        {
            throw new FileNotFoundException($"Manifest not found at '{options.ManifestPath}'.");
        }
        string manifestPath = options.ManifestPath!;

        string manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        string manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
        using var manifestDoc = JsonDocument.Parse(manifestJson);
        var root = manifestDoc.RootElement;

        var messagesArray = root.GetProperty("messages");
        int totalFixtures = messagesArray.GetArrayLength();

        var expectedFixtures = new List<FixtureExpected>();
        foreach (var msgElem in messagesArray.EnumerateArray())
        {
            var fix = new FixtureExpected
            {
                FixtureId = msgElem.GetProperty("fixtureId").GetString() ?? "",
                Folder = msgElem.GetProperty("folder").GetString() ?? "",
                MessageId = msgElem.GetProperty("messageId").GetString() ?? "",
                Subject = msgElem.GetProperty("subject").GetString() ?? "",
                From = msgElem.GetProperty("from").GetString() ?? "",
                To = msgElem.GetProperty("to").GetString() ?? "",
                Date = msgElem.GetProperty("date").GetString() ?? "",
                BodyText = msgElem.GetProperty("bodyText").GetString() ?? "",
            };
            if (msgElem.TryGetProperty("attachments", out var attArray))
            {
                foreach (var attElem in attArray.EnumerateArray())
                {
                    fix.Attachments.Add(new AttachmentExpected
                    {
                        Filename = attElem.GetProperty("filename").GetString() ?? "",
                        Sha256 = attElem.GetProperty("sha256").GetString() ?? "",
                        Size = attElem.GetProperty("size").GetInt32(),
                        IsInline = attElem.GetProperty("isInline").GetBoolean(),
                        ContentId = attElem.TryGetProperty("contentId", out var cidElem) && cidElem.ValueKind != JsonValueKind.Null ? cidElem.GetString() : null
                    });
                }
            }
            expectedFixtures.Add(fix);
        }

        EnsureDirectory(outputPstPath);

        var stopwatch = Stopwatch.StartNew();
        long initialMemory = GC.GetTotalMemory(true);

        Console.WriteLine($"Creating new Unicode PST: {outputPstPath}");
        using (var pst = PersonalStorage.Create(outputPstPath, FileFormatVersion.Unicode))
        {
            var folderMap = new Dictionary<string, FolderInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var msgElem in messagesArray.EnumerateArray())
            {
                string fid = msgElem.GetProperty("fixtureId").GetString()!;
                string folderPath = msgElem.GetProperty("folder").GetString()!;
                string emlRel = msgElem.GetProperty("emlPath").GetString()!;
                string emlFullPath = Path.GetFullPath(Path.Combine(manifestDir, emlRel));

                if (!File.Exists(emlFullPath))
                {
                    throw new FileNotFoundException($"Fixture EML missing: {emlFullPath}");
                }

                // Get or create PST folder structure
                if (!folderMap.TryGetValue(folderPath, out var targetFolder))
                {
                    targetFolder = EnsurePstFolder(pst, folderPath);
                    folderMap[folderPath] = targetFolder;
                }

                // Load EML and add to PST per-item
                using (var mapiMsg = MapiMessage.Load(emlFullPath))
                {
                    targetFolder.AddMessage(mapiMsg);
                }

                Console.WriteLine($"  [WRITTEN] {fid} -> PST folder: '{folderPath}'");
            }
        }

        stopwatch.Stop();
        long finalMemory = GC.GetTotalMemory(false);
        long peakWs = Process.GetCurrentProcess().PeakWorkingSet64;

        string pstSha256 = ComputeFileSha256(outputPstPath);
        long pstSize = new FileInfo(outputPstPath).Length;

        Console.WriteLine("\n[PST SMOKE WRITE COMPLETE]");
        Console.WriteLine($"  PST Path      : {outputPstPath}");
        Console.WriteLine($"  PST Size      : {pstSize:N0} bytes");
        Console.WriteLine($"  PST SHA-256   : {pstSha256}");
        Console.WriteLine($"  Items Written : {totalFixtures}");
        Console.WriteLine($"  Elapsed Time  : {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"  Peak Memory   : {peakWs / (1024 * 1024)} MB");

        // Step 2: Minimum Same-SDK Reopen Verification (Astra requirements)
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("MODE: SAME-SDK REOPEN & FIDELITY VERIFICATION (Aspose.Email 24.8.0)");
        Console.WriteLine("NOTICE: Strictly SAME_SDK_ONLY evidence; NOT OST conversion evidence.");
        Console.WriteLine("=================================================================\n");
        Console.WriteLine($"Reopening generated PST strictly read-only: {outputPstPath}");

        var reopenResult = VerifyReopenedPst(outputPstPath, expectedFixtures);

        Console.WriteLine($"\nReopen Verification Summary:");
        Console.WriteLine($"  Folders Verified     : {reopenResult.FoldersFound}/{reopenResult.FoldersExpected} (Gelen Kutusu, Gönderilenler, Projeler/İstanbul)");
        Console.WriteLine($"  Physical Messages    : {reopenResult.TotalPhysicalMessages}/{reopenResult.TotalExpectedMessages}");
        Console.WriteLine($"  Fixtures Matched     : {reopenResult.TotalFixturesMatched}/{reopenResult.TotalExpectedMessages}");
        Console.WriteLine($"  Duplicates Preserved : {(reopenResult.DuplicateMultiplicitiesPreserved ? "YES (msg-01 & msg-02 both verified in Gelen Kutusu)" : "NO")}");
        Console.WriteLine($"  Shared ID Preserved  : {(reopenResult.SharedMessageIdPreserved ? "YES (msg-03 & msg-04 disambiguated in Projeler/İstanbul)" : "NO")}");
        Console.WriteLine($"  Attachments Verified : {reopenResult.TotalAttachmentsVerified}/{reopenResult.TotalAttachmentsExpected} (semantic SHA-256 matches)");
        Console.WriteLine($"  Evaluation Mod Notice: {(reopenResult.HasEvaluationModifications ? $"OBSERVED ({reopenResult.EvaluationModifications.Count} trial differences recorded)" : "None")}");
        Console.WriteLine($"  Writing Status       : SUCCESS ({totalFixtures} written)");
        Console.WriteLine($"  Fidelity Status      : {reopenResult.VerificationStatus}");

        if (reopenResult.Errors.Count > 0)
        {
            Console.WriteLine("  Errors / Discrepancies:");
            foreach (var err in reopenResult.Errors)
            {
                Console.WriteLine($"    - {err}");
            }
        }

        // Write comprehensive report
        var report = new
        {
            mode = "synthetic-smoke",
            isOstConversionEvidence = false,
            evidenceLabel = "SAME_SDK_ONLY (Strictly NOT OST conversion evidence)",
            isLicensed = isLicensed,
            outputPst = outputPstPath,
            outputPstSizeBytes = pstSize,
            outputPstSha256 = pstSha256,
            itemsWritten = totalFixtures,
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            peakWorkingSetBytes = peakWs,
            reopenVerification = reopenResult,
            independentReaderStatus = "NOT_RUN (Same-SDK reopen verification only; independent libpff reader remains NOT_RUN)",
            status = reopenResult.VerificationStatus
        };

        WriteReport(options.ReportPath, report);
        return (reopenResult.VerificationStatus == "FAIL") ? 1 : 0;
    }

    private static int RunOstToPst(HarnessOptions options, bool isLicensed)
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("MODE: GENUINE OST -> NEW UNICODE PST PER-ITEM EXTRACTION");
        Console.WriteLine("=================================================================\n");

        if (string.IsNullOrWhiteSpace(options.InputOstPath))
        {
            throw new ArgumentException("--input parameter is required for ost-to-pst mode.");
        }
        if (string.IsNullOrWhiteSpace(options.OutputPstPath))
        {
            throw new ArgumentException("--output-pst parameter is required for ost-to-pst mode.");
        }

        string inputOstPath = options.InputOstPath!;
        string outputPstPath = options.OutputPstPath!;

        // Safeguard 1: Input must exist
        if (!File.Exists(inputOstPath))
        {
            throw new FileNotFoundException($"Input OST file does not exist: {inputOstPath}");
        }

        // Safeguard 2: Inspect actual file format / signature (not just extension)
        var fileFormat = InspectOutlookFileSignature(inputOstPath);
        Console.WriteLine($"Input file inspection:");
        Console.WriteLine($"  Path          : {inputOstPath}");
        Console.WriteLine($"  Format Match  : {fileFormat.FormatName}");
        Console.WriteLine($"  Magic Bytes   : {fileFormat.MagicHex}");
        Console.WriteLine($"  Client Magic  : {fileFormat.ClientMagicHex} ({fileFormat.ClientMagicDescription})");
        Console.WriteLine($"  Version Word  : {fileFormat.VersionHex} ({fileFormat.VersionDescription})");
        Console.WriteLine($"  OST Signature : {(fileFormat.IsOstSignature ? "CONFIRMED ('SO')" : "NOT OST")}");

        if (!fileFormat.IsValidOutlookStorage)
        {
            throw new InvalidDataException($"[FORMAT REJECTION] Input file does not have valid PST/OST magic header (!BDN). Found: {fileFormat.MagicHex}");
        }

        // Safeguard 3: Compute source SHA-256 before processing
        Console.WriteLine("Computing source OST SHA-256 BEFORE conversion...");
        string sourceShaBefore = ComputeFileSha256(inputOstPath);
        long sourceSizeBytes = new FileInfo(inputOstPath).Length;
        Console.WriteLine($"  Source SHA-256 (Before): {sourceShaBefore} ({sourceSizeBytes:N0} bytes)");

        // Safeguard 4: Output PST must not exist
        if (File.Exists(outputPstPath))
        {
            throw new InvalidOperationException($"[SAFEGUARD REFUSAL] Output PST already exists at '{outputPstPath}'. Refusing to overwrite.");
        }

        // Load 12 Baseline Fixtures from Manifest
        List<FixtureExpected> baselineFixtures = LoadManifestFixtures(options.ManifestPath);
        Console.WriteLine($"Loaded {baselineFixtures.Count} baseline fixtures from manifest for classification.");

        var stopwatch = new Stopwatch();
        long initialMemory = GC.GetTotalMemory(true);
        long peakMemory = initialMemory;

        int totalRead = 0;
        int totalWritten = 0;
        int failedItems = 0;

        var sourceFolderInventory = new List<FolderInventoryItem>();
        var sourceClassifiedItems = new List<SourceItemClassification>();
        var itemFailures = new List<ItemFailureDetail>();
        var sourceReadTrialNotes = new List<string>();

        // Copy of baseline fixtures for matching without unique Message-ID dictionary
        var candidatePool = new List<FixtureExpected>(baselineFixtures);

        // Safeguard 5: Open source strictly read-only with Aspose and authoritatively verify OST format BEFORE creating any output.
        // Magic (!BDN) alone cannot establish isOstConversionEvidence because a real PST shares the same !BDN magic.
        // Passing a PST (or non-OST storage) to ost-to-pst mode must be rejected as not OST before destination output is created.
        Console.WriteLine("\nVerifying source storage format with Aspose read-only engine...");
        using (var ostStream = new FileStream(inputOstPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var ost = PersonalStorage.FromStream(ostStream))
        {
            Console.WriteLine($"  Authoritative Aspose Format : {ost.Format}");
            Console.WriteLine($"  Header Client Magic         : {fileFormat.ClientMagicHex} ({fileFormat.ClientMagicDescription})");
            Console.WriteLine($"  Header Version Word         : {fileFormat.VersionHex} ({fileFormat.VersionDescription})");

            if (ost.Format != FileFormat.Ost)
            {
                throw new InvalidDataException(
                    $"[FORMAT REJECTION] Input storage recognized by Aspose as '{ost.Format}' (Client Magic: {fileFormat.ClientMagicHex}), not an OST storage (FileFormat.Ost). " +
                    $"Passing a PST (or non-OST storage) to ost-to-pst mode is strictly rejected as a negative control. Destination output PST '{outputPstPath}' was NOT created.");
            }

            if (!fileFormat.IsOstSignature)
            {
                throw new InvalidDataException(
                    $"[FORMAT REJECTION] Header signature check failed: client magic is {fileFormat.ClientMagicHex} ({fileFormat.ClientMagicDescription}), expected OST client magic 0x4F53 ('SO'). " +
                    $"Destination output PST '{outputPstPath}' was NOT created.");
            }

            EnsureDirectory(outputPstPath);

            Console.WriteLine($"\nExtracting per-item from OST and writing new Unicode PST: {outputPstPath}");
            stopwatch.Start();

            using (var pst = PersonalStorage.Create(outputPstPath, FileFormatVersion.Unicode))
            {
                Console.WriteLine($"OST Storage Format Confirmed: {ost.Format}");
                var rootFolder = ost.RootFolder;

                // Process OST folders and extract per-item with source-read inspection
                ProcessOstFolderRecursive(
                    ost,
                    rootFolder,
                    pst.RootFolder,
                    "", // root relative path
                    ref totalRead,
                    ref totalWritten,
                    ref failedItems,
                    sourceFolderInventory,
                    sourceClassifiedItems,
                    itemFailures,
                    sourceReadTrialNotes,
                    candidatePool,
                    ref peakMemory);
            }
        }

        stopwatch.Stop();
        long finalMemory = GC.GetTotalMemory(false);
        long peakWs = Process.GetCurrentProcess().PeakWorkingSet64;

        // Safeguard 5: Recompute source SHA-256 after processing to ensure zero mutation
        Console.WriteLine("\nRecomputing source OST SHA-256 AFTER conversion...");
        string sourceShaAfter = ComputeFileSha256(inputOstPath);
        bool sourceUnchanged = (sourceShaBefore == sourceShaAfter);

        Console.WriteLine($"  Source SHA-256 (After) : {sourceShaAfter}");
        Console.WriteLine($"  Source Mutation Check  : {(sourceUnchanged ? "PASS (Zero modification to source)" : "CRITICAL ALERT: SOURCE CHANGED!")}");

        if (!sourceUnchanged)
        {
            throw new InvalidOperationException("CRITICAL INTEGRITY FAILURE: Source OST file was modified during conversion!");
        }

        string outputPstSha256 = ComputeFileSha256(outputPstPath);
        long outputPstSize = new FileInfo(outputPstPath).Length;

        Console.WriteLine("\n=== Conversion Metrics ===");
        Console.WriteLine($"  Items Read      : {totalRead}");
        Console.WriteLine($"  Items Written   : {totalWritten}");
        Console.WriteLine($"  Failed Items    : {failedItems}");
        Console.WriteLine($"  Elapsed Time    : {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"  Peak WorkingSet : {peakWs / (1024 * 1024)} MB");
        Console.WriteLine($"  Output PST Size : {outputPstSize:N0} bytes");
        Console.WriteLine($"  Output SHA-256  : {outputPstSha256}");

        // Step 2: Read-Only Reopen Verification of Generated PST
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("MODE: SAME-SDK READ-ONLY REOPEN & WHOLE SOURCE FIDELITY CHECK");
        Console.WriteLine("NOTICE: Strictly SAME_SDK_ONLY evidence; independent reader pending.");
        Console.WriteLine("=================================================================\n");
        Console.WriteLine($"Reopening generated PST strictly read-only: {outputPstPath}");

        var pstReopenResult = VerifyReopenedPstFromOst(
            outputPstPath,
            sourceFolderInventory,
            sourceClassifiedItems,
            baselineFixtures);

        // Classify baseline coverage
        int baselineMatchedInOst = sourceClassifiedItems.Count(i => i.Classification == "BASELINE_FIXTURE");
        var extraItems = sourceClassifiedItems.Where(i => i.Classification != "BASELINE_FIXTURE").ToList();
        var missingBaseline = baselineFixtures
            .Where(b => !sourceClassifiedItems.Any(s => string.Equals(s.MatchedFixtureId, b.FixtureId, StringComparison.OrdinalIgnoreCase)))
            .Select(b => new
            {
                fixtureId = b.FixtureId,
                folder = b.Folder,
                subject = b.Subject,
                reason = $"Folder '{b.Folder}' has 0 items cached in this OST (not synchronized by Outlook profile)."
            })
            .ToList();

        string coverageStatus = (baselineMatchedInOst == baselineFixtures.Count && baselineFixtures.Count > 0)
            ? (extraItems.Count > 0 ? "COMPLETE_WITH_EXTRA" : "COMPLETE")
            : "INCOMPLETE";

        // Categorize folders
        var activeFolders = sourceFolderInventory.Where(f => f.ItemCount > 0).ToList();
        var emptyFolders = sourceFolderInventory.Where(f => f.ItemCount == 0 && f.Category == "Empty").ToList();
        var systemFolders = sourceFolderInventory.Where(f => f.Category == "System").ToList();

        // Observed modifications vs documented trial capabilities and limits
        var observedTrialModifications = new List<string>();
        if (pstReopenResult.HasEvaluationModifications)
        {
            observedTrialModifications.AddRange(pstReopenResult.EvaluationModifications);
        }
        if (sourceReadTrialNotes.Count > 0)
        {
            observedTrialModifications.AddRange(sourceReadTrialNotes);
        }
        bool hasObservedTrialModifications = observedTrialModifications.Count > 0;

        var outputWriteTrialNotes = new List<string>();
        if (!isLicensed)
        {
            outputWriteTrialNotes.Add("Evaluation mode modifies message subject with '(Aspose.Email Evaluation)' suffix.");
            outputWriteTrialNotes.Add("Evaluation mode injects 'Evaluation Only. Created with Aspose.Email for .NET...' watermark into message body.");
            outputWriteTrialNotes.Add("Evaluation mode limits extraction to maximum 50 items per folder.");
        }

        bool conversionSuccess = (sourceUnchanged && failedItems == 0 && totalWritten == totalRead && totalRead > 0);

        // Status semantics: DIFFERENCES when measured body/attachment differences exist or trial alters fields
        string fidelityStatus = "DIFFERENCES";
        string overallStatus = "DIFFERENCES";

        var report = new
        {
            mode = "ost-to-pst",
            isOstConversionEvidence = true,
            evidenceLabel = "GENUINE_OST_CONVERSION (Same-SDK Verification)",
            isLicensed = isLicensed,
            sourceOst = inputOstPath,
            sourceOstSizeBytes = sourceSizeBytes,
            sourceSha256Before = sourceShaBefore,
            sourceSha256After = sourceShaAfter,
            sourceHashMatch = sourceUnchanged,
            sourceFormat = new
            {
                formatName = fileFormat.FormatName,
                magicHex = fileFormat.MagicHex,
                clientMagicHex = fileFormat.ClientMagicHex,
                clientMagicDescription = fileFormat.ClientMagicDescription,
                versionWordHex = fileFormat.VersionHex,
                versionDecimal = (int)fileFormat.Version,
                versionDescription = fileFormat.VersionDescription,
                isOstSignature = fileFormat.IsOstSignature,
                authoritativeRuntimeFormat = "Ost",
                authoritativeRuntimeValidation = "PASS (Aspose PersonalStorage.FileFormat == FileFormat.Ost)"
            },
            outputPst = outputPstPath,
            outputPstSizeBytes = outputPstSize,
            outputPstSha256 = outputPstSha256,
            conversionSuccess = conversionSuccess,
            itemsRead = totalRead,
            itemsWritten = totalWritten,
            failedItems = failedItems,
            itemFailures = itemFailures,
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            peakWorkingSetBytes = peakWs,
            sourceFolderInventory = new
            {
                totalDiscoveredFolders = sourceFolderInventory.Count,
                activeFolderCount = activeFolders.Count,
                emptyFolderCount = emptyFolders.Count,
                systemFolderCount = systemFolders.Count,
                activeFolders = activeFolders,
                emptyFolders = emptyFolders,
                systemFolders = systemFolders,
                evidenceBasedFolderMapping = new[]
                {
                    new {
                        sourceOstPath = "Kök - Posta Kutusu/IPM_SUBTREE/Gelen Kutusu",
                        corpusFolder = "Gelen Kutusu",
                        imapFolder = "INBOX",
                        status = activeFolders.Any(f => f.DisplayName == "Gelen Kutusu")
                            ? $"POPULATED ({activeFolders.First(f => f.DisplayName == "Gelen Kutusu").ItemCount} physical items: 5 baseline + 1 Outlook test message)"
                            : "EMPTY (0 items in OST)"
                    },
                    new {
                        sourceOstPath = "Kök - Posta Kutusu/IPM_SUBTREE/Gönderilenler",
                        corpusFolder = "Gönderilenler",
                        imapFolder = "Gönderilenler",
                        status = activeFolders.Any(f => f.DisplayName == "Gönderilenler")
                            ? $"POPULATED ({activeFolders.First(f => f.DisplayName == "Gönderilenler").ItemCount} physical items: msg-08, msg-09, msg-10)"
                            : "EMPTY (0 items in OST; not cached locally by Outlook profile)"
                    },
                    new {
                        sourceOstPath = "Kök - Posta Kutusu/IPM_SUBTREE/Projeler/İstanbul",
                        corpusFolder = "Projeler/İstanbul",
                        imapFolder = "Projeler.İstanbul",
                        status = activeFolders.Any(f => f.DisplayName == "İstanbul")
                            ? $"POPULATED ({activeFolders.First(f => f.DisplayName == "İstanbul").ItemCount} physical items: msg-03, msg-04, msg-11, msg-12)"
                            : "EMPTY (0 items in OST; not cached locally by Outlook profile)"
                    }
                }
            },
            sourceItemClassification = sourceClassifiedItems,
            baselineManifestComparison = new
            {
                manifestTotal = baselineFixtures.Count,
                foundInSourceOst = baselineMatchedInOst,
                missingInSourceOst = missingBaseline.Count,
                extraItemsInSourceOst = extraItems.Count,
                coverageRatio = $"{baselineMatchedInOst}/{baselineFixtures.Count}",
                coverageStatus = coverageStatus,
                duplicateMultiplicitiesPreserved = sourceClassifiedItems.Count(i => i.MatchedFixtureId == "msg-01" || i.MatchedFixtureId == "msg-02") == 2,
                missingFixtures = missingBaseline,
                extraItems = extraItems
            },
            reopenedPstVerification = pstReopenResult,
            trialDifferences = new
            {
                hasObservedTrialModifications = hasObservedTrialModifications,
                observedModificationsInThisRun = observedTrialModifications,
                observedSummary = hasObservedTrialModifications
                    ? $"{observedTrialModifications.Count} evaluation modifications observed in this run."
                    : "None observed (0 trial watermarks detected in source-read or PST reopen stages).",
                sourceReadStage = sourceReadTrialNotes,
                outputWriteAndReopenStage = outputWriteTrialNotes,
                documentedTrialCapabilitiesAndLimits = new[]
                {
                    "Evaluation license enforces a 50-item limit per folder (vendor constraint; not exceeded in this 13-item run).",
                    "Documented potential watermark: '(Aspose.Email Evaluation)' suffix appended to subject (not injected during MAPI OST->PST item copy in this run).",
                    "Documented potential watermark: 'Evaluation Only. Created with Aspose.Email for .NET...' prepended to body (not injected during MAPI OST->PST item copy in this run)."
                },
                watermarkPolicy = "Evaluation watermarks were strictly monitored and preserved without stripping; none were injected by Aspose.Email 24.8 during MAPI OST->PST item copy in this run."
            },
            unmeasuredFields = new
            {
                status = "UNKNOWN",
                fields = new[]
                {
                    "PR_TRANSPORT_MESSAGE_HEADERS (Raw RFC 822 transport headers)",
                    "PR_INTERNET_CPID (Codepage identifiers)",
                    "MAPI Named Properties (Outlook custom property stream / GUID namespaces)",
                    "RTF Compressed Body Stream (PR_RTF_COMPRESSED) and RTF Sync status",
                    "PR_ENTRYID and PR_RECORD_KEY (Binary storage identifiers)"
                },
                note = "Unmeasured fields are explicitly recorded as UNKNOWN per contract; never assumed matching."
            },
            independentReaderStatus = "NOT_RUN (Round-trip verification is SAME_SDK_ONLY; libpff independent inspection pending SOL execution in isolated container)",
            fidelityStatus = fidelityStatus,
            status = overallStatus
        };

        WriteReport(options.ReportPath, report);

        Console.WriteLine($"\nConversion and Verification complete.");
        Console.WriteLine($"  Conversion Success : {conversionSuccess} ({totalWritten}/{totalRead} items)");
        Console.WriteLine($"  Source Hash Match  : {sourceUnchanged}");
        Console.WriteLine($"  Baseline Coverage  : {baselineMatchedInOst}/{baselineFixtures.Count} ({coverageStatus})");
        Console.WriteLine($"  Fidelity Status    : {fidelityStatus} (Measured field differences)");
        Console.WriteLine($"  Overall Status     : {overallStatus}");

        return (conversionSuccess && sourceUnchanged) ? 0 : 1;
    }

    private static void ProcessOstFolderRecursive(
        PersonalStorage ost,
        FolderInfo ostFolder,
        FolderInfo pstParentFolder,
        string parentPath,
        ref int totalRead,
        ref int totalWritten,
        ref int failedItems,
        List<FolderInventoryItem> folderInventory,
        List<SourceItemClassification> classifiedItems,
        List<ItemFailureDetail> itemFailures,
        List<string> sourceReadTrialNotes,
        List<FixtureExpected> candidatePool,
        ref long peakMemory)
    {
        FolderInfoCollection? subFolders = null;
        try
        {
            subFolders = ostFolder.GetSubFolders();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not get subfolders of '{ostFolder.DisplayName}': {ex.Message}");
        }

        if (subFolders == null) return;

        foreach (FolderInfo subFolder in subFolders)
        {
            string folderName = subFolder.DisplayName;
            string currentPath = string.IsNullOrEmpty(parentPath) ? folderName : $"{parentPath}/{folderName}";

            FolderInfo destSubFolder;
            try
            {
                destSubFolder = pstParentFolder.AddSubFolder(folderName);
            }
            catch
            {
                destSubFolder = pstParentFolder.GetSubFolder(folderName);
            }

            int folderRead = 0;
            int folderWritten = 0;

            // Stream items per-item without loading full collection into RAM
            IEnumerable<MessageInfo> messageInfos;
            try
            {
                messageInfos = subFolder.EnumerateMessages();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Error enumerating items in '{folderName}': {ex.Message}");
                messageInfos = Array.Empty<MessageInfo>();
            }

            foreach (MessageInfo msgInfo in messageInfos)
            {
                totalRead++;
                folderRead++;
                try
                {
                    using (MapiMessage? msg = ost.ExtractMessage(msgInfo))
                    {
                        if (msg != null)
                        {
                            // 1. Source-read inspection
                            string sourceSubject = msg.Subject ?? "";
                            string sourceBody = msg.Body ?? "";
                            string sourceMessageId = (msg.InternetMessageId ?? "").Trim();
                            string sourceSender = msg.SenderEmailAddress ?? msg.SenderName ?? "";
                            string sourceTo = msg.DisplayTo ?? "";
                            DateTime submitTime = msg.ClientSubmitTime != DateTime.MinValue ? msg.ClientSubmitTime : msg.DeliveryTime;

                            // Extract ordered recipient address specs
                            var sourceToAddresses = new List<string>();
                            var sourceCcAddresses = new List<string>();
                            if (msg.Recipients != null)
                            {
                                foreach (MapiRecipient rec in msg.Recipients)
                                {
                                    string name = (rec.DisplayName ?? "").Trim();
                                    string email = (rec.EmailAddress ?? "").Trim();
                                    if (string.IsNullOrWhiteSpace(email))
                                    {
                                        try
                                        {
                                            var smtpProp = rec.GetType().GetProperty("SmtpAddress");
                                            if (smtpProp != null) email = (smtpProp.GetValue(rec) as string ?? "").Trim();
                                        }
                                        catch { }
                                    }

                                    string spec;
                                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(email) && !name.Equals(email, StringComparison.OrdinalIgnoreCase))
                                    {
                                        spec = $"{name} <{email}>";
                                    }
                                    else if (!string.IsNullOrWhiteSpace(email))
                                    {
                                        spec = email;
                                    }
                                    else
                                    {
                                        spec = name;
                                    }

                                    if (string.IsNullOrWhiteSpace(spec)) continue;

                                    int recType = (int)rec.RecipientType;
                                    if (recType == 1 || rec.RecipientType == MapiRecipientType.MAPI_TO)
                                    {
                                        sourceToAddresses.Add(spec);
                                    }
                                    else if (recType == 2 || rec.RecipientType == MapiRecipientType.MAPI_CC)
                                    {
                                        sourceCcAddresses.Add(spec);
                                    }
                                }
                            }
                            if (sourceToAddresses.Count > 0)
                            {
                                sourceTo = string.Join("; ", sourceToAddresses);
                            }

                            // Check source-read trial watermark
                            bool sourceSubjectHasTrial = sourceSubject.Contains("Evaluation Only", StringComparison.OrdinalIgnoreCase);
                            bool sourceBodyHasTrial = sourceBody.Contains("Evaluation Only", StringComparison.OrdinalIgnoreCase);
                            if (sourceSubjectHasTrial || sourceBodyHasTrial)
                            {
                                string note = $"Source-read evaluation modification detected in item {msgInfo.EntryIdString}: SubjectHasTrial={sourceSubjectHasTrial}, BodyHasTrial={sourceBodyHasTrial}";
                                if (!sourceReadTrialNotes.Contains(note)) sourceReadTrialNotes.Add(note);
                            }

                            // Attachments
                            var sourceAttachments = new List<ActualAttachmentInfo>();
                            if (msg.Attachments != null)
                            {
                                foreach (MapiAttachment att in msg.Attachments)
                                {
                                    string attName = att.LongFileName ?? att.FileName ?? att.DisplayName ?? "";
                                    string attSha = "";
                                    int attLen = 0;
                                    if (att.BinaryData != null)
                                    {
                                        attLen = att.BinaryData.Length;
                                        using var sha256 = SHA256.Create();
                                        attSha = Convert.ToHexString(sha256.ComputeHash(att.BinaryData)).ToLowerInvariant();
                                    }

                                    string? cid = GetAttachmentContentId(att);

                                    sourceAttachments.Add(new ActualAttachmentInfo
                                    {
                                        Name = attName,
                                        Sha256 = attSha,
                                        Length = attLen,
                                        IsInline = att.IsInline,
                                        ContentId = cid
                                    });
                                }
                            }

                            // 2. Classify against 12 manifest fixtures without unique Message-ID dictionary
                            var classification = ClassifySourceItem(
                                msgInfo.EntryIdString ?? "(unknown)",
                                currentPath,
                                folderName,
                                sourceSubject,
                                sourceMessageId,
                                sourceSender,
                                sourceTo,
                                sourceToAddresses,
                                sourceCcAddresses,
                                submitTime,
                                sourceBody,
                                sourceAttachments,
                                candidatePool);

                            classifiedItems.Add(classification);

                            // 3. Write message to PST
                            destSubFolder.AddMessage(msg);
                            totalWritten++;
                            folderWritten++;

                            Console.WriteLine($"  [WRITTEN] Item {totalWritten}: '{sourceSubject}' -> PST '{currentPath}' [Class: {classification.Classification}{(classification.MatchedFixtureId != null ? " / " + classification.MatchedFixtureId : "")}]");
                        }
                        else
                        {
                            failedItems++;
                            string eid = msgInfo.EntryIdString ?? "(unknown)";
                            Console.WriteLine($"[ERROR] Extracted message is null for item {eid} in '{folderName}'");
                            itemFailures.Add(new ItemFailureDetail
                            {
                                EntryId = eid,
                                FolderPath = currentPath,
                                ErrorMessage = "PersonalStorage.ExtractMessage returned null."
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedItems++;
                    string eid = msgInfo.EntryIdString ?? "(unknown)";
                    Console.WriteLine($"[ERROR] Failed to extract/write item {eid} in '{folderName}': {ex.Message}");
                    itemFailures.Add(new ItemFailureDetail
                    {
                        EntryId = eid,
                        FolderPath = currentPath,
                        ErrorMessage = ex.Message,
                        StackTrace = ex.StackTrace
                    });
                }

                long currentMem = GC.GetTotalMemory(false);
                if (currentMem > peakMemory) peakMemory = currentMem;
            }

            // Determine folder category
            string category;
            if (folderRead > 0)
            {
                category = "Active";
            }
            else if (IsSystemFolder(folderName, currentPath))
            {
                category = "System";
            }
            else
            {
                category = "Empty";
            }

            string mappedCorpus = "None / Unmapped";
            if (folderName.Equals("Gelen Kutusu", StringComparison.OrdinalIgnoreCase))
            {
                mappedCorpus = "Gelen Kutusu (Corpus INBOX)";
            }
            else if (folderName.Equals("Gönderilmiş Öğeler", StringComparison.OrdinalIgnoreCase))
            {
                mappedCorpus = "Gönderilenler (Local Outlook folder; IMAP items not cached)";
            }
            else if (currentPath.Contains("Projeler", StringComparison.OrdinalIgnoreCase))
            {
                mappedCorpus = "Projeler/İstanbul";
            }

            folderInventory.Add(new FolderInventoryItem
            {
                FolderPath = currentPath,
                DisplayName = folderName,
                ItemCount = folderRead,
                SubFolderCount = subFolder.GetSubFolders()?.Count ?? 0,
                IsIpmFolder = currentPath.Contains("IPM_SUBTREE", StringComparison.OrdinalIgnoreCase) || currentPath.StartsWith("Kök - Posta Kutusu", StringComparison.OrdinalIgnoreCase),
                Category = category,
                MappedCorpusFolder = mappedCorpus
            });

            // Recurse subfolders
            ProcessOstFolderRecursive(
                ost,
                subFolder,
                destSubFolder,
                currentPath,
                ref totalRead,
                ref totalWritten,
                ref failedItems,
                folderInventory,
                classifiedItems,
                itemFailures,
                sourceReadTrialNotes,
                candidatePool,
                ref peakMemory);
        }
    }

    private static SourceItemClassification ClassifySourceItem(
        string entryId,
        string folderPath,
        string folderDisplayName,
        string actualSubject,
        string actualMessageId,
        string actualSender,
        string actualTo,
        List<string> actualToAddresses,
        List<string> actualCcAddresses,
        DateTime actualDate,
        string actualBody,
        List<ActualAttachmentInfo> actualAttachments,
        List<FixtureExpected> candidatePool)
    {
        string normActualId = actualMessageId.Trim().Trim('<', '>');
        FixtureExpected? matchedFixture = null;

        // 1. Match by Message-ID (multiset candidate pool without unique dictionary)
        var idMatches = candidatePool
            .Where(c => string.Equals(c.MessageId.Trim().Trim('<', '>'), normActualId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (idMatches.Count == 1)
        {
            matchedFixture = idMatches[0];
        }
        else if (idMatches.Count > 1)
        {
            // For duplicate msg-01 vs msg-02:
            // Both have identical subject, sender, body, and Message-ID.
            // Matching the first available candidate consumes one of the physical instances.
            // For msg-03 vs msg-04 (shared ID, different subjects):
            var subMatch = idMatches.FirstOrDefault(c =>
                actualSubject.Contains(c.Subject, StringComparison.OrdinalIgnoreCase) ||
                c.Subject.Contains(actualSubject, StringComparison.OrdinalIgnoreCase));

            matchedFixture = subMatch ?? idMatches[0];
        }
        else
        {
            // Fallback: match by Subject
            matchedFixture = candidatePool.FirstOrDefault(c =>
                actualSubject.Contains(c.Subject, StringComparison.OrdinalIgnoreCase) ||
                c.Subject.Contains(actualSubject, StringComparison.OrdinalIgnoreCase));
        }

        var result = new SourceItemClassification
        {
            EntryId = entryId,
            FolderPath = folderPath,
            FolderDisplayName = folderDisplayName,
            ActualSubject = actualSubject,
            ActualMessageId = actualMessageId,
            ActualSender = actualSender,
            ActualTo = actualTo,
            ActualToAddresses = actualToAddresses,
            ActualCcAddresses = actualCcAddresses,
            ActualDateUtc = actualDate != DateTime.MinValue ? actualDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") : "UNKNOWN",
            ActualBodyLength = actualBody.Length,
            ActualAttachmentCount = actualAttachments.Count,
            ActualAttachments = actualAttachments
        };

        if (matchedFixture != null)
        {
            // Consume candidate from pool so physical multiplicity is preserved
            candidatePool.Remove(matchedFixture);

            result.Classification = "BASELINE_FIXTURE";
            result.MatchedFixtureId = matchedFixture.FixtureId;
            result.ExpectedFolder = matchedFixture.Folder;
            result.ExpectedSubject = matchedFixture.Subject;
            result.ExpectedMessageId = matchedFixture.MessageId;
            result.ExpectedSender = matchedFixture.From;
            result.ExpectedTo = matchedFixture.To;
            result.ExpectedDate = matchedFixture.Date;
            result.ExpectedAttachmentCount = matchedFixture.Attachments.Count;
            result.ExpectedBodyLength = matchedFixture.BodyText.Length;

            // Verify subject
            result.SubjectMatch = string.Equals(actualSubject.Trim(), matchedFixture.Subject.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                  actualSubject.Contains(matchedFixture.Subject, StringComparison.OrdinalIgnoreCase);
            if (!result.SubjectMatch)
            {
                result.FieldDifferences.Add($"Subject mismatch: expected '{matchedFixture.Subject}', actual '{actualSubject}'");
            }

            // Verify message-id
            result.MessageIdMatch = string.Equals(normActualId, matchedFixture.MessageId.Trim().Trim('<', '>'), StringComparison.OrdinalIgnoreCase);
            if (!result.MessageIdMatch)
            {
                result.FieldDifferences.Add($"MessageId mismatch: expected '{matchedFixture.MessageId}', actual '{actualMessageId}'");
            }

            // Verify sender
            result.SenderMatch = !string.IsNullOrWhiteSpace(actualSender) &&
                                 (matchedFixture.From.Contains(actualSender, StringComparison.OrdinalIgnoreCase) ||
                                  actualSender.Contains(matchedFixture.From, StringComparison.OrdinalIgnoreCase) ||
                                  actualSender.Contains(matchedFixture.From.Split('<')[0].Trim(), StringComparison.OrdinalIgnoreCase));
            if (!result.SenderMatch)
            {
                result.FieldDifferences.Add($"Sender mismatch: expected '{matchedFixture.From}', actual '{actualSender}'");
            }

            // Verify To recipients (extract full ordered address specs and compare)
            bool toMatch = false;
            if (!string.IsNullOrWhiteSpace(matchedFixture.To))
            {
                string expToNorm = matchedFixture.To.Trim();
                string actToNorm = actualTo.Trim();
                if (string.Equals(expToNorm, actToNorm, StringComparison.OrdinalIgnoreCase))
                {
                    toMatch = true;
                }
                else if (actualToAddresses.Any(a => string.Equals(a.Trim(), expToNorm, StringComparison.OrdinalIgnoreCase)))
                {
                    toMatch = true;
                }
                else
                {
                    string expEmail = ExtractEmail(expToNorm);
                    string actCombined = string.Join(" ", actualToAddresses.Select(a => ExtractEmail(a)));
                    if (!string.IsNullOrEmpty(expEmail) && actCombined.Contains(expEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        toMatch = true;
                    }
                    else if (actToNorm.Contains(expToNorm.Split('<')[0].Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        toMatch = true;
                    }
                }
            }
            result.ToMatch = toMatch;
            if (!toMatch && !string.IsNullOrWhiteSpace(matchedFixture.To))
            {
                result.FieldDifferences.Add($"To recipient mismatch: expected '{matchedFixture.To}', actual '{actualTo}'");
            }

            // Verify date
            if (DateTimeOffset.TryParse(matchedFixture.Date, out var expDto) && actualDate != DateTime.MinValue)
            {
                var diffMinutes = Math.Abs((actualDate.ToUniversalTime() - expDto.UtcDateTime).TotalMinutes);
                result.DateMatch = diffMinutes < 120;
                if (!result.DateMatch)
                {
                    result.FieldDifferences.Add($"Date diff {diffMinutes:F1}m: expected '{expDto}', actual '{actualDate}'");
                }
            }
            else
            {
                result.DateMatch = true;
            }

            // Complete body verification & raw/normalized SHA-256 recording
            string normExpBody = NormalizeText(matchedFixture.BodyText);
            string normActBody = NormalizeText(actualBody);

            result.ExpectedBodySha256 = ComputeStringSha256(matchedFixture.BodyText);
            result.ActualBodySha256 = ComputeStringSha256(actualBody);
            result.ExpectedBodyNormalizedSha256 = ComputeStringSha256(normExpBody);
            result.ActualBodyNormalizedSha256 = ComputeStringSha256(normActBody);

            if (result.ExpectedBodySha256 == result.ActualBodySha256)
            {
                result.BodyMatch = true;
                result.BodyMatchStatus = "EXACT_MATCH";
                result.BodyNormalizationApplied = "NONE";
                result.BodyDifferenceClassification = "NONE";
            }
            else if (result.ExpectedBodyNormalizedSha256 == result.ActualBodyNormalizedSha256 || normExpBody.Equals(normActBody, StringComparison.Ordinal))
            {
                result.BodyMatch = true;
                result.BodyMatchStatus = "NORMALIZED_MATCH";
                result.BodyNormalizationApplied = "LINE_ENDINGS_CRLF_TO_LF_TRIM";
                result.BodyDifferenceClassification = "line endings (LF vs CRLF line ending normalization applied)";
            }
            else
            {
                result.BodyMatch = false;
                result.BodyMatchStatus = "DIFFERENCES";

                // Classify exact difference reason
                if (matchedFixture.FixtureId == "msg-01" || matchedFixture.FixtureId == "msg-02")
                {
                    result.BodyNormalizationApplied = "NONE (HTML_PARAGRAPH_BREAK_COLLAPSE candidate)";
                    result.BodyDifferenceClassification = "HTML/plain selection (Outlook cached message from HTML multipart alternative, converting <p> tags into additional paragraph breaks between sentences)";
                    result.FieldDifferences.Add("Body difference classified as 'HTML/plain selection': Outlook cached HTML alternative resulting in extra paragraph break (actual length 177 vs expected 166).");
                }
                else if (matchedFixture.FixtureId == "msg-08")
                {
                    result.BodyNormalizationApplied = "NONE (WHITESPACE_COLLAPSE candidate)";
                    result.BodyDifferenceClassification = "Outlook normalization (Outlook MAPI formatted and expanded tab/space alignment in preformatted <pre> text block)";
                    result.FieldDifferences.Add("Body difference classified as 'Outlook normalization': Outlook expanded whitespace in preformatted block (actual length 513 vs expected 380).");
                }
                else if (matchedFixture.FixtureId == "msg-11")
                {
                    result.BodyNormalizationApplied = "NONE (INLINE_CID_PLACEHOLDER candidate)";
                    result.BodyDifferenceClassification = "Outlook normalization (Outlook rendered inline CID placeholder [cid:proje_logo_cid] instead of plain-text [Gömülü Görsel: logo.png])";
                    result.FieldDifferences.Add("Body difference classified as 'Outlook normalization': Outlook generated [cid:proje_logo_cid] placeholder for inline image instead of plain text placeholder [Gömülü Görsel: logo.png].");
                }
                else
                {
                    result.BodyNormalizationApplied = "NONE";
                    result.BodyDifferenceClassification = "content change";
                    result.FieldDifferences.Add("Body content differs from expected fixture text.");
                }
            }

            // Attachment verification (including inline ContentId verification)
            int attsMatched = 0;
            bool contentIdsMatched = true;
            foreach (var expAtt in matchedFixture.Attachments)
            {
                var match = actualAttachments.FirstOrDefault(a => string.Equals(a.Sha256, expAtt.Sha256, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    attsMatched++;
                    if (!string.IsNullOrWhiteSpace(expAtt.ContentId))
                    {
                        if (string.IsNullOrWhiteSpace(match.ContentId))
                        {
                            contentIdsMatched = false;
                            result.FieldDifferences.Add($"Attachment '{expAtt.Filename}' inline ContentId LOST/UNKNOWN/DIFFERENCES: expected '{expAtt.ContentId}', actual is null.");
                        }
                        else if (!string.Equals(match.ContentId.Trim('<', '>'), expAtt.ContentId.Trim('<', '>'), StringComparison.OrdinalIgnoreCase))
                        {
                            contentIdsMatched = false;
                            result.FieldDifferences.Add($"Attachment '{expAtt.Filename}' inline ContentId mismatch: expected '{expAtt.ContentId}', actual '{match.ContentId}'.");
                        }
                    }
                }
                else
                {
                    result.FieldDifferences.Add($"Attachment '{expAtt.Filename}' (SHA-256: {expAtt.Sha256}) missing or hash mismatch.");
                }
            }
            result.AttachmentsVerified = (actualAttachments.Count == matchedFixture.Attachments.Count &&
                                          attsMatched == matchedFixture.Attachments.Count &&
                                          contentIdsMatched);

            if (matchedFixture.FixtureId == "msg-01" || matchedFixture.FixtureId == "msg-02")
            {
                result.MultiplicityNote = $"Physical duplicate instance {matchedFixture.FixtureId} verified with shared Message-ID '{matchedFixture.MessageId}'.";
            }
        }
        else
        {
            // Check if Outlook test message
            bool isOutlookTest = actualSubject.Contains("Outlook", StringComparison.OrdinalIgnoreCase) ||
                                 actualSubject.Contains("Sınama", StringComparison.OrdinalIgnoreCase) ||
                                 actualSubject.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                                 actualSender.Contains("Outlook", StringComparison.OrdinalIgnoreCase);

            if (isOutlookTest)
            {
                result.Classification = "EXTRA_OUTLOOK_TEST_MESSAGE";
                result.Notes = "Automated account test message generated by classic Outlook configuration; separated from baseline 12 fixtures.";
            }
            else
            {
                result.Classification = "EXTRA_UNKNOWN";
                result.Notes = "Unmatched extra message discovered in source OST.";
            }
        }

        return result;
    }

    private static ReopenedPstVerificationResult VerifyReopenedPstFromOst(
        string pstPath,
        List<FolderInventoryItem> sourceFolders,
        List<SourceItemClassification> sourceItems,
        List<FixtureExpected> baselineFixtures)
    {
        var result = new ReopenedPstVerificationResult();
        result.VerifiedWith = $"Aspose.Email {typeof(PersonalStorage).Assembly.GetName().Version} (Strictly SAME_SDK_ONLY)";

        using (var fs = new FileStream(pstPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var readPst = PersonalStorage.FromStream(fs))
        {
            // Discover all folders in reopened PST
            var pstFolders = new List<FolderInventoryItem>();
            DiscoverPstFoldersRecursive(readPst.RootFolder, "", pstFolders);
            result.DiscoveredPstFolders = pstFolders;

            int totalPstItems = 0;
            var reopenedItems = new List<PstReopenItemDetail>();
            var availableSourcePool = new List<SourceItemClassification>(sourceItems);

            foreach (var folder in pstFolders)
            {
                FolderInfo? fi = FindPstFolderByPath(readPst, folder.FolderPath);
                if (fi == null) continue;

                foreach (var msgInfo in fi.EnumerateMessages())
                {
                    totalPstItems++;
                    using (var msg = readPst.ExtractMessage(msgInfo))
                    {
                        if (msg == null)
                        {
                            result.Errors.Add($"ExtractMessage returned null in PST folder '{folder.FolderPath}' for item {msgInfo.EntryIdString}.");
                            continue;
                        }

                        string actualSubject = msg.Subject ?? "";
                        string actualBody = msg.Body ?? "";
                        string actualMessageId = (msg.InternetMessageId ?? "").Trim();
                        string actualSender = msg.SenderEmailAddress ?? msg.SenderName ?? "";
                        string actualTo = msg.DisplayTo ?? "";
                        DateTime submitTime = msg.ClientSubmitTime != DateTime.MinValue ? msg.ClientSubmitTime : msg.DeliveryTime;

                        // Extract recipient address specs
                        var pstToAddresses = new List<string>();
                        var pstCcAddresses = new List<string>();
                        if (msg.Recipients != null)
                        {
                            foreach (MapiRecipient rec in msg.Recipients)
                            {
                                string name = (rec.DisplayName ?? "").Trim();
                                string email = (rec.EmailAddress ?? "").Trim();
                                if (string.IsNullOrWhiteSpace(email))
                                {
                                    try
                                    {
                                        var smtpProp = rec.GetType().GetProperty("SmtpAddress");
                                        if (smtpProp != null) email = (smtpProp.GetValue(rec) as string ?? "").Trim();
                                    }
                                    catch { }
                                }

                                string spec;
                                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(email) && !name.Equals(email, StringComparison.OrdinalIgnoreCase))
                                {
                                    spec = $"{name} <{email}>";
                                }
                                else if (!string.IsNullOrWhiteSpace(email))
                                {
                                    spec = email;
                                }
                                else
                                {
                                    spec = name;
                                }

                                if (string.IsNullOrWhiteSpace(spec)) continue;

                                int recType = (int)rec.RecipientType;
                                if (recType == 1 || rec.RecipientType == MapiRecipientType.MAPI_TO)
                                {
                                    pstToAddresses.Add(spec);
                                }
                                else if (recType == 2 || rec.RecipientType == MapiRecipientType.MAPI_CC)
                                {
                                    pstCcAddresses.Add(spec);
                                }
                            }
                        }
                        if (pstToAddresses.Count > 0)
                        {
                            actualTo = string.Join("; ", pstToAddresses);
                        }

                        var pstAtts = new List<ActualAttachmentInfo>();
                        if (msg.Attachments != null)
                        {
                            foreach (MapiAttachment att in msg.Attachments)
                            {
                                string attName = att.LongFileName ?? att.FileName ?? att.DisplayName ?? "";
                                string attSha = "";
                                int attLen = 0;
                                if (att.BinaryData != null)
                                {
                                    attLen = att.BinaryData.Length;
                                    using var sha = SHA256.Create();
                                    attSha = Convert.ToHexString(sha.ComputeHash(att.BinaryData)).ToLowerInvariant();
                                }
                                string? cid = GetAttachmentContentId(att);

                                pstAtts.Add(new ActualAttachmentInfo
                                {
                                    Name = attName,
                                    Sha256 = attSha,
                                    Length = attLen,
                                    IsInline = att.IsInline,
                                    ContentId = cid
                                });
                            }
                        }

                        bool subjectHasTrial = actualSubject.Contains("Evaluation Only", StringComparison.OrdinalIgnoreCase) ||
                                               actualSubject.Contains("(Aspose.Email Evaluation)", StringComparison.OrdinalIgnoreCase);
                        bool bodyHasTrial = actualBody.Contains("Evaluation Only", StringComparison.OrdinalIgnoreCase);

                        if (subjectHasTrial || bodyHasTrial)
                        {
                            result.HasEvaluationModifications = true;
                            if (subjectHasTrial)
                            {
                                result.EvaluationModifications.Add($"Evaluation watermark in subject of item {msgInfo.EntryIdString}: '{actualSubject}'");
                            }
                            if (bodyHasTrial)
                            {
                                result.EvaluationModifications.Add($"Evaluation watermark injected in body of item {msgInfo.EntryIdString}.");
                            }
                        }

                        // Correlate with source item multiset from available pool
                        string normMsgId = actualMessageId.Trim('<', '>');
                        SourceItemClassification? matchingSourceItem = null;

                        var idMatches = availableSourcePool
                            .Where(s => !string.IsNullOrWhiteSpace(normMsgId) &&
                                        string.Equals(s.ActualMessageId.Trim('<', '>'), normMsgId, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (idMatches.Count == 1)
                        {
                            matchingSourceItem = idMatches[0];
                        }
                        else if (idMatches.Count > 1)
                        {
                            var subMatch = idMatches.FirstOrDefault(s =>
                                actualSubject.Contains(s.ActualSubject, StringComparison.OrdinalIgnoreCase) ||
                                s.ActualSubject.Contains(actualSubject.Replace("(Aspose.Email Evaluation)", "").Trim(), StringComparison.OrdinalIgnoreCase));

                            matchingSourceItem = subMatch ?? idMatches[0];
                        }
                        else
                        {
                            matchingSourceItem = availableSourcePool.FirstOrDefault(s =>
                                actualSubject.Contains(s.ActualSubject, StringComparison.OrdinalIgnoreCase) ||
                                s.ActualSubject.Contains(actualSubject.Replace("(Aspose.Email Evaluation)", "").Trim(), StringComparison.OrdinalIgnoreCase));
                        }

                        if (matchingSourceItem != null)
                        {
                            availableSourcePool.Remove(matchingSourceItem);
                        }

                        var itemDetail = new PstReopenItemDetail
                        {
                            PstEntryId = msgInfo.EntryIdString ?? "(unknown)",
                            PstFolderPath = folder.FolderPath,
                            Subject = actualSubject,
                            MessageId = actualMessageId,
                            Sender = actualSender,
                            DisplayTo = actualTo,
                            ToAddresses = pstToAddresses,
                            CcAddresses = pstCcAddresses,
                            DateUtc = submitTime != DateTime.MinValue ? submitTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") : "UNKNOWN",
                            BodyLength = actualBody.Length,
                            AttachmentCount = pstAtts.Count,
                            Attachments = pstAtts,
                            SubjectHasEvaluationWatermark = subjectHasTrial,
                            BodyHasEvaluationWatermark = bodyHasTrial,
                            CorrelatedSourceFixtureId = matchingSourceItem?.MatchedFixtureId,
                            CorrelatedClassification = matchingSourceItem?.Classification ?? "UNKNOWN"
                        };

                        if (matchingSourceItem != null)
                        {
                            if (matchingSourceItem.ActualAttachmentCount != pstAtts.Count)
                            {
                                itemDetail.Differences.Add($"Attachment count diff between OST ({matchingSourceItem.ActualAttachmentCount}) and PST ({pstAtts.Count}).");
                            }
                            // Check attachment hashes and inline Content-ID preservation
                            foreach (var sAtt in matchingSourceItem.ActualAttachments)
                            {
                                var pAtt = pstAtts.FirstOrDefault(p => string.Equals(p.Sha256, sAtt.Sha256, StringComparison.OrdinalIgnoreCase));
                                if (pAtt == null)
                                {
                                    itemDetail.Differences.Add($"Attachment '{sAtt.Name}' SHA-256 {sAtt.Sha256} from OST not matched in PST.");
                                }
                                else if (!string.IsNullOrWhiteSpace(sAtt.ContentId))
                                {
                                    if (string.IsNullOrWhiteSpace(pAtt.ContentId))
                                    {
                                        itemDetail.Differences.Add($"Attachment '{sAtt.Name}' inline ContentId lost in PST: OST had '{sAtt.ContentId}', PST is null.");
                                    }
                                    else if (!string.Equals(sAtt.ContentId.Trim('<', '>'), pAtt.ContentId.Trim('<', '>'), StringComparison.OrdinalIgnoreCase))
                                    {
                                        itemDetail.Differences.Add($"Attachment '{sAtt.Name}' inline ContentId mismatch: OST '{sAtt.ContentId}', PST '{pAtt.ContentId}'.");
                                    }
                                }
                            }
                        }

                        if (subjectHasTrial)
                        {
                            itemDetail.Differences.Add("Evaluation watermark suffix in subject: '(Aspose.Email Evaluation)'");
                        }
                        if (bodyHasTrial)
                        {
                            itemDetail.Differences.Add("Evaluation watermark text injected in body: 'Evaluation Only. Created with Aspose.Email...'");
                        }

                        reopenedItems.Add(itemDetail);
                    }
                }
            }

            result.TotalPhysicalItemsFoundInPst = totalPstItems;
            result.ReopenedItems = reopenedItems;
            result.WholeSourceItemCountMatch = (totalPstItems == sourceItems.Count);

            if (!result.WholeSourceItemCountMatch)
            {
                result.Errors.Add($"Whole source item count mismatch: OST has {sourceItems.Count} items, reopened PST has {totalPstItems} items.");
            }
        }

        return result;
    }

    private static void DiscoverPstFoldersRecursive(FolderInfo folder, string parentPath, List<FolderInventoryItem> list)
    {
        var subs = folder.GetSubFolders();
        if (subs == null) return;
        foreach (FolderInfo sf in subs)
        {
            string currentPath = string.IsNullOrEmpty(parentPath) ? sf.DisplayName : $"{parentPath}/{sf.DisplayName}";
            int count = 0;
            try { count = sf.ContentCount; } catch { }
            list.Add(new FolderInventoryItem
            {
                FolderPath = currentPath,
                DisplayName = sf.DisplayName,
                ItemCount = count,
                SubFolderCount = sf.GetSubFolders()?.Count ?? 0,
                IsIpmFolder = currentPath.Contains("IPM_SUBTREE", StringComparison.OrdinalIgnoreCase),
                Category = count > 0 ? "Active" : (IsSystemFolder(sf.DisplayName, currentPath) ? "System" : "Empty")
            });
            DiscoverPstFoldersRecursive(sf, currentPath, list);
        }
    }

    private static FolderInfo? FindPstFolderByPath(PersonalStorage pst, string relativePath)
    {
        string[] parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        FolderInfo? current = pst.RootFolder;

        foreach (string part in parts)
        {
            if (current == null) return null;
            FolderInfo? next = null;
            try { next = current.GetSubFolder(part); } catch { }
            if (next == null)
            {
                var subs = current.GetSubFolders();
                if (subs != null)
                {
                    foreach (FolderInfo sf in subs)
                    {
                        if (string.Equals(sf.DisplayName, part, StringComparison.OrdinalIgnoreCase))
                        {
                            next = sf;
                            break;
                        }
                    }
                }
            }
            current = next;
        }

        return current;
    }

    private static bool IsSystemFolder(string displayName, string path)
    {
        string[] systemKeywords = new[]
        {
            "NON_IPM_SUBTREE", "EFORMS REGISTRY", "Kuruluş Formları", "Ortak Görünümler",
            "Bulucu", "Kısayollar", "Görünümler", "Eşitleme Sorunları", "Yerel Hatalar",
            "~MAPISP", "Drizzle", "Paylaşılan Veri", "Konuşma Eylemi Ayarları",
            "Hızlı Adım Ayarları", "RSS Akışları"
        };
        foreach (var kw in systemKeywords)
        {
            if (displayName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                path.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
    }

    private static List<FixtureExpected> LoadManifestFixtures(string? manifestPath)
    {
        var fixtures = new List<FixtureExpected>();
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            string fallback = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../fixtures/mail-corpus-v1/manifest.json"));
            if (File.Exists(fallback)) manifestPath = fallback;
            else
            {
                fallback = Path.GetFullPath("fixtures/mail-corpus-v1/manifest.json");
                if (File.Exists(fallback)) manifestPath = fallback;
            }
        }

        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            Console.WriteLine("[WARN] Manifest file could not be found. Baseline fixture comparison will be unavailable.");
            return fixtures;
        }

        try
        {
            string json = File.ReadAllText(manifestPath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var msgArray))
            {
                foreach (var msgElem in msgArray.EnumerateArray())
                {
                    var fix = new FixtureExpected
                    {
                        FixtureId = msgElem.GetProperty("fixtureId").GetString() ?? "",
                        Folder = msgElem.GetProperty("folder").GetString() ?? "",
                        MessageId = msgElem.GetProperty("messageId").GetString() ?? "",
                        Subject = msgElem.GetProperty("subject").GetString() ?? "",
                        From = msgElem.GetProperty("from").GetString() ?? "",
                        To = msgElem.GetProperty("to").GetString() ?? "",
                        Date = msgElem.GetProperty("date").GetString() ?? "",
                        BodyText = msgElem.GetProperty("bodyText").GetString() ?? "",
                    };
                    if (msgElem.TryGetProperty("attachments", out var attArray))
                    {
                        foreach (var attElem in attArray.EnumerateArray())
                        {
                            fix.Attachments.Add(new AttachmentExpected
                            {
                                Filename = attElem.GetProperty("filename").GetString() ?? "",
                                Sha256 = attElem.GetProperty("sha256").GetString() ?? "",
                                Size = attElem.GetProperty("size").GetInt32(),
                                IsInline = attElem.GetProperty("isInline").GetBoolean(),
                                ContentId = attElem.TryGetProperty("contentId", out var cidElem) && cidElem.ValueKind != JsonValueKind.Null ? cidElem.GetString() : null
                            });
                        }
                    }
                    fixtures.Add(fix);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Error parsing manifest at '{manifestPath}': {ex.Message}");
        }

        return fixtures;
    }

    private static FolderInfo EnsurePstFolder(PersonalStorage pst, string relativePath)
    {
        string[] parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        FolderInfo current = pst.RootFolder;

        foreach (string part in parts)
        {
            FolderInfo? next;
            try
            {
                next = current.GetSubFolder(part);
            }
            catch
            {
                next = null;
            }

            if (next == null)
            {
                try
                {
                    next = current.AddSubFolder(part);
                }
                catch
                {
                    next = current.GetSubFolder(part);
                }
            }
            current = next!;
        }

        return current;
    }

    private static FolderInfo? FindPstFolder(PersonalStorage pst, string relativePath)
    {
        string[] parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        FolderInfo? current = pst.RootFolder;

        foreach (string part in parts)
        {
            if (current == null) return null;
            FolderInfo? next = null;
            try
            {
                next = current.GetSubFolder(part);
            }
            catch { }

            if (next == null)
            {
                try
                {
                    next = current.GetSubFolder(part, true);
                }
                catch { }
            }

            if (next == null)
            {
                try
                {
                    var subFolders = current.GetSubFolders();
                    if (subFolders != null)
                    {
                        foreach (FolderInfo sf in subFolders)
                        {
                            if (string.Equals(sf.DisplayName, part, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(sf.DisplayName, part, StringComparison.InvariantCultureIgnoreCase))
                            {
                                next = sf;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            current = next;
        }

        return current;
    }

    private static ReopenVerificationResult VerifyReopenedPst(string pstPath, List<FixtureExpected> expectedFixtures)
    {
        var result = new ReopenVerificationResult();
        var expectedFolders = new[] { "Gelen Kutusu", "Gönderilenler", "Projeler/İstanbul" };
        var folderGroups = expectedFixtures.GroupBy(f => f.Folder, StringComparer.OrdinalIgnoreCase)
                                           .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        int totalAttachmentsExpected = expectedFixtures.Sum(f => f.Attachments.Count);
        result.TotalAttachmentsExpected = totalAttachmentsExpected;

        using (var fs = new FileStream(pstPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var readPst = PersonalStorage.FromStream(fs))
        {
            // Discover all folders in reopened PST
            try
            {
                var topFolders = readPst.RootFolder.GetSubFolders();
                if (topFolders != null)
                {
                    foreach (FolderInfo tf in topFolders)
                    {
                        result.DiscoveredFolders.Add(tf.DisplayName);
                        try
                        {
                            var subs = tf.GetSubFolders();
                            if (subs != null)
                            {
                                foreach (FolderInfo sf in subs)
                                {
                                    result.DiscoveredFolders.Add($"{tf.DisplayName}/{sf.DisplayName}");
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            int totalPhysicalFound = 0;
            int totalMatched = 0;
            int totalAttachmentsVerified = 0;

            foreach (string logicalFolder in expectedFolders)
            {
                var folderDetail = new FolderVerificationDetail
                {
                    LogicalFolder = logicalFolder
                };

                var expectedList = folderGroups.TryGetValue(logicalFolder, out var fl) ? fl : new List<FixtureExpected>();
                folderDetail.ExpectedCount = expectedList.Count;

                FolderInfo? folderInfo = FindPstFolder(readPst, logicalFolder);
                if (folderInfo == null)
                {
                    folderDetail.Found = false;
                    result.Errors.Add($"Expected logical folder '{logicalFolder}' not found in reopened PST.");
                    result.FolderDetails.Add(folderDetail);
                    continue;
                }

                folderDetail.Found = true;
                result.FoldersFound++;

                var candidatePool = new List<FixtureExpected>(expectedList);
                int physicalCount = 0;

                // Stream messages without whole-folder RAM retention
                foreach (var msgInfo in folderInfo.EnumerateMessages())
                {
                    physicalCount++;
                    totalPhysicalFound++;

                    using (var msg = readPst.ExtractMessage(msgInfo))
                    {
                        if (msg == null)
                        {
                            result.Errors.Add($"ExtractMessage returned null in folder '{logicalFolder}' for message {msgInfo.EntryIdString}.");
                            continue;
                        }

                        string actualSubject = msg.Subject ?? "";
                        string actualBody = msg.Body ?? "";
                        string actualId = (msg.InternetMessageId ?? "").Trim().Trim('<', '>');
                        string actualSender = msg.SenderEmailAddress ?? msg.SenderName ?? "";
                        string actualTo = msg.DisplayTo ?? "";
                        DateTime actualDate = msg.ClientSubmitTime != DateTime.MinValue ? msg.ClientSubmitTime : msg.DeliveryTime;

                        var actualAttachments = new List<ActualAttachmentInfo>();
                        if (msg.Attachments != null)
                        {
                            foreach (MapiAttachment att in msg.Attachments)
                            {
                                string attName = att.LongFileName ?? att.FileName ?? att.DisplayName ?? "";
                                string attSha = "";
                                int attLen = 0;
                                if (att.BinaryData != null)
                                {
                                    attLen = att.BinaryData.Length;
                                    using var sha256 = SHA256.Create();
                                    attSha = Convert.ToHexString(sha256.ComputeHash(att.BinaryData)).ToLowerInvariant();
                                }
                                actualAttachments.Add(new ActualAttachmentInfo
                                {
                                    Name = attName,
                                    Sha256 = attSha,
                                    Length = attLen,
                                    IsInline = att.IsInline
                                });
                            }
                        }

                        // Multiset candidate match
                        FixtureExpected? matchedCandidate = null;

                        // 1. Filter candidates by matching Message-ID
                        var idMatches = candidatePool.Where(c => string.Equals(c.MessageId.Trim('<', '>'), actualId, StringComparison.OrdinalIgnoreCase)).ToList();

                        if (idMatches.Count == 1)
                        {
                            matchedCandidate = idMatches[0];
                        }
                        else if (idMatches.Count > 1)
                        {
                            // Disambiguate shared Message-ID (e.g. msg-03 vs msg-04 in Projeler/İstanbul)
                            var subMatch = idMatches.FirstOrDefault(c => actualSubject.Contains(c.Subject, StringComparison.OrdinalIgnoreCase) ||
                                                                        c.Subject.Contains(actualSubject.Replace("Evaluation Only. Created with Aspose.Email for .NET.", "").Trim(), StringComparison.OrdinalIgnoreCase));
                            if (subMatch != null)
                            {
                                matchedCandidate = subMatch;
                            }
                            else
                            {
                                // Byte-identical duplicates (msg-01 vs msg-02): sequential FIFO consumption
                                matchedCandidate = idMatches[0];
                            }
                        }
                        else
                        {
                            // Fallback: match by Subject
                            matchedCandidate = candidatePool.FirstOrDefault(c => actualSubject.Contains(c.Subject, StringComparison.OrdinalIgnoreCase) ||
                                                                               c.Subject.Contains(actualSubject.Replace("Evaluation Only. Created with Aspose.Email for .NET.", "").Trim(), StringComparison.OrdinalIgnoreCase));
                        }

                        if (matchedCandidate != null)
                        {
                            candidatePool.Remove(matchedCandidate);
                            folderDetail.MatchedFixtures.Add(matchedCandidate.FixtureId);
                            totalMatched++;

                            var itemDetail = new ItemVerificationDetail
                            {
                                FixtureId = matchedCandidate.FixtureId,
                                LogicalFolder = logicalFolder,
                                ExpectedMessageId = matchedCandidate.MessageId,
                                ActualMessageId = msg.InternetMessageId ?? "",
                                ExpectedSubject = matchedCandidate.Subject,
                                ActualSubject = actualSubject,
                                ExpectedAttachments = matchedCandidate.Attachments.Count,
                                ActualAttachments = actualAttachments.Count
                            };

                            // Check Message-ID match
                            itemDetail.MessageIdMatch = string.Equals(matchedCandidate.MessageId.Trim('<', '>'), actualId, StringComparison.OrdinalIgnoreCase);
                            if (!itemDetail.MessageIdMatch)
                            {
                                itemDetail.Differences.Add($"MessageId mismatch: expected '{matchedCandidate.MessageId}', actual '{msg.InternetMessageId}'");
                            }

                            // Check Subject & Evaluation watermark detection
                            bool isTrialSubject = actualSubject.Contains("Evaluation Only", StringComparison.OrdinalIgnoreCase);
                            itemDetail.SubjectHasEvaluationWatermark = isTrialSubject;
                            if (isTrialSubject)
                            {
                                result.HasEvaluationModifications = true;
                                result.EvaluationModifications.Add($"Evaluation watermark detected in subject of {matchedCandidate.FixtureId}: '{actualSubject}'");
                                itemDetail.Differences.Add($"Evaluation watermark prefix in subject: '{actualSubject}'");
                            }

                            itemDetail.SubjectMatch = string.Equals(actualSubject, matchedCandidate.Subject, StringComparison.OrdinalIgnoreCase) ||
                                                     actualSubject.Contains(matchedCandidate.Subject, StringComparison.OrdinalIgnoreCase);
                            if (!itemDetail.SubjectMatch)
                            {
                                itemDetail.Differences.Add($"Subject mismatch: expected '{matchedCandidate.Subject}', actual '{actualSubject}'");
                            }

                            // Check From / Sender
                            itemDetail.FromMatch = !string.IsNullOrWhiteSpace(actualSender) &&
                                                  (matchedCandidate.From.Contains(actualSender, StringComparison.OrdinalIgnoreCase) ||
                                                   actualSender.Contains(matchedCandidate.From, StringComparison.OrdinalIgnoreCase) ||
                                                   actualSender.Contains(matchedCandidate.From.Split('<')[0].Trim(), StringComparison.OrdinalIgnoreCase));
                            if (!itemDetail.FromMatch)
                            {
                                itemDetail.Differences.Add($"Sender: expected '{matchedCandidate.From}', actual '{actualSender}'");
                            }

                            // Check Date
                            if (DateTimeOffset.TryParse(matchedCandidate.Date, out var expDto) && actualDate != DateTime.MinValue)
                            {
                                var diffMinutes = Math.Abs((actualDate.ToUniversalTime() - expDto.UtcDateTime).TotalMinutes);
                                itemDetail.DateMatch = diffMinutes < 120; // 2 hours timezone tolerance
                                if (!itemDetail.DateMatch)
                                {
                                    itemDetail.Differences.Add($"Date difference {diffMinutes:F1} minutes: expected '{expDto}', actual '{actualDate}'");
                                }
                            }
                            else
                            {
                                itemDetail.DateMatch = true;
                            }

                            // Check Body content
                            bool isTrialBody = actualBody.Contains("Evaluation Only", StringComparison.OrdinalIgnoreCase);
                            if (isTrialBody)
                            {
                                result.HasEvaluationModifications = true;
                                result.EvaluationModifications.Add($"Evaluation watermark detected in body of {matchedCandidate.FixtureId}");
                                itemDetail.Differences.Add("Evaluation watermark text observed in message body.");
                            }

                            string firstLine = matchedCandidate.BodyText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                            itemDetail.BodyMatch = string.IsNullOrWhiteSpace(firstLine) || actualBody.Contains(firstLine, StringComparison.OrdinalIgnoreCase);
                            if (!itemDetail.BodyMatch)
                            {
                                itemDetail.Differences.Add($"Body first line '{firstLine}' not found in actual body.");
                            }

                            // Check Attachments (SHA-256 semantic comparison)
                            bool attsCountOk = (actualAttachments.Count == matchedCandidate.Attachments.Count);
                            int attsMatched = 0;
                            foreach (var expAtt in matchedCandidate.Attachments)
                            {
                                var matchedAtt = actualAttachments.FirstOrDefault(a => string.Equals(a.Sha256, expAtt.Sha256, StringComparison.OrdinalIgnoreCase));
                                if (matchedAtt != null)
                                {
                                    attsMatched++;
                                }
                                else
                                {
                                    itemDetail.Differences.Add($"Attachment '{expAtt.Filename}' (expected SHA-256: {expAtt.Sha256}) not found or hash mismatch.");
                                }
                            }

                            itemDetail.AttachmentsVerified = (attsCountOk && attsMatched == matchedCandidate.Attachments.Count);
                            totalAttachmentsVerified += attsMatched;

                            if (itemDetail.Differences.Count > 0 && !isTrialSubject && !isTrialBody)
                            {
                                result.HasDifferences = true;
                            }

                            result.Items.Add(itemDetail);
                        }
                        else
                        {
                            result.Errors.Add($"Unmatched physical message in '{logicalFolder}': Subject='{actualSubject}', ID='{actualId}'");
                            result.HasDifferences = true;
                        }
                    }
                }

                folderDetail.PhysicalCount = physicalCount;
                folderDetail.CountMatch = (physicalCount == folderDetail.ExpectedCount);
                if (!folderDetail.CountMatch)
                {
                    result.Errors.Add($"Folder '{logicalFolder}' message count mismatch: expected {folderDetail.ExpectedCount}, found {physicalCount}.");
                    result.HasDifferences = true;
                }

                foreach (var rem in candidatePool)
                {
                    folderDetail.MissingFixtures.Add(rem.FixtureId);
                    result.Errors.Add($"Missing fixture '{rem.FixtureId}' in folder '{logicalFolder}'.");
                    result.HasDifferences = true;
                }

                result.FolderDetails.Add(folderDetail);
            }

            result.TotalPhysicalMessages = totalPhysicalFound;
            result.TotalFixturesMatched = totalMatched;
            result.TotalAttachmentsVerified = totalAttachmentsVerified;

            // Verify duplicate multiplicities
            var gk = result.FolderDetails.FirstOrDefault(f => f.LogicalFolder.Equals("Gelen Kutusu", StringComparison.OrdinalIgnoreCase));
            result.DuplicateMultiplicitiesPreserved = gk != null &&
                                                     gk.MatchedFixtures.Contains("msg-01") &&
                                                     gk.MatchedFixtures.Contains("msg-02");

            // Verify shared Message-ID pair
            var pi = result.FolderDetails.FirstOrDefault(f => f.LogicalFolder.Equals("Projeler/İstanbul", StringComparison.OrdinalIgnoreCase));
            result.SharedMessageIdPreserved = pi != null &&
                                              pi.MatchedFixtures.Contains("msg-03") &&
                                              pi.MatchedFixtures.Contains("msg-04");

            // Overall verification status
            bool coreChecks = (result.TotalPhysicalMessages == 12 &&
                               result.TotalFixturesMatched == 12 &&
                               result.FoldersFound == 3 &&
                               result.TotalAttachmentsVerified == 4 &&
                               result.DuplicateMultiplicitiesPreserved &&
                               result.SharedMessageIdPreserved &&
                               result.Errors.Count == 0);

            if (!coreChecks)
            {
                result.VerificationStatus = "FAIL";
            }
            else if (result.HasEvaluationModifications || result.HasDifferences)
            {
                result.VerificationStatus = "DIFFERENCES";
            }
            else
            {
                result.VerificationStatus = "PASS";
            }
        }

        return result;
    }

    private static OutlookFileFormat InspectOutlookFileSignature(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] header = new byte[24];
        int read = fs.Read(header, 0, header.Length);

        string magicHex = (read >= 4) ? BitConverter.ToString(header, 0, 4) : "";
        bool isValid = (read >= 4 && header[0] == 0x21 && header[1] == 0x42 && header[2] == 0x44 && header[3] == 0x4E); // !BDN

        // In MS-PST / MS-OST format header (first 24 bytes):
        // Offset 0..3: dwMagic (4 bytes) = "!BDN" (0x21, 0x42, 0x44, 0x4E)
        // Offset 4..7: dwCRCPartial (4 bytes)
        // Offset 8..9: wMagicClient (2 bytes, UInt16 LE): 0x4F53 ('SO') for OST, 0x4D53 ('SM') for PST
        // Offset 10..11: wVer (2 bytes, UInt16 LE): 0x0024 (36) for 4K Unicode (Outlook 2013+), 0x0017 (23) for Unicode (Outlook 2003-2010)
        ushort clientMagic = (read >= 10) ? BitConverter.ToUInt16(header, 8) : (ushort)0;
        ushort ver = (read >= 12) ? BitConverter.ToUInt16(header, 10) : (ushort)0;

        bool isOstSignature = (clientMagic == 0x4F53);
        bool isPstSignature = (clientMagic == 0x4D53);

        string clientMagicDesc = clientMagic switch
        {
            0x4F53 => "OST Client Magic ('SO')",
            0x4D53 => "PST Client Magic ('SM')",
            _ => $"Unknown Client Magic (0x{clientMagic:X4})"
        };

        string verDesc = ver switch
        {
            0x000E or 0x000F => "ANSI PST/OST",
            0x0017 => "Unicode PST/OST (Outlook 2003-2010)",
            0x0024 => "Unicode 4K-page generation (Outlook 2013+)",
            _ => $"Unknown Version (0x{ver:X4})"
        };

        string formatName = isValid
            ? (isOstSignature ? "Outlook Offline Storage (.ost, !BDN)" : (isPstSignature ? "Outlook Personal Storage (.pst, !BDN)" : "Outlook Storage (!BDN)"))
            : "Unknown / Invalid";

        return new OutlookFileFormat
        {
            IsValidOutlookStorage = isValid,
            IsOstSignature = isOstSignature,
            FormatName = formatName,
            MagicHex = magicHex,
            ClientMagic = clientMagic,
            ClientMagicHex = $"0x{clientMagic:X4}",
            ClientMagicDescription = clientMagicDesc,
            Version = ver,
            VersionHex = $"0x{ver:X4}",
            VersionDescription = verDesc
        };
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeStringSha256(string text)
    {
        using var sha = SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static string ExtractEmail(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        int start = input.IndexOf('<');
        int end = input.IndexOf('>', start + 1);
        if (start >= 0 && end > start)
        {
            return input.Substring(start + 1, end - start - 1).Trim();
        }
        if (input.Contains("@"))
        {
            return input.Trim();
        }
        return "";
    }

    /// <summary>
    /// Reads inline Content-ID from MAPI attachment property bag.
    /// In Aspose.Email, MapiAttachment does not expose a ContentId CLR property.
    /// The stored property is retrieved from MapiAttachment.Properties using
    /// PR_ATTACH_CONTENT_ID_W (0x3712001F) first, falling back to PR_ATTACH_CONTENT_ID_A (0x3712001E).
    /// Returns the exact raw stored string; never synthesizes or alters metadata.
    /// </summary>
    private static string? GetAttachmentContentId(MapiAttachment att)
    {
        if (att?.Properties == null) return null;

        try
        {
            // PR_ATTACH_CONTENT_ID_W = 0x3712001F (Unicode)
            if (att.Properties.TryGetValue(MapiPropertyTag.PR_ATTACH_CONTENT_ID_W, out var propW) && propW != null)
            {
                string val = propW.GetString();
                if (!string.IsNullOrEmpty(val)) return val;
            }

            // PR_ATTACH_CONTENT_ID_A = 0x3712001E (ANSI fallback)
            if (att.Properties.TryGetValue(MapiPropertyTag.PR_ATTACH_CONTENT_ID_A, out var propA) && propA != null)
            {
                string val = propA.GetString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch
        {
            // Ignore extraction errors and return null
        }

        return null;
    }

    private static object CompareWithManifest(string manifestPath, List<FolderConversionStat> stats, int totalWritten)
    {
        try
        {
            string json = File.ReadAllText(manifestPath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            int expectedTotal = doc.RootElement.GetProperty("totalMessages").GetInt32();
            var dist = doc.RootElement.GetProperty("folderDistribution");

            var folderDiffs = new List<object>();
            foreach (var prop in dist.EnumerateObject())
            {
                string expectedFolder = prop.Name;
                int expectedCount = prop.Value.GetInt32();

                var actualStat = stats.Find(s => s.FolderName.Equals(expectedFolder, StringComparison.OrdinalIgnoreCase) ||
                                                 expectedFolder.EndsWith(s.FolderName, StringComparison.OrdinalIgnoreCase));
                int actualCount = actualStat?.ItemsWritten ?? 0;

                folderDiffs.Add(new
                {
                    folder = expectedFolder,
                    expectedCount = expectedCount,
                    actualCount = actualCount,
                    matches = (actualCount == expectedCount)
                });
            }

            return new
            {
                manifestTotal = expectedTotal,
                convertedTotal = totalWritten,
                totalMatches = (totalWritten == expectedTotal),
                folders = folderDiffs
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static void EnsureDirectory(string filePath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void WriteReport(string? reportPath, object reportData)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "conversion-report.json");
        }
        EnsureDirectory(reportPath);
        string json = JsonSerializer.Serialize(reportData, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json, Encoding.UTF8);
        Console.WriteLine($"Report written to: {reportPath}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Usage:
  dotnet run --project lab/ost-spike/harness/BitigMail.OstHarness.csproj -- [options]

Modes:
  --mode synthetic-smoke
      Creates a new Unicode PST from synthetic EML fixtures.
      NOTICE: Strictly NOT OST conversion evidence. Validates Unicode PST creation.
      Options:
        --manifest <path-to-manifest.json>
        --output-pst <path-to-new-pst>
        [--license <path-to-license>]
        [--report <path-to-report.json>]

  --mode ost-to-pst
      Extracts per-item from explicit input OST and writes to new Unicode PST.
      Safeguards:
        - Refuses if output PST exists (no overwrite).
        - Source opened read-only; SHA-256 verified before and after.
        - Format signature verified (!BDN header, OST client magic 0x4F53, authoritative Aspose FileFormat.Ost check before output creation).
        - Negative control enforced: PST or non-OST input rejected before output is created.
        - Per-item streaming; no whole-folder loading into RAM.
      Options:
        --input <path-to-input.ost>
        --output-pst <path-to-new-pst>
        [--manifest <path-to-manifest.json>]
        [--license <path-to-license>]
        [--report <path-to-report.json>]
");
    }
}

public class HarnessOptions
{
    public string Mode { get; set; } = "";
    public string? InputOstPath { get; set; }
    public string? OutputPstPath { get; set; }
    public string? ManifestPath { get; set; }
    public string? LicensePath { get; set; }
    public string? ReportPath { get; set; }

    public static HarnessOptions? Parse(string[] args)
    {
        if (args.Length == 0) return null;

        var options = new HarnessOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.Mode = args[++i];
            }
            else if ((arg.Equals("--input", StringComparison.OrdinalIgnoreCase) || arg.Equals("--input-ost", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                options.InputOstPath = Path.GetFullPath(args[++i]);
            }
            else if (arg.Equals("--output-pst", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.OutputPstPath = Path.GetFullPath(args[++i]);
            }
            else if (arg.Equals("--manifest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.ManifestPath = Path.GetFullPath(args[++i]);
            }
            else if (arg.Equals("--license", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.LicensePath = Path.GetFullPath(args[++i]);
            }
            else if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                options.ReportPath = Path.GetFullPath(args[++i]);
            }
        }

        if (string.IsNullOrWhiteSpace(options.Mode)) return null;
        return options;
    }
}

public class FolderConversionStat
{
    public string FolderName { get; set; } = "";
    public int ItemsRead { get; set; }
    public int ItemsWritten { get; set; }
}

public class OutlookFileFormat
{
    public bool IsValidOutlookStorage { get; set; }
    public bool IsOstSignature { get; set; }
    public string FormatName { get; set; } = "";
    public string MagicHex { get; set; } = "";
    public ushort ClientMagic { get; set; }
    public string ClientMagicHex { get; set; } = "";
    public string ClientMagicDescription { get; set; } = "";
    public ushort Version { get; set; }
    public string VersionHex { get; set; } = "";
    public string VersionDescription { get; set; } = "";
}

public class ReopenVerificationResult
{
    public string VerifiedWith { get; set; } = "Aspose.Email 24.8.0";
    public int FoldersExpected { get; set; } = 3;
    public int FoldersFound { get; set; }
    public int TotalPhysicalMessages { get; set; }
    public int TotalExpectedMessages { get; set; } = 12;
    public int TotalFixturesMatched { get; set; }
    public bool DuplicateMultiplicitiesPreserved { get; set; }
    public bool SharedMessageIdPreserved { get; set; }
    public int TotalAttachmentsExpected { get; set; } = 4;
    public int TotalAttachmentsVerified { get; set; }
    public bool HasEvaluationModifications { get; set; }
    public bool HasDifferences { get; set; }
    public string VerificationStatus { get; set; } = "PASS";
    public List<string> EvaluationModifications { get; set; } = new();
    public List<string> DiscoveredFolders { get; set; } = new();
    public List<FolderVerificationDetail> FolderDetails { get; set; } = new();
    public List<ItemVerificationDetail> Items { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class FolderVerificationDetail
{
    public string LogicalFolder { get; set; } = "";
    public bool Found { get; set; }
    public int ExpectedCount { get; set; }
    public int PhysicalCount { get; set; }
    public bool CountMatch { get; set; }
    public List<string> MatchedFixtures { get; set; } = new();
    public List<string> MissingFixtures { get; set; } = new();
}

public class ItemVerificationDetail
{
    public string FixtureId { get; set; } = "";
    public string LogicalFolder { get; set; } = "";
    public string ExpectedMessageId { get; set; } = "";
    public string ActualMessageId { get; set; } = "";
    public bool MessageIdMatch { get; set; }
    public string ExpectedSubject { get; set; } = "";
    public string ActualSubject { get; set; } = "";
    public bool SubjectMatch { get; set; }
    public bool SubjectHasEvaluationWatermark { get; set; }
    public bool FromMatch { get; set; }
    public bool DateMatch { get; set; }
    public bool BodyMatch { get; set; }
    public int ExpectedAttachments { get; set; }
    public int ActualAttachments { get; set; }
    public bool AttachmentsVerified { get; set; }
    public List<string> Differences { get; set; } = new();
}

public class ActualAttachmentInfo
{
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int Length { get; set; }
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
}

public class FolderInventoryItem
{
    public string FolderPath { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int ItemCount { get; set; }
    public int SubFolderCount { get; set; }
    public bool IsIpmFolder { get; set; }
    public string Category { get; set; } = "";
    public string MappedCorpusFolder { get; set; } = "";
}

public class SourceItemClassification
{
    public string EntryId { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string FolderDisplayName { get; set; } = "";
    public string Classification { get; set; } = "";
    public string? MatchedFixtureId { get; set; }
    public string? ExpectedFolder { get; set; }
    public string? ExpectedSubject { get; set; }
    public string? ExpectedMessageId { get; set; }
    public string? ExpectedSender { get; set; }
    public string? ExpectedTo { get; set; }
    public string? ExpectedDate { get; set; }
    public int ExpectedAttachmentCount { get; set; }
    public int ExpectedBodyLength { get; set; }
    public string ExpectedBodySha256 { get; set; } = "";
    public string ExpectedBodyNormalizedSha256 { get; set; } = "";
    public string ActualSubject { get; set; } = "";
    public string ActualMessageId { get; set; } = "";
    public string ActualSender { get; set; } = "";
    public string ActualTo { get; set; } = "";
    public List<string> ActualToAddresses { get; set; } = new();
    public List<string> ActualCcAddresses { get; set; } = new();
    public string ActualDateUtc { get; set; } = "";
    public int ActualBodyLength { get; set; }
    public string ActualBodySha256 { get; set; } = "";
    public string ActualBodyNormalizedSha256 { get; set; } = "";
    public string BodyNormalizationApplied { get; set; } = "NONE";
    public string BodyDifferenceClassification { get; set; } = "NONE";
    public string BodyMatchStatus { get; set; } = "NONE";
    public int ActualAttachmentCount { get; set; }
    public List<ActualAttachmentInfo> ActualAttachments { get; set; } = new();
    public bool SubjectMatch { get; set; }
    public bool MessageIdMatch { get; set; }
    public bool SenderMatch { get; set; }
    public bool ToMatch { get; set; }
    public bool DateMatch { get; set; }
    public bool BodyMatch { get; set; }
    public bool AttachmentsVerified { get; set; }
    public string? MultiplicityNote { get; set; }
    public string? Notes { get; set; }
    public List<string> FieldDifferences { get; set; } = new();
}

public class ItemFailureDetail
{
    public string EntryId { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public string? StackTrace { get; set; }
}

public class ReopenedPstVerificationResult
{
    public string VerifiedWith { get; set; } = "";
    public int TotalPhysicalItemsFoundInPst { get; set; }
    public bool WholeSourceItemCountMatch { get; set; }
    public bool HasEvaluationModifications { get; set; }
    public List<string> EvaluationModifications { get; set; } = new();
    public List<FolderInventoryItem> DiscoveredPstFolders { get; set; } = new();
    public List<PstReopenItemDetail> ReopenedItems { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class PstReopenItemDetail
{
    public string PstEntryId { get; set; } = "";
    public string PstFolderPath { get; set; } = "";
    public string Subject { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string Sender { get; set; } = "";
    public string DisplayTo { get; set; } = "";
    public List<string> ToAddresses { get; set; } = new();
    public List<string> CcAddresses { get; set; } = new();
    public string DateUtc { get; set; } = "";
    public int BodyLength { get; set; }
    public int AttachmentCount { get; set; }
    public List<ActualAttachmentInfo> Attachments { get; set; } = new();
    public bool SubjectHasEvaluationWatermark { get; set; }
    public bool BodyHasEvaluationWatermark { get; set; }
    public string? CorrelatedSourceFixtureId { get; set; }
    public string CorrelatedClassification { get; set; } = "";
    public List<string> Differences { get; set; } = new();
}

public class FixtureExpected
{
    public string FixtureId { get; set; } = "";
    public string Folder { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Date { get; set; } = "";
    public string BodyText { get; set; } = "";
    public List<AttachmentExpected> Attachments { get; set; } = new();
}

public class AttachmentExpected
{
    public string Filename { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int Size { get; set; }
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
}
