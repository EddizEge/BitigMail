import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocalEngineClient } from '../api/localEngineClient';

afterEach(() => vi.unstubAllGlobals());
describe('MIME HTTP contract', () => {
  it.each(['eml-files', 'eml-tree', 'mbox'] as const)('passes %s mode to the authenticated native-picker endpoint', async mode => {
    const fetchMock = vi.fn().mockResolvedValueOnce({ ok: true, json: async () => ({ token: 'memory-only' }) })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ cancelled: true }) });
    vi.stubGlobal('fetch', fetchMock);
    const client = new LocalEngineClient('http://127.0.0.1:6175');
    expect(await client.pickMimeSource(mode)).toEqual({ cancelled: true });
    expect(fetchMock.mock.calls[1][0]).toBe('http://127.0.0.1:6175/api/picker/mime-source');
    expect(fetchMock.mock.calls[1][1]).toMatchObject({ method: 'POST', cache: 'no-store', headers: { 'X-BitigMail-Session': 'memory-only', 'Content-Type': 'application/json' }, body: JSON.stringify({ mode }) });
  });
  it('keeps the immutable preview, source fingerprint and customer context in the start request', async () => {
    const payload = { sourceHandle: 'opaque-source', targetHandle: 'opaque-target', selectionId: 'opaque-preview', expectedSourceSha256: 'server-fingerprint', idempotencyKey: 'retry-key', clientContext: { companyId: 'c1', companyName: 'Şirket', projectId: 'p1', projectName: 'Arşiv' } };
    const fetchMock = vi.fn().mockResolvedValueOnce({ ok: true, json: async () => ({ token: 'memory-only' }) })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ jobId: 'actual-job', jobKind: 'mime-import', clientContext: payload.clientContext }) });
    vi.stubGlobal('fetch', fetchMock);
    const result = await new LocalEngineClient('http://127.0.0.1:6175').startMimeJob(payload);
    expect(result.jobKind).toBe('mime-import');
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual(payload);
  });
  it('preserves original-date filter values and camelCase preview results', async () => {
    const filters = { sourceHandle: 'opaque', folderIds: ['istanbul'], startDate: '2024-01-01', endDate: '2024-02-16' };
    const wire = { selectionId: 'selection', selectedMessagesCount: 3, selectedAttachmentsCount: 1, canConvert: true };
    const fetchMock = vi.fn().mockResolvedValueOnce({ ok: true, json: async () => ({ token: 'memory-only' }) }).mockResolvedValueOnce({ ok: true, json: async () => wire });
    vi.stubGlobal('fetch', fetchMock);
    expect(await new LocalEngineClient().previewMimeSelection(filters)).toEqual(wire);
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual(filters);
  });
  it.each([true, false])('reports server errors without inventing a successful analysis (JSON=%s)', async hasJson => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce({ ok: true, json: async () => ({ token: 'memory-only' }) }).mockResolvedValueOnce({ ok: false, status: 400, json: async () => { if (hasJson) return { error: 'Kaynak değişti' }; throw new Error('invalid JSON'); } }));
    await expect(new LocalEngineClient().analyzeMimeSource('opaque')).rejects.toThrow(hasJson ? 'Kaynak değişti' : 'HTTP 400');
  });
});
