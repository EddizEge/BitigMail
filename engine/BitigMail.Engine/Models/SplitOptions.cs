using System;

namespace BitigMail.Engine.Models;

public class SplitOptions
{
    public const long MinSizeCapBytes = 1_000_000L; // 1 MB (decimal)
    public const long MaxSizeCapBytes = 100_000_000_000L; // 100 GB (decimal)

    public const string ModeYear = "year";
    public const string ModeSize = "size";

    public string SplitMode { get; set; } = ModeYear;

    public string Mode
    {
        get => SplitMode;
        set => SplitMode = value;
    }

    public long? SizeCapBytes { get; set; }

    public readonly record struct ValidationResult(bool IsValid, string? Error);

    public static ValidationResult Validate(string? splitMode, long? sizeCapBytes)
    {
        string mode = splitMode?.Trim().ToLowerInvariant() ?? "";
        if (mode != ModeYear && mode != ModeSize)
        {
            return new ValidationResult(false, $"Bilinmeyen bölme modu ('{splitMode}'). Yalnızca 'year' veya 'size' desteklenmektedir.");
        }

        if (mode == ModeSize)
        {
            if (!sizeCapBytes.HasValue)
            {
                return new ValidationResult(false, "Boyuta göre bölümleme için boyut sınırı (sizeCapBytes) belirtilmelidir.");
            }

            if (sizeCapBytes.Value < MinSizeCapBytes)
            {
                return new ValidationResult(false, $"Boyut sınırı en az 1.000.000 bayt (1 MB) olmalıdır. Belirtilen: {sizeCapBytes.Value:N0} bayt.");
            }

            if (sizeCapBytes.Value > MaxSizeCapBytes)
            {
                return new ValidationResult(false, $"Boyut sınırı en fazla 100.000.000.000 bayt (100 GB) olmalıdır. Belirtilen: {sizeCapBytes.Value:N0} bayt.");
            }
        }

        return new ValidationResult(true, null);
    }

    public static void Validate(SplitOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options), "Bölümleme seçenekleri belirtilmelidir.");
        }

        var result = Validate(options.SplitMode, options.SizeCapBytes);
        if (!result.IsValid)
        {
            throw new ArgumentException(result.Error, nameof(options));
        }
    }
}
