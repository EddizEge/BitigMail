using System.Text.Json;
using System.Text.Json.Nodes;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.LocalHost;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class TransferRequestCapabilityBoundaryTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    public static IEnumerable<object[]> Requests()
    {
        yield return [typeof(StartJobRequest), """{"sourceHandle":"s","targetHandle":"t"}"""];
        yield return [typeof(SelectionPreviewRequest), """{"sourceHandle":"s"}"""];
        yield return [typeof(ImapTransferPreviewRequest), """{"companyId":"c","projectId":"p","sourceAccountId":"s","targetAccountId":"t","selectedFolders":[]}"""];
        yield return [typeof(ImapTransferStartRequest), """{"previewId":"v","idempotencyKey":"k"}"""];
        yield return [typeof(BridgeImportPreviewRequest), """{"companyId":"c","projectId":"p","sourceHandle":"h","targetAccountId":"t","selectedFolders":[]}"""];
        yield return [typeof(BridgeExportPreviewRequest), """{"companyId":"c","projectId":"p","sourceAccountId":"s","targetDirHandle":"h","targetFormat":"eml","selectedFolders":[]}"""];
        yield return [typeof(PopPreviewApiRequest), """{"accountId":"s","companyId":"c","projectId":"p"}"""];
        yield return [typeof(PopStartApiRequest), """{"planId":"v","companyId":"c","projectId":"p","outputDirHandle":"h","idempotencyKey":"k"}"""];
        yield return [typeof(ArchiveSearchRequest), """{"selectedScopes":[],"query":"invoice","field":"subject"}"""];
    }

    [Theory, MemberData(nameof(Requests))]
    public void UnsupportedSelectionControlsCannotSilentlyDisappear(Type requestType, string supportedJson)
    {
        Assert.NotNull(JsonSerializer.Deserialize(supportedJson, requestType, Web));
        foreach (var property in (requestType == typeof(ImapTransferPreviewRequest) || requestType == typeof(BridgeImportPreviewRequest) || requestType == typeof(BridgeExportPreviewRequest) || requestType == typeof(ArchiveSearchRequest) || requestType == typeof(SelectionPreviewRequest))
            ? new[] { "unrecognizedSelectionPolicy" } : new[] { "advancedFilter", "unrecognizedSelectionPolicy" })
        {
            var altered = JsonNode.Parse(supportedJson)!.AsObject();
            altered[property] = JsonNode.Parse("""{"version":1,"root":{"kind":"condition","field":"subject","operator":"contains","text":"only this"}}""");
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(altered.ToJsonString(), requestType, Web));
        }
    }

    [Fact]
    public void NestedFolderMappingAlsoRejectsUnknownSelectionControls()
    {
        const string json = """{"companyId":"c","projectId":"p","sourceAccountId":"s","targetAccountId":"t","selectedFolders":[{"sourceFolderPath":"Inbox","targetFolderPath":"Archive","exclude":true}]}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ImapTransferPreviewRequest>(json, Web));
    }
}
