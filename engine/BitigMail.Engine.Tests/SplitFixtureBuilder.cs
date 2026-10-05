using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;

namespace BitigMail.Engine.Tests;

public static class SplitFixtureBuilder
{
    public class FixtureItemManifest
    {
        public string Subject { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public DateTime? StoredDate { get; set; }
        public string AttachmentName { get; set; } = string.Empty;
        public long AttachmentSizeBytes { get; set; }
        public string AttachmentSha256 { get; set; } = string.Empty;
    }

    public class SplitFixtureManifest
    {
        public string SourceSha256 { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public int TotalMessages { get; set; }
        public List<FixtureItemManifest> Items { get; set; } = new();
    }

    public static (string PstPath, string ManifestPath, SplitFixtureManifest Manifest) BuildNineMessageFixture(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        string pstPath = Path.Combine(targetDirectory, "synthetic-split-nine.pst");
        string manifestPath = Path.Combine(targetDirectory, "synthetic-split-nine-manifest.json");

        if (File.Exists(pstPath)) File.Delete(pstPath);

        var manifest = new SplitFixtureManifest();

        // 9 items with 256 KiB attachments across 2 folders (<50 items each)
        // Folder 1: "Projeler" (5 items: 2022, 2023, 2024, 2024, 2025)
        // Folder 2: "Musteriler" (4 items: 2024, 2026, Tarihsiz, Tarihsiz)
        using (var pst = PersonalStorage.Create(pstPath, FileFormatVersion.Unicode))
        {
            var folderProjeler = pst.RootFolder.AddSubFolder("Projeler");
            var folderMusteriler = pst.RootFolder.AddSubFolder("Musteriler");

            var itemsSpec = new[]
            {
                new { Folder = folderProjeler, FolderName = "Projeler", Subject = "Proje 2022 Raporu", Date = (DateTime?)new DateTime(2022, 5, 10, 10, 0, 0, DateTimeKind.Utc), Year = "2022", AttSeed = 1 },
                new { Folder = folderProjeler, FolderName = "Projeler", Subject = "Proje 2023 Planı", Date = (DateTime?)new DateTime(2023, 8, 15, 14, 30, 0, DateTimeKind.Utc), Year = "2023", AttSeed = 2 },
                new { Folder = folderProjeler, FolderName = "Projeler", Subject = "Proje 2024 Faz 1", Date = (DateTime?)new DateTime(2024, 1, 20, 9, 0, 0, DateTimeKind.Utc), Year = "2024", AttSeed = 3 },
                new { Folder = folderProjeler, FolderName = "Projeler", Subject = "Proje 2024 Faz 2", Date = (DateTime?)new DateTime(2024, 6, 12, 11, 0, 0, DateTimeKind.Utc), Year = "2024", AttSeed = 4 },
                new { Folder = folderProjeler, FolderName = "Projeler", Subject = "Proje 2025 Vizyon", Date = (DateTime?)new DateTime(2025, 3, 1, 15, 0, 0, DateTimeKind.Utc), Year = "2025", AttSeed = 5 },

                new { Folder = folderMusteriler, FolderName = "Musteriler", Subject = "Müşteri 2024 Sözleşme", Date = (DateTime?)new DateTime(2024, 9, 10, 8, 0, 0, DateTimeKind.Utc), Year = "2024", AttSeed = 6 },
                new { Folder = folderMusteriler, FolderName = "Musteriler", Subject = "Müşteri 2026 Mutabakat", Date = (DateTime?)new DateTime(2026, 2, 14, 16, 0, 0, DateTimeKind.Utc), Year = "2026", AttSeed = 7 },
                new { Folder = folderMusteriler, FolderName = "Musteriler", Subject = "Müşteri Tarihsiz Not 1", Date = (DateTime?)null, Year = "Tarihsiz", AttSeed = 8 },
                new { Folder = folderMusteriler, FolderName = "Musteriler", Subject = "Müşteri Tarihsiz Not 2", Date = (DateTime?)null, Year = "Tarihsiz", AttSeed = 9 },
            };

            const int attSize = 256 * 1024; // 256 KiB
            using var sha256 = SHA256.Create();

            foreach (var item in itemsSpec)
            {
                byte[] attBytes = new byte[attSize];
                // Deterministic fill
                for (int i = 0; i < attBytes.Length; i++)
                {
                    attBytes[i] = (byte)((item.AttSeed * 31 + i) % 256);
                }
                string attHash = Convert.ToHexString(sha256.ComputeHash(attBytes)).ToLowerInvariant();
                string attName = $"ek_{item.AttSeed}.dat";

                using var msg = new MapiMessage(
                    "sender@bitigmail.test",
                    "receiver@bitigmail.test",
                    item.Subject,
                    $"Gövde metni: {item.Subject} - Boyut: 256 KiB ek içerir.");

                if (item.Date.HasValue)
                {
                    msg.ClientSubmitTime = item.Date.Value;
                    msg.DeliveryTime = item.Date.Value;
                }
                else
                {
                    msg.ClientSubmitTime = DateTime.MinValue;
                    msg.DeliveryTime = DateTime.MinValue;
                    msg.RemoveProperty(MapiPropertyTag.PR_CLIENT_SUBMIT_TIME);
                    msg.RemoveProperty(MapiPropertyTag.PR_MESSAGE_DELIVERY_TIME);
                }

                msg.Attachments.Add(attName, attBytes);
                item.Folder.AddMessage(msg);

                manifest.Items.Add(new FixtureItemManifest
                {
                    Subject = item.Subject,
                    Folder = item.FolderName,
                    Year = item.Year,
                    StoredDate = item.Date,
                    AttachmentName = attName,
                    AttachmentSizeBytes = attSize,
                    AttachmentSha256 = attHash
                });
            }
        }

        var fi = new FileInfo(pstPath);
        manifest.FileSizeBytes = fi.Length;
        manifest.TotalMessages = manifest.Items.Count;

        using (var fs = File.OpenRead(pstPath))
        using (var sha = SHA256.Create())
        {
            manifest.SourceSha256 = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);

        return (pstPath, manifestPath, manifest);
    }

    public static string BuildSingleMessageOversizeFixture(string targetDirectory, int attachmentSizeBytes = 1_200_000)
    {
        Directory.CreateDirectory(targetDirectory);
        string pstPath = Path.Combine(targetDirectory, "synthetic-oversize-single.pst");
        if (File.Exists(pstPath)) File.Delete(pstPath);

        using (var pst = PersonalStorage.Create(pstPath, FileFormatVersion.Unicode))
        {
            var folder = pst.RootFolder.AddSubFolder("BuyukMesaj");
            byte[] attBytes = new byte[attachmentSizeBytes];
            for (int i = 0; i < attBytes.Length; i++)
            {
                attBytes[i] = (byte)(i % 251);
            }

            using var msg = new MapiMessage(
                "oversize@bitigmail.test",
                "receiver@bitigmail.test",
                "Büyük Tek İleti",
                "Bu ileti tek başına boyutu 1MB'ı aşan bir ek içerir.");

            msg.DeliveryTime = DateTime.UtcNow;
            msg.Attachments.Add("buyuk_ek.bin", attBytes);
            folder.AddMessage(msg);
        }

        return pstPath;
    }
}
