import { useEffect, useState } from 'react';
import { AppState } from '../../state/useAppState';
import { localEngineClient } from '../../api/localEngineClient';
import { LocalConvertWorkflow } from './LocalConvertWorkflow';
import { LocalMimeWorkflow } from './LocalMimeWorkflow';
import { EmlxNormalizationWorkflow } from './EmlxNormalizationWorkflow';
import { OutlookEmlWorkflow } from './OutlookEmlWorkflow';
import { ChoiceSelector } from '../layout/PageHeader';

type ConversionKind = 'ost' | 'mime' | 'emlx' | 'outlook-eml';

export function ConversionWorkspace({ state }: { state: AppState }) {
  const [kind, setKind] = useState<ConversionKind>(() => {
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
    <div className="conversion-kind-step">
      <ChoiceSelector<ConversionKind>
        label="2. Dönüşüm türünü seçin"
        value={kind}
        testId="convert-kind-selector"
        onChange={(value) => { state.setSelectedLocalJobId(null); setResolvedJob(null); setError(''); setKind(value); }}
        options={[
          { value: 'ost', label: 'OST → PST', hint: 'Outlook önbelleğinden yeni PST', testId: 'convert-input-ost' },
          { value: 'mime', label: 'EML / MBOX → PST', hint: "Posta dosyalarını PST'ye toplayın", testId: 'convert-input-mime' },
          { value: 'outlook-eml', label: 'PST / OST / OLM → EML', hint: "Outlook dosyalarını EML'e açın", testId: 'convert-input-outlook-eml' },
          { value: 'emlx', label: 'Apple Mail EMLX → EML', hint: "Mac postalarını EML'e çevirin", testId: 'convert-input-emlx' },
        ]}
      />
    </div>
    {pending ? <p role={error ? 'alert' : 'status'} style={{ padding: 16 }}>{error || 'İş kaydı açılıyor…'}</p>
      : kind === 'mime' ? <LocalMimeWorkflow state={state} /> : kind === 'emlx' ? <EmlxNormalizationWorkflow state={state} /> : kind === 'outlook-eml' ? <OutlookEmlWorkflow state={state} /> : <LocalConvertWorkflow state={state} />}
  </div>;
}
