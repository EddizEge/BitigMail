using System;
using System.IO;
using Aspose.Email;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;

namespace BitigMail.Engine.Storage;

public static class OutlookStorageInspector
{
    public static OutlookStorageFormatInfo InspectSignature(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return InspectSignature(fs, filePath);
    }

    public static OutlookStorageFormatInfo InspectSignature(Stream stream, string filePath = "")
    {
        long originalPos = stream.Position;
        byte[] header = new byte[24];
        int read = stream.Read(header, 0, header.Length);
        stream.Position = originalPos;

        string magicHex = (read >= 4) ? BitConverter.ToString(header, 0, 4) : "";
        bool isValid = (read >= 4 && header[0] == 0x21 && header[1] == 0x42 && header[2] == 0x44 && header[3] == 0x4E); // !BDN

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

        return new OutlookStorageFormatInfo
        {
            IsValidOutlookStorage = isValid,
            IsOstSignature = isOstSignature,
            IsPstSignature = isPstSignature,
            FormatName = formatName,
            MagicHex = magicHex,
            ClientMagic = clientMagic,
            ClientMagicHex = $"0x{clientMagic:X4}",
            ClientMagicDescription = clientMagicDesc,
            Version = ver,
            VersionHex = $"0x{ver:X4}",
            VersionDescription = verDesc,
            AuthoritativeRuntimeFormat = "Unknown",
            AuthoritativeRuntimeValidation = "Pending"
        };
    }

    public static OutlookStorageFormatInfo ValidateOstAuthoritatively(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ValidateOstAuthoritatively(fs, filePath);
    }

    public static OutlookStorageFormatInfo ValidateOstAuthoritatively(Stream stream, string filePath = "")
    {
        var info = InspectSignature(stream, filePath);

        if (!info.IsValidOutlookStorage)
        {
            throw new InvalidDataException($"[FORMAT REJECTION] Dosya geçerli bir Outlook depolama magic başlığı (!BDN) taşımıyor. Bulunan: {info.MagicHex}");
        }

        if (!info.IsOstSignature)
        {
            throw new InvalidDataException($"[FORMAT REJECTION] Başlık istemci magic denetimi başarısız: bulunan {info.ClientMagicHex} ({info.ClientMagicDescription}), beklenen OST istemci magic 0x4F53 ('SO'). PST veya yabancı dosyalar OST olarak reddedilir.");
        }

        long originalPos = stream.Position;
        try
        {
            using var storage = PersonalStorage.FromStream(new NonClosingStream(stream));
            info.AuthoritativeRuntimeFormat = storage.Format.ToString();

            if (storage.Format != FileFormat.Ost)
            {
                throw new InvalidDataException($"[FORMAT REJECTION] Depolama Aspose çalışma motoru tarafından '{storage.Format}' olarak tanımlandı, OST depolaması (FileFormat.Ost) değil. PST veya posta dışı veriler reddedilir.");
            }

            info.AuthoritativeRuntimeValidation = "PASS (PersonalStorage.Format == FileFormat.Ost)";
            return info;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }

    public static OutlookStorageFormatInfo ValidateSplitSourceAuthoritatively(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ValidateSplitSourceAuthoritatively(fs, filePath);
    }

    public static OutlookStorageFormatInfo ValidateSplitSourceAuthoritatively(Stream stream, string filePath = "")
    {
        var info = InspectSignature(stream, filePath);

        if (!info.IsValidOutlookStorage)
        {
            throw new InvalidDataException($"[FORMAT REJECTION] Dosya geçerli bir Outlook depolama magic başlığı (!BDN) taşımıyor. Bulunan: {info.MagicHex}");
        }

        if (!info.IsOstSignature && !info.IsPstSignature)
        {
            throw new InvalidDataException($"[FORMAT REJECTION] Başlık istemci magic denetimi başarısız: bulunan {info.ClientMagicHex} ({info.ClientMagicDescription}), beklenen OST (0x4F53) veya PST (0x4D53) magic. Yabancı dosyalar reddedilir.");
        }

        long originalPos = stream.Position;
        try
        {
            using var storage = PersonalStorage.FromStream(new NonClosingStream(stream));
            info.AuthoritativeRuntimeFormat = storage.Format.ToString();

            if (info.IsOstSignature && storage.Format != FileFormat.Ost)
            {
                throw new InvalidDataException($"[FORMAT REJECTION] Başlık OST magic taşıyor fakat çalışma motoru formatı '{storage.Format}' olarak belirledi. Başlık ve SDK formatı uyuşmuyor.");
            }

            if (info.IsPstSignature && storage.Format != FileFormat.Pst)
            {
                throw new InvalidDataException($"[FORMAT REJECTION] Başlık PST magic taşıyor fakat çalışma motoru formatı '{storage.Format}' olarak belirledi. Başlık ve SDK formatı uyuşmuyor.");
            }

            if (storage.Format != FileFormat.Ost && storage.Format != FileFormat.Pst)
            {
                throw new InvalidDataException($"[FORMAT REJECTION] Depolama Aspose çalışma motoru tarafından '{storage.Format}' olarak tanımlandı, OST veya PST depolaması değil. Yabancı veriler reddedilir.");
            }

            info.AuthoritativeRuntimeValidation = $"PASS (PersonalStorage.Format == {storage.Format})";
            return info;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }
}
