using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost.Jobs;

public partial class JobManager
{
    private static IReadOnlyList<JobAuthorizationScope> FreezeScopes(IEnumerable<JobAuthorizationScope> scopes)
    {
        var copy = scopes.Take(1001).ToArray();
        if (copy.Length > 1000 || copy.Any(s => s is null || string.IsNullOrWhiteSpace(s.CompanyId) || string.IsNullOrWhiteSpace(s.ProjectId)))
            throw new InvalidDataException("İş yetki kapsamı geçersiz.");
        return Array.AsReadOnly(copy.Distinct().Select(s => new JobAuthorizationScope(s.CompanyId, s.ProjectId)).ToArray());
    }

    public bool CanAccessJob(AuthenticatedSessionPrincipal actor, LocalJobRecord record)
    {
        if (_identityCatalog is null) return true;
        try
        {
            if (string.IsNullOrWhiteSpace(record.ActorUserId) ||
                (record.ActorUserId != actor.UserId && !_identityCatalog.IsAdmin(actor)) ||
                record.RequiredScopes is null || record.RequiredScopes.Count is < 1 or > 1000) return false;
            var target = new JobAuthorizationScope(record.ClientContext.CompanyId, record.ClientContext.ProjectId);
            return record.RequiredScopes.Contains(target) && record.RequiredScopes.All(s => s is not null &&
                _identityCatalog.CanAccess(actor, s.CompanyId, s.ProjectId));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    private LocalJobRecord ReturnExistingAuthorized(LocalJobRecord record)
    {
        if (_identityCatalog is not null && (_httpContextAccessor?.HttpContext?.Items[typeof(AuthenticatedSessionPrincipal)] is not AuthenticatedSessionPrincipal actor || !CanAccessJob(actor, record)))
            throw new KeyNotFoundException("İş bulunamadı.");
        return CloneJobRecord(record);
    }

    private void RejectDispatchLocked(LocalJobRecord record)
    {
        record.Status = "failed";
        record.Stage = "Yetki doğrulanamadı";
        record.ErrorMessage = "İş güncel kullanıcı ve tüm kaynak/hedef kapsam yetkileri doğrulanamadığı için başlatılmadı.";
        record.CompletedAt = DateTimeOffset.UtcNow;
        record.WaitingAtShutdown = false;
        _jobs[record.JobId] = CloneJobRecord(record);
        try { SaveJobRecordLocked(CloneJobRecord(record)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Do not continue dispatching after failing to durably record a security refusal.
            _dispatchBlocked = true;
            try { JobSnapshotPersistence.Write(Path.Combine(_runtimeDir, "security-dispatch-unresolved.flag"), record.JobId); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void EnsureSynchronousDispatch(LocalJobRecord record)
    {
        if (CanDispatch(record)) return;
        lock (_jobLock) RejectDispatchLocked(record);
        throw new UnauthorizedAccessException("İş yetkisi doğrulanamadı.");
    }

    private void IncludeSourceJobScopes(LocalJobRecord record, string? sourceJobId)
    {
        if (_identityCatalog is null || string.IsNullOrWhiteSpace(sourceJobId)) return;
        if (!_jobs.TryGetValue(sourceJobId, out var source)) throw new KeyNotFoundException("Kaynak iş bulunamadı.");
        ReturnExistingAuthorized(source);
        record.RequiredScopes = FreezeScopes(record.RequiredScopes.Concat(source.RequiredScopes)
            .Append(new(source.ClientContext.CompanyId, source.ClientContext.ProjectId)));
    }
}
