using System.Security.Cryptography;
using BitigMail.Engine.Archive;
using BitigMail.LocalHost.Jobs;

namespace BitigMail.LocalHost.Archive;

public sealed record RetentionPreviewItem(string ArchiveId,string CompanyId,string ProjectId,DateTimeOffset CreatedAtUtc,long TotalBytes,int TotalItems,string Reason,bool Protected);
public sealed record RetentionPreviewResponse(DateTimeOffset EvaluatedAtUtc,int CandidateCount,long CandidateBytes,IReadOnlyList<RetentionPreviewItem> Items,string Policy);
public sealed record CustomerDeliveryReport(string JobId,string JobKind,string CompanyId,string ProjectId,string Status,string SourceType,string TargetType,string FilterEvidence,string? StartDate,string? EndDate,int ItemsRead,int ItemsWritten,int AttachmentCount,int FailedItems,int SkippedItems,bool DateFilterBlocked,IReadOnlyList<string> QualificationWarnings,string EvidenceSha256,string PortabilityNotice);

public sealed class ArchiveGovernanceService
{
 private readonly ArchiveCatalogService _catalog;private readonly JobManager _jobs;
 public ArchiveGovernanceService(ArchiveCatalogService catalog,JobManager jobs){_catalog=catalog;_jobs=jobs;}
 public RetentionPreviewResponse PreviewRetention(string companyId,string? projectId,int olderThanDays,long? largerThanBytes)
 {
  if(olderThanDays is <1 or >36500||largerThanBytes is <0)throw new ArgumentException("Saklama önizleme sınırı geçersiz.");var cutoff=DateTimeOffset.UtcNow.AddDays(-olderThanDays);
  var items=_catalog.GetRegisteredManifests().Values.Where(x=>x.CompanyId==companyId&&(projectId is null||x.ProjectId==projectId)).Where(x=>x.CreatedAtUtc<=cutoff||largerThanBytes.HasValue&&x.TotalSizeBytes>=largerThanBytes.Value).Select(x=>new RetentionPreviewItem(x.ArchiveId,x.CompanyId,x.ProjectId,x.CreatedAtUtc,x.TotalSizeBytes,x.TotalItems,x.CreatedAtUtc<=cutoff?"age":"size",false)).OrderBy(x=>x.CreatedAtUtc).ToArray();
  return new(DateTimeOffset.UtcNow,items.Length,items.Sum(x=>x.TotalBytes),items,"PREVIEW_ONLY_NO_SOURCE_OR_ARCHIVE_DELETION; LEGAL_HOLD_NOT_SUPPORTED");
 }
 public CustomerDeliveryReport GetDeliveryReport(string jobId)
 {
  var job=_jobs.GetJob(jobId)??throw new KeyNotFoundException();int skipped=job.SkippedDuplicateItemIds?.Count??0;string source=SafeCode(job.SourceKind,"unspecified-source"),target=job.ArchiveId is null?"external-output":"managed-archive";string filter=job.FrozenSelectionFingerprint??job.SelectionContentHash??job.AdvancedFilterFingerprint??"none";string[] warnings=(job.QualificationWarnings?.Count??0)==0?Array.Empty<string>():["QUALIFICATION_WARNING_RECORDED"];
  var content=new{job.JobId,job.JobKind,job.ClientContext.CompanyId,job.ClientContext.ProjectId,job.Status,SourceType=source,TargetType=target,FilterEvidence=filter,StartDate=job.SelectionFilter?.StartDate,EndDate=job.SelectionFilter?.EndDate,job.ItemsRead,job.ItemsWritten,AttachmentCount=job.SelectedAttachmentsCount,job.FailedItems,SkippedItems=skipped,job.DateFilterBlocked,QualificationWarnings=warnings};string evidence=Hash(System.Text.Json.JsonSerializer.Serialize(content));
  return new(job.JobId,job.JobKind,job.ClientContext.CompanyId,job.ClientContext.ProjectId,job.Status,source,target,filter,job.SelectionFilter?.StartDate,job.SelectionFilter?.EndDate,job.ItemsRead,job.ItemsWritten,job.SelectedAttachmentsCount,job.FailedItems,skipped,job.DateFilterBlocked,warnings,evidence,"Hesap kimlik bilgileri ve DPAPI sırları rapora veya arşiv yedeğine dahil değildir; başka cihazda hesaplar yeniden bağlanır.");
 }
 private static string SafeCode(string? value,string fallback)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=64&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '.')?value:fallback;
 private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
