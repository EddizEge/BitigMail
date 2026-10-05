namespace BitigMail.LocalHost.Jobs;

public sealed record DesktopShutdownStatus(bool Requested, bool CanExit, string? ActiveJobId, int PendingJobs, bool UnresolvedWorker, bool PersistenceFailure);

public partial class JobManager
{
    private bool _desktopShutdownRequested;
    private bool _desktopShutdownPersistenceFailure;

    // Called from the owned native control channel, never an anonymous HTTP endpoint.
    public DesktopShutdownStatus BeginDesktopShutdown()
    {
        lock (_jobLock)
        {
            _desktopShutdownRequested = true;
            try
            {
                foreach (var pending in _pendingJobs)
                {
                    pending.Record.WaitingAtShutdown = true;
                    SaveJobRecordLocked(CloneJobRecord(pending.Record));
                }
                _desktopShutdownPersistenceFailure = false;
            }
            catch { _desktopShutdownPersistenceFailure = true; throw; }
            return DesktopShutdownStatusLocked();
        }
    }

    public DesktopShutdownStatus GetDesktopShutdownStatus()
    {
        lock (_jobLock) return DesktopShutdownStatusLocked();
    }

    private DesktopShutdownStatus DesktopShutdownStatusLocked() => new(
        _desktopShutdownRequested,
        _desktopShutdownRequested && _activeRunningJobId is null && !_dispatchBlocked && !_desktopShutdownPersistenceFailure,
        _activeRunningJobId,
        _pendingJobs.Count,
        _dispatchBlocked,
        _desktopShutdownPersistenceFailure);
}
