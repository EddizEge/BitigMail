using System.Security.Cryptography;

namespace BitigMail.Engine.Security;

public static class VerifiedStreamCopy
{
    // Destination must remain unpublished until this method and all package checks succeed.
    public static async Task CopyAsync(Stream source, Stream destination, long expectedLength,
        string expectedSha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (expectedLength < 0 || expectedLength > ArchiveZipPayloadVerifier.MaximumEntryBytes ||
            expectedSha256 is not { Length: 64 } || !expectedSha256.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid expected payload identity.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long copied = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (read > expectedLength - copied) throw new InvalidDataException("Payload grew during copy.");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (copied != expectedLength || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Payload changed during copy.");
    }
}
