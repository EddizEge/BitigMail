using BitigMail.Engine.Storage;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("owned probe root and new evidence output required");
string root = Path.GetFullPath(args[0]);
var service = new EmlxNormalizationService();
var results = new Dictionary<string, bool>();
void MustReject(string name, Action action)
{
    try { action(); results[name] = false; }
    catch (InvalidDataException ex) when (ex.Message.Contains("reparse", StringComparison.OrdinalIgnoreCase)) { results[name] = true; }
}
MustReject("sourceTreeJunction", () => service.BuildDirectoryManifest(Path.Combine(root, "source-link")));
MustReject("sourceAncestorJunction", () => service.BuildFilesManifest([Path.Combine(root, "source-link", "sample.emlx")]));
var manifest = service.BuildDirectoryManifest(Path.Combine(root, "source-real"));
MustReject("outputJunction", () => service.Normalize(manifest, Path.Combine(root, "output-link"), "junction-negative", new NoProbe()));
results["noOutputWritten"] = !Directory.EnumerateFileSystemEntries(Path.Combine(root, "output-real")).Any();
using var evidence = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write);
JsonSerializer.Serialize(evidence, results, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(JsonSerializer.Serialize(results));
if (results.Values.Any(passed => !passed)) Environment.ExitCode = 1;

sealed class NoProbe : IDiskCapacityProbe
{
    public DiskCapacityResult Probe(string directoryPath) => throw new Exception("Output reparse must fail before capacity probing.");
}
