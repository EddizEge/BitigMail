using System.Text.Json;

namespace BitigMail.Engine.Models;

public static class FrozenMailFilter
{
    public static CompiledMailFilter? Validate(string? canonicalJson, string? fingerprint, int unknownCount, bool dateFilterBlocked = false)
    {
        if (canonicalJson is null && fingerprint is null && unknownCount == 0) return null;
        if (canonicalJson is null || canonicalJson.Length > 64 * 1024 || unknownCount < 0)
            throw new InvalidDataException("Kalıcı gelişmiş filtre kaydı geçersiz.");
        try
        {
            var definition = JsonSerializer.Deserialize<MailFilterDefinition>(canonicalJson)
                ?? throw new InvalidDataException("Kalıcı gelişmiş filtre kaydı eksik.");
            var compiled = AdvancedMailFilter.Compile(definition, dateFidelityUnresolved: dateFilterBlocked);
            if (compiled.CanonicalJson != canonicalJson || compiled.Fingerprint != fingerprint)
                throw new InvalidDataException("Kalıcı gelişmiş filtre özeti değişmiş.");
            return compiled;
        }
        catch (JsonException ex) { throw new InvalidDataException("Kalıcı gelişmiş filtre biçimi geçersiz.", ex); }
    }
}
