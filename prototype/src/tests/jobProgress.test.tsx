// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { JobCenter } from '../components/jobs/JobCenter';
import { LocalEngineClient } from '../api/localEngineClient';
import { LocalJobRecord } from '../types/localEngine';
afterEach(cleanup);
const record = (status: LocalJobRecord['status'], jobKind = 'bridge-import'): LocalJobRecord => ({
  jobId: 'phase-job', jobKind, status, stage: 'Son doğrulama yapılıyor', percentComplete: 100,
  itemsRead: 12, itemsWritten: 12, totalItems: 12, failedItems: 0, currentFolder: '',
  sourceFileName: 'Kaynak', targetFileName: 'Hedef', createdAt: '2026-09-14T00:00:00Z',
  clientContext: { companyId: 'frozen-company', companyName: 'Kayıtlı Müşteri', projectId: 'frozen-project', projectName: 'Kayıtlı Proje' },
});
function setup(job: LocalJobRecord) {
  const client = new LocalEngineClient();
  vi.spyOn(client, 'getAllJobs').mockResolvedValue([job]);
  const resume = vi.spyOn(client, 'resumeBridgeImport').mockResolvedValue({ ...job, status: 'converting' });
  const props = { jobs: [], onToggleJobPause: vi.fn(), onNavigateToWorkspace: vi.fn(), onAddNewJob: vi.fn(), client };
  return { client, resume, props, view: render(<JobCenter {...props} />) };
}
describe('TASK021 truthful durable job progress', () => {
  it('all written does not imply completed; verification uses its own count', async () => {
    setup({ ...record('verifying'), progressPhase: 'verifying', phaseCompleted: 3, phaseTotal: 12 });
    await screen.findByText('Son doğrulama: 3 / 12 ileti');
    expect(screen.getByTestId('job-progress-bar').style.width).toBe('25%');
    expect(screen.queryByTestId('job-resume-bridge-btn')).toBeNull();
  });
  it('legacy unknown verification count stays indeterminate', async () => {
    setup(record('verifying'));
    await screen.findByText('Son doğrulama: — / — ileti');
    expect(screen.getByText('Doğrulanıyor')).toBeDefined();
  });
  it.each([['failed', 'bridge-import'], ['interrupted', 'convert'], ['interrupted', 'split']] as const)('does not offer unsupported resume for %s %s', async (status, kind) => {
    setup(record(status, kind));
    await screen.findByTestId('job-progress-bar');
    expect(screen.queryByTestId('job-resume-bridge-btn')).toBeNull();
    expect(screen.queryByTestId('job-resume-imap-btn')).toBeNull();
  });
  it('reload never resumes automatically; explicit click uses persisted owner and same job', async () => {
    const { props, view, resume } = setup(record('interrupted'));
    await screen.findByTestId('job-resume-bridge-btn');
    expect(resume).not.toHaveBeenCalled();
    view.unmount();
    render(<JobCenter {...props} />);
    const button = await screen.findByTestId('job-resume-bridge-btn');
    expect(resume).not.toHaveBeenCalled();
    fireEvent.click(button);
    await waitFor(() => expect(resume).toHaveBeenCalledExactlyOnceWith('phase-job', 'frozen-company', 'frozen-project', true));
  });
  it('requires reconnect before resume when authorization expired', async () => {
    setup({ ...record('interrupted'), errorMessage: '[reauthorization_required] connection expired' });
    await screen.findByText(/Önce ilgili hesabın bağlantısını yeniden kurun/);
    expect(screen.queryByTestId('job-resume-bridge-btn')).toBeNull();
  });
});
