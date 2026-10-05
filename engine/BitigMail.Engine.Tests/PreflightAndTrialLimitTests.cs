using System;
using System.Collections.Generic;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using Xunit;

namespace BitigMail.Engine.Tests;

public class PreflightAndTrialLimitTests
{
    [Fact]
    public void Preflight_FolderExceeding50Items_TriggersTrialBlocker()
    {
        var folders = new List<FolderSummary>
        {
            new FolderSummary { DisplayName = "Gelen Kutusu", ItemCount = 51, Category = "Active" },
            new FolderSummary { DisplayName = "Arşiv", ItemCount = 10, Category = "Active" }
        };

        // Invoke the production preflight gate
        var preflight = OstAnalyzer.EvaluatePreflight(folders);

        Assert.False(preflight.CanConvert);
        Assert.True(preflight.HasTrialBlocker);
        Assert.Single(preflight.Blockers);
        Assert.Contains("51 öğe var", preflight.TrialBlockerReason);
    }

    [Fact]
    public void Preflight_FoldersUnder50Items_PermitsConversion()
    {
        var folders = new List<FolderSummary>
        {
            new FolderSummary { DisplayName = "Gelen Kutusu", ItemCount = 13, Category = "Active" },
            new FolderSummary { DisplayName = "Gönderilenler", ItemCount = 3, Category = "Active" },
            new FolderSummary { DisplayName = "Silinmiş Öğeler", ItemCount = 0, Category = "Empty" }
        };

        // Invoke the production preflight gate
        var preflight = OstAnalyzer.EvaluatePreflight(folders);

        Assert.True(preflight.CanConvert);
        Assert.False(preflight.HasTrialBlocker);
        Assert.Empty(preflight.Blockers);
    }

    [Fact]
    public void Preflight_EnumerationError_RecordedAsExplicitBlockerWithoutSwallowing()
    {
        var folders = new List<FolderSummary>
        {
            new FolderSummary { DisplayName = "Normal Klasör", ItemCount = 5, Category = "Active" }
        };
        var blockers = new List<string>
        {
            "Klasör iletileri numaralandırılamadı: 'Bozuk Klasör' - MAPI RPC connection dropped or corrupt message table"
        };

        // Invoke production preflight gate passing initial blockers discovered during traversal
        var preflight = OstAnalyzer.EvaluatePreflight(folders, existingBlockers: blockers);

        Assert.False(preflight.CanConvert);
        Assert.Single(preflight.Blockers);
        Assert.Contains("numaralandırılamadı", preflight.Blockers[0]);
    }
}
