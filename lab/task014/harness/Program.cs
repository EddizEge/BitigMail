using System.Security.Cryptography;
using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

string lab = Path.Combine(Directory.GetCurrentDirectory(), "lab", "task014");
int labPort = 4143;
if (args.Contains("--dovecot")) { lab = Path.Combine(lab,"dovecot"); labPort = 5143; }
string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "bitigmail-task014-qa", "gate-a", "run-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);
using var credentials = JsonDocument.Parse(File.ReadAllText(Path.Combine(lab, "local-credentials.json")));
using var seed = JsonDocument.Parse(File.ReadAllText(Path.Combine(lab, "seed-manifest.json")));
using var source = Connect("source");
using var target = Connect("target");
var options = FormatOptions.Default.Clone();
options.NewLineFormat = NewLineFormat.Dos;
options.EnsureNewLine = true; // ImapFolder.CreateAppendOptions enforces this value.
options.HiddenHeaders.Clear();
var observations = new List<Row>();
var payloads = new List<(Row Row, byte[] Raw, MimeMessage Message, MessageFlags Flags, string[] Keywords, DateTimeOffset Date)>();
var mappings = new Dictionary<string, string>();
string targetRootName = "AstraGateA" + Guid.NewGuid().ToString("N")[..12];
string? failure = null;
try
{
    foreach (var entry in seed.RootElement.GetProperty("folderMeta").EnumerateObject())
    {
        string sourcePath = entry.Value.GetProperty("imapMailbox").GetString()!;
        var sf = source.GetFolder(sourcePath);
        sf.Open(FolderAccess.ReadOnly);
        var uids = sf.Search(SearchQuery.All);
        var summaries = sf.Fetch(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Flags | MessageSummaryItems.InternalDate);
        foreach (var summary in summaries)
        {
            byte[] raw = Read(sf, summary.UniqueId);
            using var stream = new MemoryStream(raw, writable: false);
            var message = MimeMessage.Load(stream);
            using var serialized = new MemoryStream();
            message.WriteTo(options, serialized);
            byte[] written = serialized.ToArray();
            var row = new Row { SourceFolder = sourcePath, SourceUidValidity = sf.UidValidity,
                SourceUid = summary.UniqueId.Id, SourceHash = Hash(raw), SourceBytes = raw.Length,
                SerializedHash = Hash(written), SerializedBytes = written.Length,
                SerializationEqual = raw.AsSpan().SequenceEqual(written),
                FirstDifference = FirstDifference(raw,written),
                SourceFlags = (summary.Flags ?? MessageFlags.None).ToString(),
                SourceDate = summary.InternalDate,
                Token = "BitigMail_" + Guid.NewGuid().ToString("N") };
            observations.Add(row);
            if (!summary.InternalDate.HasValue) throw new InvalidOperationException("MissingInternalDate");
            payloads.Add((row, raw, message, (summary.Flags ?? MessageFlags.None) & ~MessageFlags.Recent,
                summary.Keywords.ToArray(), summary.InternalDate.Value));
        }
        sf.Close();
    }
    if (observations.Count != 12) throw new InvalidOperationException("UnexpectedSourceCount");
    if (observations.Any(row => !row.SerializationEqual)) throw new InvalidOperationException("SerializationDiffers");

    var ns = target.GetFolder(target.PersonalNamespaces[0]);
    var root = ns.Create(targetRootName, false) ?? throw new InvalidOperationException("CannotCreateTarget");
    foreach (var group in payloads.GroupBy(p => p.Row.SourceFolder))
    {
        IMailFolder tf = root;
        string[] segments = group.Key.Split(source.GetFolder(group.Key).DirectorySeparator);
        for (int i = 0; i < segments.Length; i++)
        {
            var existing = tf.GetSubfolders(false).FirstOrDefault(f => f.Name == segments[i]);
            tf = existing ?? tf.Create(segments[i], i == segments.Length - 1) ?? throw new InvalidOperationException("CannotCreateFolder");
        }
        tf.Open(FolderAccess.ReadWrite);
        mappings[group.Key] = tf.FullName;
        foreach (var payload in group)
        {
            payload.Row.TargetFolder = tf.FullName;
            payload.Row.TargetUidValidity = tf.UidValidity;
            payload.Row.PermanentFlags = tf.PermanentFlags.ToString();
        }
        if ((tf.PermanentFlags & MessageFlags.UserDefined) == 0) throw new InvalidOperationException("NoPermanentKeywordSupport");
        foreach (var payload in group)
        {
            if ((payload.Flags & MessageFlags.Deleted) != 0) throw new InvalidOperationException("DeletedFlagUnsupported");
            if ((payload.Flags & ~tf.PermanentFlags) != 0) throw new InvalidOperationException("UnsupportedFlags");
            var request = new AppendRequest(payload.Message, payload.Flags,
                payload.Keywords.Concat(new[] { payload.Row.Token }), payload.Date);
            UniqueId? uid = tf.Append(options, request);
            var found = tf.Search(SearchQuery.HasKeyword(payload.Row.Token));
            payload.Row.KeywordMatches = found.Count;
            if (found.Count != 1) throw new InvalidOperationException("TokenNotUnique");
            if (uid.HasValue && uid.Value != found[0]) throw new InvalidOperationException("AppendUidMismatch");
            payload.Row.TargetUid = found[0].Id;
            payload.Row.TargetHash = Hash(Read(tf, found[0]));
            payload.Row.TargetEqual = payload.Row.SourceHash == payload.Row.TargetHash;
            var actual = tf.Fetch(found, MessageSummaryItems.Flags | MessageSummaryItems.InternalDate).Single();
            payload.Row.FlagsEqual = ((actual.Flags ?? MessageFlags.None) & ~MessageFlags.Recent) == payload.Flags;
            payload.Row.DateEqual = actual.InternalDate == payload.Date;
            payload.Row.SourceKeywordsRetained = payload.Keywords.All(k => actual.Keywords.Contains(k));
            if (!payload.Row.TargetEqual || !payload.Row.FlagsEqual || !payload.Row.DateEqual || !payload.Row.SourceKeywordsRetained)
                throw new InvalidOperationException("TargetFidelityMismatch");
        }
        tf.Close();
    }
    target.Disconnect(true);
    using var reopened = Connect("target");
    foreach (var group in observations.GroupBy(x => x.TargetFolder))
    {
        var tf = reopened.GetFolder(group.Key);
        tf.Open(FolderAccess.ReadOnly);
        if (tf.Search(SearchQuery.All).Count != group.Count()) throw new InvalidOperationException("TargetCountMismatch");
        foreach (var row in group)
        {
            var found = tf.Search(SearchQuery.HasKeyword(row.Token));
            row.ReconnectedTokenMatches = found.Count;
            row.ReconnectedEqual = found.Count == 1 && tf.UidValidity == row.TargetUidValidity && found[0].Id == row.TargetUid && Hash(Read(tf,found[0])) == row.SourceHash;
            if (!row.ReconnectedEqual) throw new InvalidOperationException("ReconnectedMismatch");
        }
        tf.Close();
    }
    foreach (var group in payloads.GroupBy(x => x.Row.SourceFolder))
    {
        var sf = source.GetFolder(group.Key);
        sf.Open(FolderAccess.ReadOnly);
        foreach (var payload in group)
        {
            var uid = new UniqueId(payload.Row.SourceUid);
            var summary = sf.Fetch(new[] { uid }, MessageSummaryItems.Flags | MessageSummaryItems.InternalDate).Single();
            payload.Row.SourceUnchanged = sf.UidValidity == payload.Row.SourceUidValidity && Hash(Read(sf,uid)) == payload.Row.SourceHash &&
                ((summary.Flags ?? MessageFlags.None) & ~MessageFlags.Recent) == payload.Flags && summary.InternalDate == payload.Date;
        }
        sf.Close();
    }
}
catch (Exception ex) { failure = ex is InvalidOperationException ? ex.Message : ex.GetType().Name; }
finally { foreach (var p in payloads) p.Message.Dispose(); }
bool pass = failure == null && observations.Count == 12 && observations.All(r => r.SerializationEqual && r.TargetEqual && r.ReconnectedEqual && r.SourceUnchanged);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
File.WriteAllText(Path.Combine(output,"report.json"), JsonSerializer.Serialize(new { pass, failure, targetRootName, mappings, observations }, jsonOptions));
Console.WriteLine(JsonSerializer.Serialize(new { pass, failure, count = observations.Count, serializationEqual = observations.Count(x => x.SerializationEqual), output }));
return pass ? 0 : 1;

ImapClient Connect(string role)
{
    var cfg=credentials.RootElement.GetProperty(role);
    if (cfg.GetProperty("imapHost").GetString() != "127.0.0.1" || cfg.GetProperty("imapPort").GetInt32() != labPort) throw new InvalidOperationException("LabOnly");
    var client=new ImapClient { Timeout = 20000 };
    client.Connect("127.0.0.1",labPort,SecureSocketOptions.None);
    client.Authenticate(cfg.GetProperty("username").GetString()!,cfg.GetProperty("password").GetString()!);
    return client;
}
static byte[] Read(IMailFolder folder,UniqueId uid) { using var s=folder.GetStream(uid);using var ms=new MemoryStream();s.CopyTo(ms);return ms.ToArray(); }
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static int FirstDifference(byte[] a,byte[] b) { for(int i=0;i<Math.Min(a.Length,b.Length);i++)if(a[i]!=b[i])return i;return a.Length==b.Length?-1:Math.Min(a.Length,b.Length); }
sealed class Row
{
    public string SourceFolder {get;set;}=""; public uint SourceUidValidity {get;set;} public uint SourceUid {get;set;}
    public string SourceHash {get;set;}=""; public int SourceBytes {get;set;} public string SerializedHash {get;set;}=""; public int SerializedBytes {get;set;}
    public bool SerializationEqual {get;set;} public int FirstDifference {get;set;} public string SourceFlags {get;set;}=""; public DateTimeOffset? SourceDate {get;set;}
    public string TargetFolder {get;set;}=""; public uint TargetUidValidity {get;set;} public string PermanentFlags {get;set;}="";
    public string Token {get;set;}=""; public uint TargetUid {get;set;} public int KeywordMatches {get;set;} public string TargetHash {get;set;}="";
    public bool TargetEqual {get;set;} public bool FlagsEqual {get;set;} public bool DateEqual {get;set;} public bool SourceKeywordsRetained {get;set;}
    public int ReconnectedTokenMatches {get;set;} public bool ReconnectedEqual {get;set;} public bool SourceUnchanged {get;set;}
}
