using System.Text;

namespace BitigMail.Engine.Distribution;

public static class DesktopProtocolReader
{
    public static async Task<string?> ReadLineAsync(TextReader reader, CancellationToken cancellationToken = default, int maximumCharacters = 1024)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maximumCharacters is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        var line = new StringBuilder(Math.Min(maximumCharacters, 128));
        var character = new char[1];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = await reader.ReadAsync(character.AsMemory(), cancellationToken);
            if (count == 0) return line.Length == 0 ? null : line.ToString();
            if (character[0] == '\n')
            {
                if (line.Length > 0 && line[^1] == '\r') line.Length--;
                return line.ToString();
            }
            if (line.Length >= maximumCharacters) throw new InvalidDataException("Desktop protocol line limit exceeded.");
            line.Append(character[0]);
        }
    }
}
