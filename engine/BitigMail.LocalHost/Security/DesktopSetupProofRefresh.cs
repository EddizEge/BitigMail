using System.Security.Cryptography;
using BitigMail.Engine.Distribution;

namespace BitigMail.LocalHost.Security;

/// <summary>Only called from the owned desktop's private stdin channel, never an HTTP endpoint.</summary>
internal static class DesktopSetupProofRefresh
{
    internal const string Prefix = "SETUP_PROOF ";
    internal const string Rejected = "BITIGMAIL_SETUP_REJECTED";

    internal static string Handle(string command, int port, SetupProofGate proof,
        IdentityCatalog catalog, DesktopOperationGate operations)
    {
        if (!command.StartsWith(Prefix, StringComparison.Ordinal) || command.Length != Prefix.Length + 64)
            return Rejected;
        string value = command[Prefix.Length..];
        if (value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) return Rejected;
        using var operation = operations.TryEnter();
        if (operation is null || catalog.IsInitialized) return Rejected;
        byte[] bytes = Convert.FromHexString(value);
        try
        {
            string ready = DesktopReadyProof.CreateLine(bytes, port);
            proof.InstallFromNativeChannel(bytes);
            return ready;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
