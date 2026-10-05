import { AppState } from '../../state/useAppState';
import { useMimeImport } from '../../hooks/useMimeImport';
import { MailFilterDefinition, MimeSourceMode } from '../../types/localEngine';
import { AdvancedFilterBuilder } from '../filters/AdvancedFilterBuilder';
import './mimeWorkflow.css';

const modes: Array<{ value: MimeSourceMode; title: string; note: string }> = [
  { value: 'eml-files', title: 'EML dosyaları', note: 'Bir veya birden fazla ileti dosyası' },
  { value: 'eml-tree', title: 'EML klasörü', note: 'Alt klasörleriyle birlikte arşiv' },
  { value: 'mbox', title: 'MBOX arşivi', note: 'Tek bir mboxrd posta dosyası' },
];

export function LocalMimeWorkflow({ state }: { state: AppState }) {
  const company = state.companies.find(c => c.id === state.plan.companyId) || state.companies[0];
  const projects = state.projects.filter(p => p.companyId === company?.id);
  const project = projects.find(p => p.id === state.plan.projectId) || projects[0];
  const context = { companyId: company?.id || '', companyName: company?.name || '', projectId: project?.id || '', projectName: project?.name || '' };
  const engine = useMimeImport(context, state.selectedLocalJobId);
  const { analysis, preview, report, job } = engine;
  const frozen = report?.clientContext || job?.clientContext;
  const locked = engine.running || engine.starting || engine.busy;
  const folders = analysis?.folders.filter(f => f.itemCount > 0) || [];
  const selected = engine.folders.length === 0 ? 0 : preview?.selectedMessagesCount;
  const detail = report?.mimeImport;
  const advancedFilter = (() => { try { return engine.advancedFilterJson ? JSON.parse(engine.advancedFilterJson) as MailFilterDefinition : null; } catch { return null; } })();
  function download() {
    if (!report) return;
    const url = URL.createObjectURL(new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' }));
    const a = document.createElement('a'); a.href = url; a.download = `bitigmail-${report.jobId}.json`; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return <section className="mime-workflow" data-testid="local-mime-workflow" aria-label="EML ve MBOX içe aktarma">
    <header className="mime-header">
      <div><span className="mime-eyebrow">DOSYADAN POSTA ARŞİVİNE</span><h2>EML / MBOX → PST</h2>
        <p>İletilerini seç, kapsamı belirle ve yeni bir Outlook arşivi oluştur.</p></div>
      <div className="mime-service" data-testid="mime-service-status">
        <span className={`mime-dot ${engine.status === 'online' ? 'online' : ''}`} />
        {engine.status === 'online' ? 'Yerel motor hazır' : engine.status === 'checking' ? 'Bağlanıyor…' : 'Yerel motor kapalı'}
        {engine.status === 'offline' && <button className="btn btn-outline-gray" onClick={engine.connect}>Yeniden bağlan</button>}
      </div>
    </header>
    <div className="mime-context"><span>Müşteri <strong data-testid="mime-draft-company">{context.companyName}</strong></span><span>Proje <strong>{context.projectName}</strong></span></div>
    <div className="mime-notice" data-testid="mime-trial-notice"><strong>Deneme sürümü</strong><span>Çıktıların konu ve gövdelerinde değerlendirme işaretleri bulunur. Kaynakta klasör başına 50 ileti sınırı uygulanır.</span></div>
    {engine.error && <div className="mime-error" role="alert">{engine.error}</div>}

    <section className="card mime-card">
      <div className="mime-section-title"><span className="mime-step">1</span><h3>Kaynağı seç</h3></div>
      <div className="mime-modes">{modes.map(mode => <button key={mode.value} className={`mime-mode ${engine.mode === mode.value ? 'selected' : ''}`} aria-pressed={engine.mode === mode.value} data-testid={`mime-mode-${mode.value}`} disabled={locked} onClick={() => { state.setSelectedLocalJobId(null); engine.setMode(mode.value); }}><strong>{mode.title}</strong><small>{mode.note}</small></button>)}</div>
      <p className="mime-muted">{engine.mode === 'eml-tree' ? 'EML alt klasörleri PST içinde korunur; diğer dosyalar sayılarak dışarıda bırakılır.' : engine.mode === 'mbox' ? 'MBOX içeriği mboxrd olarak okunur ve PST içinde tek klasöre alınır.' : 'Seçtiğin EML dosyaları PST içinde aynı klasöre alınır; aynı iletiyi içeren ayrı dosyalar korunur.'}</p>
      <div className="mime-actions"><button className="btn btn-primary-orange" data-testid="mime-pick-source" disabled={locked || engine.status !== 'online'} onClick={() => { state.setSelectedLocalJobId(null); void engine.pickSource(); }}>{engine.busy ? 'Dosyalar kontrol ediliyor…' : analysis ? 'Başka kaynak seç' : engine.mode === 'eml-tree' ? 'EML klasörü seç' : engine.mode === 'mbox' ? 'MBOX dosyası seç' : 'EML dosyaları seç'}</button>
        {analysis && <span data-testid="mime-source-name">{analysis.sourceFileName}</span>}</div>
      {analysis && <><div className="mime-stats"><div><strong>{analysis.totalItems}</strong><span>Kaynak ileti</span></div><div><strong>{analysis.totalAttachments}</strong><span>Ek</span></div><div><strong>{folders.length}</strong><span>İleti içeren klasör</span></div><div><strong data-testid="mime-ignored-count">{analysis.ignoredNonEmlFilesCount}</strong><span>Atlanan diğer dosya</span></div></div>
        {analysis.preflight.warnings?.map((warning, i) => <p className="mime-muted" key={i}>{warning}</p>)}
        {analysis.preflight.blockers?.map((blocker, i) => <p role="alert" className="mime-error" key={i}>{blocker}</p>)}
        {analysis.preflight.hasTrialBlocker && <p className="mime-error" role="alert">{analysis.preflight.trialBlockerReason}</p>}
      </>}
    </section>

    {analysis && <section className="card mime-card" data-testid="mime-selection-card">
      <div className="mime-section-title"><span className="mime-step">2</span><h3>Aktarılacak iletileri belirle</h3></div>
      <div className="mime-filter-grid"><div><div className="mime-actions"><strong>Klasörler</strong><button className="mime-text-button" disabled={locked} data-testid="mime-select-all" onClick={() => engine.setFolders(folders.map(f => f.folderId))}>Tümünü seç</button><button className="mime-text-button" disabled={locked} data-testid="mime-clear-folders" onClick={() => engine.setFolders([])}>Temizle</button></div>
        <p className="mime-muted">Her seçim yalnız o klasörün kendi iletilerini kapsar.</p>
        <div className="mime-folders">{folders.map(folder => <div key={folder.folderId} data-testid="mime-folder"><label><input type="checkbox" disabled={locked} checked={engine.folders.includes(folder.folderId)} onChange={e => engine.setFolders(e.target.checked ? [...engine.folders, folder.folderId] : engine.folders.filter(id => id !== folder.folderId))} /><span>{folder.folderPath || folder.displayName}</span><small>{folder.itemCount}</small></label>{engine.folders.includes(folder.folderId)&&<input aria-label={`${folder.folderPath || folder.displayName} hedef klasörü`} disabled={locked} value={engine.folderMappings?.[folder.folderId] ?? folder.folderPath ?? folder.displayName} onChange={e=>engine.setFolderMappings(current=>({...current,[folder.folderId]:e.target.value}))}/>}</div>)}</div>
        <label>Yinelenen ileti politikası<select aria-label="Yinelenen ileti politikası" disabled={locked} value={engine.duplicatePolicy ?? 'PreservePhysical'} onChange={e=>engine.setDuplicatePolicy(e.target.value as typeof engine.duplicatePolicy)}><option value="PreservePhysical">Her fiziksel iletiyi koru</option><option value="ContentOnly">Aynı içeriği tekilleştir</option><option value="ContentAndMetadata">İçerik ve metadata birlikte aynıysa tekilleştir</option></select></label>
      </div><div><strong>Tarih aralığı · isteğe bağlı</strong><p className="mime-muted">Başlangıç ve bitiş günleri dahil, Türkiye saatiyle. Tarihi eksik veya geçersiz iletiler tarih filtresinde dışlanır.</p>
        <div className="mime-dates"><label>Başlangıç<input type="date" data-testid="mime-start-date" disabled={locked} value={engine.startDate} onChange={e => engine.setStartDate(e.target.value)} /></label><label>Bitiş<input type="date" data-testid="mime-end-date" disabled={locked} value={engine.endDate} onChange={e => engine.setEndDate(e.target.value)} /></label></div>
        <AdvancedFilterBuilder testId="mime-advanced-filter" disabled={locked} value={advancedFilter} unknownCount={preview?.advancedFilterUnknownCount ?? 0} onChange={value=>engine.setAdvancedFilterJson(value?JSON.stringify(value):'')} /><p className="mime-muted">Filtre sunucuda doğrulanır; desteklenmeyen alanlar yok sayılmaz.</p>
      </div></div>
      <div className="mime-preview" aria-live="polite"><div><strong data-testid="mime-preview-selected">{selected ?? '…'}</strong><span> / {analysis.totalItems} ileti aktarılacak</span></div><div>{engine.loadingPreview ? 'Önizleme güncelleniyor…' : engine.folders.length === 0 ? 'Devam etmek için en az bir klasör seç.' : preview ? `${preview.excludedMessagesCount} ileti dışarıda · ${preview.selectedAttachmentsCount} ek · ${preview.missingDateExcludedCount} tarihsiz/geçersiz tarih dışlandı` : 'Geçerli bir önizleme bekleniyor.'}</div></div>
      {preview && !preview.canConvert && <p className="mime-error" role="alert">{preview.blockerReason || 'Bu seçimle dönüşüm başlatılamaz.'}</p>}
      {preview?.qualificationWarnings?.map((warning, i) => <p className="mime-muted" data-testid="mime-qualification-warning" key={i}>{warning}</p>)}
    </section>}

    {analysis && <section className="card mime-card"><div className="mime-section-title"><span className="mime-step">3</span><h3>Yeni PST oluştur</h3></div><p className="mime-muted">Yeni bir dosya adı seç. Var olan dosyanın üzerine yazılmaz.</p>
      <div className="mime-actions"><button className="btn btn-outline-gray" data-testid="mime-pick-target" disabled={locked || !preview?.canConvert} onClick={engine.pickTarget}>{engine.target ? 'Hedefi değiştir' : 'PST konumunu seç'}</button><span data-testid="mime-target-path">{engine.target?.displayPath || 'Henüz hedef seçilmedi'}</span></div>
      <div className="mime-actions mime-start-row"><button className="btn btn-primary-orange" data-testid="mime-start" disabled={!engine.canStart} onClick={engine.start}>{engine.starting ? 'Başlatılıyor…' : 'Başlat / sıraya ekle'}</button><span className="mime-muted">{!preview?.canConvert ? 'Önce geçerli bir kapsam seç.' : !engine.target ? 'Başlatmak için yeni PST konumunu seç.' : 'Önizlemedeki kapsam ve müşteri bilgisi işlem kaydına sabitlenir.'}</span></div>
    </section>}

    {job && <section className="card mime-card" data-testid="mime-job-card"><div className="mime-section-title"><h3>{engine.running ? 'İletiler aktarılıyor' : job.status === 'completed' ? 'PST oluşturuldu' : 'İşlem tamamlanamadı'}</h3><span className="mime-muted" data-testid="mime-job-id">{job.jobId}</span></div>
      <p data-testid="mime-job-context"><strong>{frozen?.companyName}</strong> · {frozen?.projectName}</p>
      {engine.running && <><progress max="100" value={job.percentComplete} /><p>{job.stage} · {job.itemsWritten} / {job.totalItems} ileti</p></>}
      {job.errorMessage && <p role="alert" className="mime-error">{job.errorMessage}</p>}
      {job.outputPath && <p className="mime-path" data-testid="mime-output-path">{job.outputPath}</p>}
      {job.status === 'completed' && !report && <p>Rapor yükleniyor… <button className="mime-text-button" onClick={() => engine.loadJob(job.jobId)}>Yeniden dene</button></p>}
      {report && detail && <>
        <div className="mime-notice" data-testid="mime-result-qualification"><strong>Deneme işaretleri içeriyor</strong><span>Özgün içerik ve ölçülen alanlar kontrol edildi; konu ve gövdeye eklenen değerlendirme işaretleri çıktıda korunur.</span></div>
        {detail.qualificationWarnings?.map((warning, i) => <p className="mime-muted" data-testid="mime-result-source-warning" key={i}>{warning}</p>)}
        <p className="mime-muted" data-testid="mime-sdk-qualification">SDK: {detail.sdkQualification || 'LICENSED_OUTPUT_ACCEPTANCE_PENDING'}</p>
        <div className="mime-stats"><div><strong data-testid="mime-result-count">{report.itemsWritten}</strong><span>Yazılan ileti</span></div><div><strong>{report.reopenedPstVerification.totalAttachmentsVerified}</strong><span>Doğrulanan ek</span></div><div><strong>{report.excludedMessagesCount ?? 0}</strong><span>Dışlanan ileti</span></div><div><strong>{detail.ignoredNonEmlFilesCount}</strong><span>Atlanan diğer dosya</span></div></div>
        <p className="mime-muted">{report.isFiltered ? 'Filtreli aktarım' : 'Tüm kaynak aktarıldı'} · {report.selectionFilter?.startDate || 'Başlangıç sınırı yok'} — {report.selectionFilter?.endDate || 'Bitiş sınırı yok'}</p>
        {report.selectionFilter?.selectedFolders?.length ? <p className="mime-muted">Klasörler: {report.selectionFilter.selectedFolders.map(f => f.folderPath).join(' · ')}</p> : null}
        <details><summary>Doğrulama ayrıntıları</summary><p>Kaynak kümesi parmak izi</p><code className="mime-path">{detail.sourceSetFingerprint}</code><p>PST yeniden açılarak {report.reopenedPstVerification.totalPhysicalItemsFound} ileti ve eklerin bütünlüğü kontrol edildi.</p><p className="mime-muted">{report.unmeasuredFields.note} Bağımsız CID ve tüm MAPI alanları için kapsamlı doğrulama iddiası yoktur.</p><ul>{detail.observedDifferences.map((difference, i) => <li key={i}>{difference}</li>)}</ul></details>
        <div className="mime-actions"><button className="btn btn-outline-gray" data-testid="mime-download-report" onClick={download}>Raporu indir</button><button className="btn btn-outline-gray" data-testid="mime-open-archive" onClick={() => { state.setSelectedLocalJobId(null); state.updatePlan(p => ({ ...p, operationType: 'archive' })); }}>Arşivlemeye geç</button></div>
        <p className="mime-muted">Yıl veya boyuta göre bölümlemek için Arşivleme ekranında yukarıdaki PST dosyasını kaynak seç.</p>
      </>}
    </section>}
  </section>;
}
