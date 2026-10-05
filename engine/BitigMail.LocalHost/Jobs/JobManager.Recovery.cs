using BitigMail.Engine.Models;

namespace BitigMail.LocalHost.Jobs;
public partial class JobManager
{
    public LocalJobRecord ScheduleStage7Job(string jobKind,string sourceName,string targetName,ClientProjectContext owner,Func<LocalJobRecord,Task> execute,bool enqueueIfBusy=true,IReadOnlyList<JobAuthorizationScope>? requiredScopes=null)
    {lock(_jobLock){string id="job-"+Guid.NewGuid().ToString("N")[..12];var record=new LocalJobRecord{JobId=id,JobKind=jobKind,RequiredScopes=FreezeScopes(requiredScopes??Array.Empty<JobAuthorizationScope>()),SourceFileName=sourceName,TargetFileName=targetName,ClientContext=new(){CompanyId=owner.CompanyId,CompanyName=owner.CompanyName,ProjectId=owner.ProjectId,ProjectName=owner.ProjectName},Status=_activeRunningJobId is null?"converting":"queued",Stage=_activeRunningJobId is null?"Hazırlanıyor":"Sırada",CreatedAt=DateTimeOffset.UtcNow,StartedAt=_activeRunningJobId is null?DateTimeOffset.UtcNow:null};PrepareSchedulingLocked(record,enqueueIfBusy);SaveJobRecordLocked(CloneJobRecord(record));_jobs[id]=record;ScheduleLocked(record,enqueueIfBusy,()=>execute(record));return CloneJobRecord(record);}}
    public LocalJobRecord SetWaitingPriority(string jobId,int priority,string companyId,string projectId)
    {
        if(priority is < -100 or > 100)throw new InvalidOperationException("Bekleme önceliği -100 ile 100 arasında olmalıdır.");lock(_jobLock){var pending=_pendingJobs.FirstOrDefault(x=>x.JobId==jobId)??throw new InvalidOperationException("Öncelik yalnızca bekleyen işlerde değiştirilebilir.");if(pending.Record.ClientContext.CompanyId!=companyId||pending.Record.ClientContext.ProjectId!=projectId)throw new InvalidOperationException("İş kapsamı uyuşmuyor.");pending.Record.WaitingPriority=priority;SaveJobRecordLocked(CloneJobRecord(pending.Record));_jobs[jobId]=CloneJobRecord(pending.Record);return CloneJobRecord(pending.Record);}
    }
    private bool _dispatchBlocked;
    public LocalJobRecord ScheduleRecovery(string sourceFileName,string sourceHash,ClientProjectContext owner,Func<LocalJobRecord,Task> execute,bool enqueueIfBusy=true)
    {
        lock(_jobLock)
        {
            if(_dispatchBlocked)throw new InvalidOperationException("Önceki işçi sürecinin sonlandığı doğrulanamadı; yeni yerel iş başlatılamaz.");
            var frozenOwner = new ClientProjectContext { CompanyId = owner.CompanyId, CompanyName = owner.CompanyName, ProjectId = owner.ProjectId, ProjectName = owner.ProjectName };
            string id="recovery-"+Guid.NewGuid().ToString("N")[..12];var record=new LocalJobRecord{JobId=id,JobKind="damaged-recovery",SourceFileName=sourceFileName,TargetFileName="Yeni doğrulanmış EML klasörü",Status=_activeRunningJobId is null?"converting":"queued",Stage=_activeRunningJobId is null?"Salt okunur tarama hazırlanıyor":"Sırada",SelectionContentHash=sourceHash,ClientContext=frozenOwner,CreatedAt=DateTimeOffset.UtcNow,StartedAt=_activeRunningJobId is null?DateTimeOffset.UtcNow:null};PrepareSchedulingLocked(record,enqueueIfBusy);SaveJobRecordLocked(CloneJobRecord(record));_jobs[id]=CloneJobRecord(record);ScheduleLocked(record,enqueueIfBusy,()=>execute(record));return CloneJobRecord(record);
        }
    }
    public void UpdateRecovery(LocalJobRecord record){lock(_jobLock){_jobs[record.JobId]=CloneJobRecord(record);SaveJobRecordLocked(CloneJobRecord(record));}}
    public void LatchUnresolvedWorker(LocalJobRecord record)
    {
        lock (_jobLock)
        {
            _dispatchBlocked = true;
            record.RecoveryWorkerUnresolved = true;
            record.RecoveryOutcome = "unresolved_worker";
            record.Status = "failed"; record.Stage = "İşçi durumu çözülemedi";
            record.ErrorMessage = "İşçi sürecinin sonlandığı doğrulanamadı; yeni işler güvenlik için durduruldu.";
            JobSnapshotPersistence.Write(Path.Combine(_runtimeDir, "recovery-worker-unresolved.flag"), record.JobId);
            UpdateRecovery(record);
        }
    }
}
