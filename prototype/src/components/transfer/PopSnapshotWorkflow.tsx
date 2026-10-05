import { useEffect, useState } from 'react';
import { LocalEngineClient } from '../../api/localEngineClient';
import { LocalJobRecord, PopAccountPublicDto, PopSnapshotPlan } from '../../types/localEngine';

type PopSecurity = 'ssl' | 'starttls' | 'none';
export function PopSnapshotWorkflow({ companyId, projectId, client }: { companyId: string; projectId: string; client: LocalEngineClient }) {
  const [accounts, setAccounts] = useState<PopAccountPublicDto[]>([]); const [accountId, setAccountId] = useState('');
  const [form, setForm] = useState({ displayName: '', email: '', host: '', port: 995, tlsMode: 'ssl' as PopSecurity, username: '', password: '', allowUnencryptedConnection: false });
  const [plan, setPlan] = useState<PopSnapshotPlan | null>(null); const [output, setOutput] = useState(''); const [job, setJob] = useState<LocalJobRecord | null>(null); const [error, setError] = useState('');
  const refresh = async () => { const list = await client.listPopAccounts(companyId, projectId); setAccounts(list); setAccountId((value) => value || list[0]?.accountId || ''); };
  useEffect(() => { refresh().catch((caught) => setError(String(caught))); }, [companyId, projectId]);
  const changeSecurity = (tlsMode: PopSecurity) => setForm((value) => ({ ...value, tlsMode, port: tlsMode === 'ssl' ? 995 : 110, allowUnencryptedConnection: false }));
  async function create() { try { setError(''); await client.createPopAccount({ ...form, companyId, projectId, allowUnencryptedConnection: form.tlsMode === 'none' && form.allowUnencryptedConnection }); await refresh(); } catch (caught) { setError(caught instanceof Error ? caught.message : String(caught)); } }
  async function preview() { try { setPlan(await client.previewPop({ accountId, companyId, projectId })); } catch (caught) { setError(caught instanceof Error ? caught.message : String(caught)); } }
  async function pickOutput() { const selected = await client.pickOutputDir(); if (!selected.cancelled && selected.handle) setOutput(selected.handle); }
  async function start() { if (!plan || !output) return; setJob(await client.startPop({ planId: plan.planId, companyId, projectId, outputDirHandle: output, idempotencyKey: crypto.randomUUID(), enqueueIfBusy: true })); }
  return <section className="mime-workflow" data-testid="pop-workflow"><header className="mime-header"><div><span className="mime-eyebrow">KAYNAK-YÖNLÜ</span><h2>POP kaynağından doğrulanmış EML alın</h2><p>Postalar sunucuda kalır; BitigMail silme komutu göndermez.</p><p className="mime-muted" data-testid="pop-filter-guidance">Tam gövde filtresini indirme sonrasında EML / MBOX → PST akışında uygulayabilirsiniz.</p></div></header>{error && <p role="alert" className="mime-error">{error}</p>}
    <section className="card mime-card"><h3>1. POP hesabını bağlayın</h3><div className="form-grid">
      <label>Hesap adı<input placeholder="Örn. Eski destek kutusu" value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })}/></label>
      <label>E-posta<input type="email" placeholder="destek@ornek.com" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })}/></label>
      <label>POP sunucusu<input placeholder="pop.example.com" value={form.host} onChange={(e) => setForm({ ...form, host: e.target.value, allowUnencryptedConnection: false })}/></label>
      <label>Port<input type="number" value={form.port} onChange={(e) => setForm({ ...form, port: Number(e.target.value), allowUnencryptedConnection: false })}/></label>
      <label>Bağlantı güvenliği<select aria-label="TLS" value={form.tlsMode} onChange={(e) => changeSecurity(e.target.value as PopSecurity)}><option value="ssl">SSL/TLS (önerilen, 995)</option><option value="starttls">STARTTLS (110)</option><option value="none">Şifreleme yok (önerilmez)</option></select></label>
      <label>Kullanıcı adı<input placeholder="Kullanıcı adı" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })}/></label>
      <label>Parola / uygulama parolası<input type="password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })}/></label>
    </div>{form.tlsMode === 'none' && <label className="plaintext-consent" data-testid="pop-plaintext-consent"><input type="checkbox" checked={form.allowUnencryptedConnection} onChange={(e) => setForm({ ...form, allowUnencryptedConnection: e.target.checked })}/><span><strong>Şifresiz bağlantı riskini anlıyorum.</strong> Parola ve iletiler ağ üzerinde okunabilir.</span></label>}
      <button className="btn btn-outline-gray" disabled={form.tlsMode === 'none' && !form.allowUnencryptedConnection} onClick={create}>POP hesabını kaydet</button>
    </section>
    <section className="card mime-card"><h3>2. İndirilecek iletileri doğrulayın</h3><label>Kayıtlı hesap<select aria-label="POP hesabı" value={accountId} onChange={(e) => setAccountId(e.target.value)}>{accounts.map((account) => <option key={account.accountId} value={account.accountId}>{account.displayName}</option>)}</select></label><button className="btn btn-primary-orange" data-testid="pop-preview" disabled={!accountId} onClick={preview}>UIDL anlık görüntüsü oluştur</button>{plan && <p>{plan.items.length} ileti · değişmez UIDL planı</p>}</section>
    {plan && <section className="card mime-card"><h3>3. Doğrulanmış EML çıktısı</h3><button className="btn btn-outline-gray" onClick={pickOutput}>Çıktı klasörü seç</button><button className="btn btn-primary-orange" data-testid="pop-start" disabled={!output} onClick={start}>İndirmeyi başlat</button>{job && <p data-testid="pop-job-status">{job.stage} · {job.itemsWritten}/{job.totalItems}</p>}</section>}
  </section>;
}
