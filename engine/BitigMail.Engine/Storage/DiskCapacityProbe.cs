using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BitigMail.Engine.Storage;

public sealed record DiskCapacityResult(bool IsAvailable, long? AvailableBytes, string? Error);

public interface IDiskCapacityProbe
{
    DiskCapacityResult Probe(string directoryPath);
}

public sealed class WindowsDiskCapacityProbe : IDiskCapacityProbe
{
    public DiskCapacityResult Probe(string directoryPath)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, null, "Disk kapasitesi bu platformda sorgulanamıyor.");
        try
        {
            string existing = DiskCapacityPlanning.ResolveNearestExistingDirectory(directoryPath);
            if (!GetDiskFreeSpaceEx(existing, out ulong callerAvailable, out _, out _))
                return new(false, null, $"Disk kapasitesi sorgulanamadı (Windows hata kodu: {Marshal.GetLastWin32Error()}).");
            if (callerAvailable > long.MaxValue)
                return new(false, null, "Disk kapasitesi desteklenen sayı aralığını aşıyor.");
            return new(true, checked((long)callerAvailable), null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(false, null, $"Disk kapasitesi sorgulanamadı: {ex.Message}");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong callerAvailable,
        out ulong totalBytes, out ulong totalFreeBytes);
}

public static class DiskCapacityPlanning
{
    public const long PerItemAllowanceBytes = 64L * 1024;
    public const long FixedReserveBytes = 256L * 1024 * 1024;
    public const long PerSplitPartAllowanceBytes = 1L * 1024 * 1024;

    public static long EstimatePst(long wholeSourceBytes, int selectedItems) =>
        EstimateChecked(wholeSourceBytes, selectedItems, 0, sourceMultiplier: 4);

    public static long EstimateSplit(long wholeSourceBytes, int selectedItems, int plannedParts) =>
        EstimateChecked(wholeSourceBytes, selectedItems, plannedParts, sourceMultiplier: 4);

    public static long EstimateArchive(long rawBytes, int items) =>
        EstimateChecked(rawBytes, items, 0, sourceMultiplier: 4);

    public static int EstimateSizeSplitParts(long wholeSourceBytes, long sizeCapBytes)
    {
        if (wholeSourceBytes < 0) throw new ArgumentOutOfRangeException(nameof(wholeSourceBytes));
        if (sizeCapBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeCapBytes));
        checked
        {
            long outputAllowance = 2L * wholeSourceBytes;
            long parts = Math.Max(1, (outputAllowance / sizeCapBytes) + (outputAllowance % sizeCapBytes == 0 ? 0 : 1));
            if (parts > int.MaxValue) throw new OverflowException("Tahmini bölüm sayısı desteklenen aralığı aşıyor.");
            return (int)parts;
        }
    }

    public static string ResolveNearestExistingDirectory(string intendedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intendedPath);
        string full = Path.GetFullPath(intendedPath);
        string? candidate = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(candidate) && !Directory.Exists(candidate))
            candidate = Path.GetDirectoryName(candidate);
        return candidate ?? throw new DirectoryNotFoundException("Disk kapasitesi için mevcut üst dizin bulunamadı.");
    }

    public static string? CapacityBlocker(long requiredBytes, DiskCapacityResult result)
    {
        if (requiredBytes < 0) return "[DİSK KAPASİTESİ ENGELİ] Tahmini gereken alan geçersiz.";
        if (!result.IsAvailable || !result.AvailableBytes.HasValue)
            return $"[DİSK KAPASİTESİ ENGELİ] Kullanılabilir disk alanı doğrulanamadı. {result.Error}".Trim();
        return result.AvailableBytes.Value < requiredBytes
            ? $"[DİSK KAPASİTESİ ENGELİ] Tahmini gereken alan {requiredBytes} bayt, kullanılabilir alan {result.AvailableBytes.Value} bayt."
            : null;
    }

    private static long EstimateChecked(long sourceBytes, int items, int parts, int sourceMultiplier)
    {
        if (sourceBytes < 0) throw new ArgumentOutOfRangeException(nameof(sourceBytes));
        if (items < 0) throw new ArgumentOutOfRangeException(nameof(items));
        if (parts < 0) throw new ArgumentOutOfRangeException(nameof(parts));
        checked
        {
            return (sourceMultiplier * sourceBytes) + (items * PerItemAllowanceBytes)
                + (parts * PerSplitPartAllowanceBytes) + FixedReserveBytes;
        }
    }
}

public static class BridgeExportCapacityEstimator
{
    public const long PerItemAllowanceBytes = DiskCapacityPlanning.PerItemAllowanceBytes;
    public const long FixedReserveBytes = DiskCapacityPlanning.FixedReserveBytes;
    public static long Estimate(string targetFormat, long eligibleRawBytes, int eligibleItems)
    {
        if (eligibleRawBytes < 0) throw new ArgumentOutOfRangeException(nameof(eligibleRawBytes));
        if (eligibleItems < 0) throw new ArgumentOutOfRangeException(nameof(eligibleItems));
        checked
        {
            long content = string.Equals(targetFormat, "mboxrd", StringComparison.OrdinalIgnoreCase)
                ? 3L * eligibleRawBytes : eligibleRawBytes;
            return content + (eligibleItems * PerItemAllowanceBytes) + FixedReserveBytes;
        }
    }
}
