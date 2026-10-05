// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { JobCenter } from '../components/jobs/JobCenter';
import { LocalEngineClient } from '../api/localEngineClient';

const record = (index: number) => ({
  jobId: `job-${String(index).padStart(5, '0')}`,
  jobKind: 'convert',
  sourceFileName: `source-${index}.ost`,
  targetFileName: `target-${index}.pst`,
  status: 'completed',
  stage: 'Tamamlandı',
  percentComplete: 100,
  itemsRead: 1,
  itemsWritten: 1,
  failedItems: 0,
  totalItems: 1,
  clientContext: { companyId: 'company', companyName: 'Company', projectId: 'project', projectName: 'Project' },
  createdAt: '2026-09-15T12:00:00Z',
});

describe('TASK-028 bounded job history and pending controls', () => {
  it('does not overlap polling while a slow page request is unresolved', async () => {
    vi.useFakeTimers();
    try {
      const client = new LocalEngineClient('http://127.0.0.1:6174');
      let resolvePage!: (value: any) => void;
      client.getJobsPage = vi.fn().mockReturnValue(new Promise(resolve => { resolvePage = resolve; }));
      render(<JobCenter jobs={[]} onToggleJobPause={vi.fn()} onNavigateToWorkspace={vi.fn()} onAddNewJob={vi.fn()} client={client} />);
      await vi.advanceTimersByTimeAsync(5_000);
      expect(client.getJobsPage).toHaveBeenCalledTimes(1);
      resolvePage({ items: [{ ...record(1), status: 'queued', waitingAtShutdown: true }], page: 1, pageSize: 50, totalCount: 1 });
      await vi.advanceTimersByTimeAsync(1_199);
      expect(client.getJobsPage).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(1);
      expect(client.getJobsPage).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('backs off after one transient polling failure and then recovers', async () => {
    vi.useFakeTimers();
    try {
      const client = new LocalEngineClient('http://127.0.0.1:6174');
      const active = { ...record(2), status: 'queued', waitingAtShutdown: true };
      client.getJobsPage = vi.fn()
        .mockResolvedValueOnce({ items: [active], page: 1, pageSize: 50, totalCount: 1 })
        .mockRejectedValueOnce(new Error('transient'))
        .mockResolvedValue({ items: [{ ...active, status: 'completed' }], page: 1, pageSize: 50, totalCount: 1 });
      render(<JobCenter jobs={[]} onToggleJobPause={vi.fn()} onNavigateToWorkspace={vi.fn()} onAddNewJob={vi.fn()} client={client} />);
      await vi.advanceTimersByTimeAsync(1_200);
      expect(client.getJobsPage).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(2_400);
      expect(client.getJobsPage).toHaveBeenCalledTimes(3);
    } finally {
      vi.useRealTimers();
    }
  });

  it('renders only the server page for a 10000-record history and requests the next stable page', async () => {
    const client = new LocalEngineClient('http://127.0.0.1:6174');
    client.getJobsPage = vi.fn().mockImplementation(({ page = 1 }) => Promise.resolve({
      items: Array.from({ length: 50 }, (_, offset) => record(10_000 - ((page - 1) * 50) - offset)),
      page,
      pageSize: 50,
      totalCount: 10_000,
    }));
    render(<JobCenter jobs={[]} onToggleJobPause={vi.fn()} onNavigateToWorkspace={vi.fn()} onAddNewJob={vi.fn()} client={client} />);

    await waitFor(() => expect(screen.getAllByTestId(/^job-row-/)).toHaveLength(50));
    expect(client.getJobsPage).toHaveBeenCalledWith({ page: 1, pageSize: 50, status: 'all', search: '' });
    fireEvent.click(screen.getByRole('button', { name: /sonraki/i }));
    await waitFor(() => expect(client.getJobsPage).toHaveBeenCalledWith({ page: 2, pageSize: 50, status: 'all', search: '' }));
    expect(screen.getAllByTestId(/^job-row-/)).toHaveLength(50);
  });

  it('resets pagination for server-side filters and removes a pending job through its frozen scope', async () => {
    const client = new LocalEngineClient('http://127.0.0.1:6174');
    const queued = { ...record(1), status: 'queued', waitingAtShutdown: true, neverStartedQueued: true };
    client.getJobsPage = vi.fn().mockResolvedValue({ items: [queued], page: 1, pageSize: 50, totalCount: 1 });
    client.cancelPendingJob = vi.fn().mockResolvedValue({ ...queued, status: 'cancelled' });
    render(<JobCenter jobs={[]} onToggleJobPause={vi.fn()} onNavigateToWorkspace={vi.fn()} onAddNewJob={vi.fn()} client={client} />);

    const cancel = await screen.findByTestId('job-cancel-pending-btn');
    fireEvent.click(cancel);
    await waitFor(() => expect(client.cancelPendingJob).toHaveBeenCalledWith('job-00001', 'company', 'project'));
    fireEvent.click(screen.getByTestId('status-tab-completed'));
    await waitFor(() => expect(client.getJobsPage).toHaveBeenCalledWith({ page: 1, pageSize: 50, status: 'completed', search: '' }));
  });

  it('trusts server-side public-field search results such as raw job id', async () => {
    const client = new LocalEngineClient('http://127.0.0.1:6174');
    client.getJobsPage = vi.fn().mockResolvedValue({ items: [record(42)], page: 1, pageSize: 50, totalCount: 1 });
    render(<JobCenter jobs={[]} onToggleJobPause={vi.fn()} onNavigateToWorkspace={vi.fn()} onAddNewJob={vi.fn()} client={client} />);
    await screen.findByTestId('job-row-job-00042');
    fireEvent.change(screen.getByTestId('job-search-input'), { target: { value: 'job-00042' } });
    await waitFor(() => expect(client.getJobsPage).toHaveBeenCalledWith({ page: 1, pageSize: 50, status: 'all', search: 'job-00042' }));
    expect(screen.getByTestId('job-row-job-00042')).toBeDefined();
  });

  it('keeps visible row highlight and detail selection consistent and hides detail for an empty result', async () => {
    const client = new LocalEngineClient('http://127.0.0.1:6174');
    client.getJobsPage = vi.fn().mockImplementation(({ search }) => Promise.resolve(
      search ? { items: [], page: 1, pageSize: 50, totalCount: 0 } : { items: [record(7)], page: 1, pageSize: 50, totalCount: 1 }
    ));
    render(<JobCenter jobs={[]} onToggleJobPause={vi.fn()} onNavigateToWorkspace={vi.fn()} onAddNewJob={vi.fn()} client={client} />);
    const row = await screen.findByTestId('job-row-job-00007');
    expect(row.className).toContain('selected');
    expect(screen.getByTestId('job-details-pane').textContent).toContain('source-7.ost');
    fireEvent.change(screen.getByTestId('job-search-input'), { target: { value: 'no-match' } });
    await waitFor(() => expect(screen.queryByTestId('job-details-pane')).toBeNull());
    expect(screen.queryByTestId('job-open-btn')).toBeNull();
  });
});
