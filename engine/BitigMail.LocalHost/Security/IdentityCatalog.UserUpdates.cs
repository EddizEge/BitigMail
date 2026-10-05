namespace BitigMail.LocalHost.Security;

public sealed partial class IdentityCatalog
{
    private async Task<string> HashPasswordAsync(LocalUser user, string password, CancellationToken cancellationToken)
    {
        await _passwordVerifications.WaitAsync(cancellationToken);
        try { return _hasher.HashPassword(user, password); }
        finally { _passwordVerifications.Release(); }
    }

    public async Task<AuthenticatedUserView> UpdateUserAsync(AuthenticatedSessionPrincipal actor, string targetUserId,
        LocalUserRole? role = null, bool? active = null, IReadOnlyList<CompanyGrant>? grants = null,
        string? newPassword = null, CancellationToken cancellationToken = default)
    {
        if (role.HasValue && !Enum.IsDefined(role.Value)) throw new ArgumentException("Kullanıcı rolü geçersiz.");
        var copiedGrants = grants is null ? null : CopyGrants(grants);
        if (newPassword is not null) ValidatePassword(newPassword);
        LocalUser original;
        lock (_gate)
        {
            var doc = Load(); RequireAdmin(doc, actor);
            original = doc.Users.SingleOrDefault(u => u.UserId == targetUserId) ?? throw new KeyNotFoundException();
        }
        string? hash = newPassword is null ? null : await HashPasswordAsync(original, newPassword, cancellationToken);
        await using var lease = await ProfileWriteLease.AcquireAsync(_profileDirectory, cancellationToken);
        lock (_gate)
        {
            var doc = Load(); RequireAdmin(doc, actor);
            var current = doc.Users.SingleOrDefault(u => u.UserId == targetUserId) ?? throw new KeyNotFoundException();
            // Refuse stale form updates instead of overwriting a concurrent security change.
            if (current.SecurityVersion != original.SecurityVersion) throw new InvalidOperationException("Kullanıcı değişti; bilgileri yenileyip tekrar deneyin.");
            if (copiedGrants is not null) ValidateRequestedGrants(doc, copiedGrants);
            var updated = current with { Role = role ?? current.Role, Active = active ?? current.Active,
                Grants = copiedGrants ?? current.Grants, PasswordHash = hash ?? current.PasswordHash };
            bool changed = updated.Role != current.Role || updated.Active != current.Active || hash is not null ||
                !GrantSet(updated.Grants).SequenceEqual(GrantSet(current.Grants), StringComparer.Ordinal);
            if (!changed) return View(current);
            if ((!updated.Active || updated.Role != LocalUserRole.Admin) && current.Active && current.Role == LocalUserRole.Admin &&
                !doc.Users.Any(u => u.UserId != current.UserId && u.Active && u.Role == LocalUserRole.Admin))
                throw new InvalidOperationException("Son etkin yönetici devre dışı bırakılamaz veya yetkisi azaltılamaz.");
            updated = updated with { SecurityVersion = checked(current.SecurityVersion + 1) };
            Save(doc with { Users = doc.Users.Select(u => u.UserId == updated.UserId ? updated : u).ToArray() });
            return View(updated);
        }
    }

    private static IEnumerable<string> GrantSet(IReadOnlyList<CompanyGrant> grants) => grants
        .OrderBy(g => g.CompanyId, StringComparer.Ordinal)
        .Select(g => g.CompanyId + ":" + string.Join(",", g.ProjectIds.OrderBy(p => p, StringComparer.Ordinal)));
}
