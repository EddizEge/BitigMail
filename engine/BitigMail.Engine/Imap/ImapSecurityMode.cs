namespace BitigMail.Engine.Imap;

/// <summary>
/// Security mode for IMAP connections according to docs/IMAP_TRANSFER_WORKFLOW.md.
/// TLS is the default. Password accounts may explicitly opt into unencrypted transport.
/// OAuth providers always use their fixed TLS endpoints.
/// </summary>
public enum ImapSecurityMode
{
    /// <summary>
    /// Implicit TLS on connection (typically port 993).
    /// </summary>
    SslOnConnect = 1,

    /// <summary>
    /// Explicit TLS negotiation after connection (typically port 143 or 587).
    /// </summary>
    StartTls = 2,

    /// <summary>
    /// Legacy transport value reserved for the host-controlled loopback test lab.
    /// </summary>
    PlaintextLoopbackLabOnly = 3,

    /// <summary>Unencrypted transport explicitly selected for this account.</summary>
    PlaintextExplicitConsent = 4
}
