import { useEffect, useState } from 'react';
import { AppState } from '../../state/useAppState';
import { localEngineClient } from '../../api/localEngineClient';
import { LocalConvertWorkflow } from './LocalConvertWorkflow';
import { LocalMimeWorkflow } from './LocalMimeWorkflow';
import { EmlxNormalizationWorkflow } from './EmlxNormalizationWorkflow';
import { OutlookEmlWorkflow } from './OutlookEmlWorkflow';

export function ConversionWorkspace({ state }: { state: AppState }) {
  const [kind, setKind] = useState<'ost' | 'mime' | 'emlx' | 'outlook-eml'>(() => {
    try { const saved = localStorage.getItem('bitigmail-convert-input'); return saved === 'mime' || saved === 'emlx' || saved === 'outlook-eml' ? saved : 'ost'; } catch { return 'ost'; }
  });
  const [resolvedJob, setResolvedJob] = useState<string | null>(null);
  const [error, setError] = useState('');
  useEffect(() => {
    let alive = true;
    if (state.selectedLocalJobId) {
      const id = state.selectedLocalJobId;
      setError('');
      localEngineClient.getJob(id).then(job => {
        if (!alive) return;
        setKind(job.jobKind === 'mime-import' ? 'mime' : job.jobKind === 'emlx-normalize' ? 'emlx' : job.jobKind === 'outlook-eml-normalize' ? 'outlook-eml' : 'ost');
        setResolvedJob(id);
      }).catch(e => { if (alive) setError(e.message); });
    }
    return () => { alive = false; };
  }, [state.selectedLocalJobId]);
  useEffect(() => { try { localStorage.setItem('bitigmail-convert-input', kind); } catch { /* optional preference */ } }, [kind]);
  const pending = !!state.selectedLocalJobId && resolvedJob !== state.selectedLocalJobId;
  return <div style={{ display: 'flex', flexDirection: 'column', minHeight: 0, minWidth: 0, flex: 1, overflow: 'hidden' }}>
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, padding: '12px 16px 0' }} aria-label="Dönüşüm kaynağı">
      {(['ost', 'mime', 'emlx', 'outlook-eml'] as const).map(value => <button key={value} className={`btn ${kind === value ? 'btn-primary-orange' : 'btn-outline-gray'}`} aria-pressed={kind === value} data-testid={`convert-input-${value}`} onClick={() => {
        state.setSelectedLocalJobId(null); setResolvedJob(null); setError(''); setKind(value);
      }}>{value === 'ost' ? 'OST → PST' : value === 'mime' ? 'EML / MBOX → PST' : value === 'emlx' ? 'Apple Mail EMLX → EML' : 'PST / OST / OLM → EML'}</button>)}
    </div>
    {pending ? <p role={error ? 'alert' : 'status'} style={{ padding: 16 }}>{error || 'İş kaydı açılıyor…'}</p>
      : kind === 'mime' ? <LocalMimeWorkflow state={state} /> : kind === 'emlx' ? <EmlxNormalizationWorkflow state={state} /> : kind === 'outlook-eml' ? <OutlookEmlWorkflow state={state} /> : <LocalConvertWorkflow state={state} />}
  </div>;
}
