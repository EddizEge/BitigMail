using Aspose.Email.Storage.Olm;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("source and evidence output required");
string source = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string Hash() { using var s = File.OpenRead(source); return Convert.ToHexString(SHA256.HashData(s)); }
string before = Hash();
var folders = new List<object>();
int messages = 0, attachments = 0, unresolvedAttachments = 0;
var messageClasses = new Dictionary<string, int>(StringComparer.Ordinal);
using (var storage = new OlmStorage(source))
{
    void Visit(IEnumerable<OlmFolder> input, int depth, string parent = "")
    {
        if (depth > 100) throw new InvalidDataException("Folder depth exceeded");
        foreach (var folder in input)
        {
            string folderPath = parent + "/" + folder.Name;
            int count = 0, attachmentCount = 0;
            var attachmentHashes = new List<string>();
            var attachmentSaveResults = new List<object>();
            var mailIdentities = new List<string>();
            var mailFields = new List<object>();
            foreach (var message in storage.EnumerateMessages(folder))
            {
                using (message)
                {
                    count++;
                    string messageClass = message.MessageClass ?? "UNKNOWN";
                    messageClasses[messageClass] = messageClasses.GetValueOrDefault(messageClass) + 1;
                    if (messageClass == "IPM.Note")
                    {
                        string TextHash(string? value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value ?? "")));
                        mailIdentities.Add(TextHash(message.InternetMessageId));
                        mailFields.Add(new { identityHash = TextHash(message.InternetMessageId),
                            sdkSubject = message.Subject, sdkPlain = message.Body, sdkHtml = message.BodyHtml,
                            subjectHash = TextHash(message.Subject), plainHash = TextHash(message.Body), htmlHash = TextHash(message.BodyHtml),
                            plainLfHash = TextHash(message.Body?.Replace("\r\n", "\n").Replace("\r", "\n")),
                            htmlLfHash = TextHash(message.BodyHtml?.Replace("\r\n", "\n").Replace("\r", "\n")),
                            submitTime = message.ClientSubmitTime.ToString("O"), submitKind = message.ClientSubmitTime.Kind.ToString(),
                            deliveryTime = message.DeliveryTime.ToString("O"), deliveryKind = message.DeliveryTime.Kind.ToString()
                        });
                    }
                    foreach (var attachment in message.Attachments)
                    {
                        attachmentCount++;
                        if (attachment.BinaryData is { } bytes)
                            attachmentHashes.Add(Convert.ToHexString(SHA256.HashData(bytes)));
                        else
                        {
                            unresolvedAttachments++;
                            attachmentHashes.Add("UNRESOLVED_BINARY_DATA");
                        }
                        try
                        {
                            using var saved = new MemoryStream();
                            attachment.Save(saved);
                            attachmentSaveResults.Add(new { status = "READ", hash = Convert.ToHexString(SHA256.HashData(saved.ToArray())), bytes = saved.Length, isEmbeddedMessage = attachment.ObjectData?.IsOutlookMessage == true });
                        }
                        catch (Exception ex)
                        {
                            attachmentSaveResults.Add(new { status = "FAILED", errorType = ex.GetType().Name });
                        }
                    }
                }
            }
            messages += count;
            attachments += attachmentCount;
            folders.Add(new { ordinal = folders.Count + 1, folderPath, mailIdentities, mailFields, depth, messages = count, attachments = attachmentCount, attachmentHashes, attachmentSaveResults,
                sampleEntryIds = folder.EnumerateMessages().Take(3).Select(info => info.EntryId).ToArray() });
            Visit(folder.SubFolders, depth + 1, folderPath);
        }
    }
    Visit(storage.FolderHierarchy, 0);
}
string after = Hash();
var evidence = new { status = before != after ? "FAIL" : unresolvedAttachments > 0 ? "READ_WITH_UNRESOLVED_ATTACHMENTS" : "READ_ONLY_PROBE_PASS", sdk = typeof(OlmStorage).Assembly.GetName().Version?.ToString(), before, after, messages, messageClasses, attachments, unresolvedAttachments, folders, qualification = "One public vendor fixture; trial SDK. Items include non-mail classes, not all emails. No conversion, general format, or UI acceptance. Initial probe failed on null attachment BinaryData; this probe records it explicitly." };
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write)) JsonSerializer.Serialize(file, evidence, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine($"Read-only OLM probe: {messages} items, {attachments} attachments, source unchanged: {before == after}");
