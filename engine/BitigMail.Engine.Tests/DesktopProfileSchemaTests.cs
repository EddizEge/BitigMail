using System;
using System.IO;
using BitigMail.Engine.Distribution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopProfileSchemaTests
{
    private static DesktopInstanceLease Profile(out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), "bitigmail-schema-" + Guid.NewGuid().ToString("N"));
        return DesktopInstanceLease.Acquire(directory);
    }

    [Fact]
    public void FreshProfilePersistsAndReopensWithoutChangingData()
    {
        string directory;
        using (var lease = Profile(out directory))
        {
            Assert.Equal(1, DesktopProfileSchema.ReadOrInitialize(lease));
            File.WriteAllText(Path.Combine(directory, "archive.txt"), "original");
        }
        using var reopened = DesktopInstanceLease.Acquire(directory);
        DesktopProfileSchema.RequireCurrent(reopened);
        Assert.Equal("original", File.ReadAllText(Path.Combine(directory, "archive.txt")));
    }

    [Fact]
    public void UnversionedExistingDataIsNeverSilentlyRelabeled()
    {
        using var lease = Profile(out string directory);
        File.WriteAllText(Path.Combine(directory, "archive.txt"), "original");
        Assert.Throws<InvalidDataException>(() => DesktopProfileSchema.ReadOrInitialize(lease));
        Assert.False(File.Exists(Path.Combine(directory, DesktopProfileSchema.FileName)));
        Assert.Equal("original", File.ReadAllText(Path.Combine(directory, "archive.txt")));
    }

    [Fact]
    public void NewerSchemaIsReadButOldEngineCannotOpenOrRewriteIt()
    {
        using var lease = Profile(out string directory);
        string path = Path.Combine(directory, DesktopProfileSchema.FileName);
        string json = "{\"Product\":\"BitigMail\",\"SchemaVersion\":2}";
        File.WriteAllText(path, json);
        Assert.Equal(2, DesktopProfileSchema.ReadOrInitialize(lease));
        Assert.Throws<InvalidDataException>(() => DesktopProfileSchema.RequireCurrent(lease));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"Product\":\"BitigMail\",\"SchemaVersion\":1,\"SchemaVersion\":2}")]
    [InlineData("{\"Product\":\"Other\",\"SchemaVersion\":1}")]
    [InlineData("{\"Product\":\"BitigMail\",\"SchemaVersion\":0}")]
    [InlineData("{\"Product\":\"BitigMail\",\"SchemaVersion\":\"1\"}")]
    [InlineData("{\"Product\":\"BitigMail\",\"SchemaVersion\":1,\"Extra\":true}")]
    public void MalformedMarkerFailsClosed(string json)
    {
        using var lease = Profile(out string directory);
        File.WriteAllText(Path.Combine(directory, DesktopProfileSchema.FileName), json);
        Assert.Throws<InvalidDataException>(() => DesktopProfileSchema.ReadOrInitialize(lease));
    }

    [Fact]
    public void DisposedLeaseCannotReadOrInitialize()
    {
        var lease = Profile(out string directory);
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => DesktopProfileSchema.ReadOrInitialize(lease));
        Assert.False(File.Exists(Path.Combine(directory, DesktopProfileSchema.FileName)));
    }
}
