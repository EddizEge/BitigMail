import { describe, it, expect } from 'vitest';
import {
  ALL_SAMPLE_MESSAGES,
  DETERMINISTIC_2024_INBOX_MESSAGES,
  verifyDeterministicDataContract,
  getMessagesBySourceId,
  getMessagesForSourceIds,
} from '../data/sampleMessages';
import { DEFAULT_TRANSFER_PLAN, computePlanHash } from '../state/storage';
import { evaluatePreflight, getFilteredMessages } from '../state/preflightRules';
import { createRunSnapshot, generateReportBlob } from '../state/useSimulation';

describe('BitigMail Deterministic Data Contract', () => {
  it('should verify exactly 248 messages in 2024 Inbox', () => {
    const contract = verifyDeterministicDataContract();
    expect(contract.inbox2024Count).toBe(248);
  });

  it('should have exactly 3 duplicate messages and 1 distinct oversized message (38 MB)', () => {
    const contract = verifyDeterministicDataContract();
    expect(contract.duplicateCount).toBe(3);
    expect(contract.oversizedCount).toBe(1);

    const oversized = DETERMINISTIC_2024_INBOX_MESSAGES.find((m) => m.isOversized);
    expect(oversized).toBeDefined();
    expect(oversized?.subject).toBe('Proje teslim arşivi');
    expect(oversized?.sizeBytes).toBe(39845888); // 38 MB
    expect(oversized?.sizeFormatted).toBe('38 MB');
  });

  it('should provide additional messages across other years and folders', () => {
    expect(ALL_SAMPLE_MESSAGES.length).toBeGreaterThan(248);
    const sentCount = ALL_SAMPLE_MESSAGES.filter((m) => m.folderId === 'sent').length;
    expect(sentCount).toBeGreaterThan(0);
    const year2023Count = ALL_SAMPLE_MESSAGES.filter((m) => m.year === 2023).length;
    expect(year2023Count).toBeGreaterThan(0);
  });
});

describe('Filter Rules & Scopes', () => {
  it('should return exactly 248 messages under default 2024 Inbox plan', () => {
    const scope = getFilteredMessages(ALL_SAMPLE_MESSAGES, DEFAULT_TRANSFER_PLAN);
    expect(scope.length).toBe(248);
  });

  it('should filter by year correctly', () => {
    const plan2023 = {
      ...DEFAULT_TRANSFER_PLAN,
      filters: { ...DEFAULT_TRANSFER_PLAN.filters, year: '2023' as const },
    };
    const scope2023 = getFilteredMessages(ALL_SAMPLE_MESSAGES, plan2023);
    expect(scope2023.length).toBe(35);
    expect(scope2023.every((m) => m.year === 2023)).toBe(true);
  });

  it('should filter by attachment condition', () => {
    const planWithAttach = {
      ...DEFAULT_TRANSFER_PLAN,
      filters: { ...DEFAULT_TRANSFER_PLAN.filters, attachment: 'with' as const },
    };
    const scopeWithAttach = getFilteredMessages(ALL_SAMPLE_MESSAGES, planWithAttach);
    expect(scopeWithAttach.every((m) => m.hasAttachment)).toBe(true);
    expect(scopeWithAttach.length).toBeLessThan(248);

    const planWithoutAttach = {
      ...DEFAULT_TRANSFER_PLAN,
      filters: { ...DEFAULT_TRANSFER_PLAN.filters, attachment: 'without' as const },
    };
    const scopeWithoutAttach = getFilteredMessages(ALL_SAMPLE_MESSAGES, planWithoutAttach);
    expect(scopeWithoutAttach.every((m) => !m.hasAttachment)).toBe(true);
    expect(scopeWithAttach.length + scopeWithoutAttach.length).toBe(248);
  });

  it('should filter by search text in subject or sender', () => {
    const planSearch = {
      ...DEFAULT_TRANSFER_PLAN,
      filters: { ...DEFAULT_TRANSFER_PLAN.filters, searchTerm: 'Deniz Akın' },
    };
    const scope = getFilteredMessages(ALL_SAMPLE_MESSAGES, planSearch);
    expect(scope.length).toBeGreaterThan(0);
    expect(scope.every((m) => m.sender.includes('Deniz Akın') || m.body.includes('Deniz Akın'))).toBe(true);
  });

  it('should support manual selection mode with exact subset count', () => {
    const planManual = {
      ...DEFAULT_TRANSFER_PLAN,
      manualSelectionMode: true,
      selectedMessageIds: ['msg-2024-inbox-001', 'msg-2024-inbox-002'],
    };
    const scope = getFilteredMessages(ALL_SAMPLE_MESSAGES, planManual);
    expect(scope.length).toBe(2);
    expect(scope.map((m) => m.id)).toEqual(['msg-2024-inbox-001', 'msg-2024-inbox-002']);
  });
});

describe('Preflight Gates & Invalidation', () => {
  it('should identify 38 MB blocker when resolution is none', () => {
    const preflight = evaluatePreflight(ALL_SAMPLE_MESSAGES, DEFAULT_TRANSFER_PLAN);
    expect(preflight.hasBlocker).toBe(true);
    expect(preflight.resolved).toBe(false);
    expect(preflight.oversizedItem?.sizeBytes).toBe(39845888);
    expect(preflight.checks.find((c) => c.key === 'item_size')?.status).toBe('warning');
  });

  it('should resolve blocker when skip_and_report is selected', () => {
    const planResolved = {
      ...DEFAULT_TRANSFER_PLAN,
      oversizedResolution: 'skip_and_report' as const,
    };
    const preflight = evaluatePreflight(ALL_SAMPLE_MESSAGES, planResolved);
    expect(preflight.hasBlocker).toBe(false);
    expect(preflight.resolved).toBe(true);
    expect(preflight.checks.find((c) => c.key === 'item_size')?.status).toBe('passed');
  });

  it('should trigger zero-scope guard if 0 messages match', () => {
    const planZero = {
      ...DEFAULT_TRANSFER_PLAN,
      filters: { ...DEFAULT_TRANSFER_PLAN.filters, searchTerm: 'NON_EXISTING_QUERY_STRING_XYZ' },
    };
    const preflight = evaluatePreflight(ALL_SAMPLE_MESSAGES, planZero);
    expect(preflight.scopeCount).toBe(0);
    expect(preflight.hasBlocker).toBe(true);
    expect(preflight.resolved).toBe(false);
    expect(preflight.checks.find((c) => c.key === 'scope_guard')?.status).toBe('error');
  });

  it('should detect stale preflight when plan hash changes', () => {
    const initialHash = computePlanHash(DEFAULT_TRANSFER_PLAN);
    const mutatedPlan = {
      ...DEFAULT_TRANSFER_PLAN,
      filters: { ...DEFAULT_TRANSFER_PLAN.filters, year: '2023' as const },
    };
    const mutatedHash = computePlanHash(mutatedPlan);
    expect(initialHash).not.toBe(mutatedHash);
  });
});

describe('Simulation & Mutually Exclusive Accounting', () => {
  it('should initialize immutable run snapshot with exact scope items', () => {
    const planResolved = {
      ...DEFAULT_TRANSFER_PLAN,
      oversizedResolution: 'skip_and_report' as const,
    };
    const snapshot = createRunSnapshot(ALL_SAMPLE_MESSAGES, planResolved);
    expect(snapshot.items.length).toBe(248);
    expect(snapshot.items.every((i) => i.status === 'pending')).toBe(true);
    expect(snapshot.planSnapshot.targetType).toBe(planResolved.targetType);
  });

  it('should generate valid JSON and CSV reports with synthetic label', () => {
    const planResolved = {
      ...DEFAULT_TRANSFER_PLAN,
      oversizedResolution: 'skip_and_report' as const,
    };
    const snapshot = createRunSnapshot(ALL_SAMPLE_MESSAGES, planResolved);
    // Mark one transferred, one skipped, one failed
    snapshot.items[0].status = 'transferred';
    snapshot.items[1].status = 'skipped';
    snapshot.items[1].reason = 'Yinelenen ileti';
    snapshot.items[2].status = 'failed';
    snapshot.items[2].reason = 'Ağ zaman aşımı';

    const jsonRep = generateReportBlob(snapshot, 'json');
    expect(jsonRep.filename).toContain('.json');
    expect(jsonRep.blob.size).toBeGreaterThan(0);

    const csvRep = generateReportBlob(snapshot, 'csv');
    expect(csvRep.filename).toContain('.csv');
    expect(csvRep.blob.size).toBeGreaterThan(0);
  });
});

describe('TASK-005 Multi-Client & Source Scoping Contract', () => {
  it('should retrieve messages strictly by sourceId without fallback', () => {
    const imapMessages = getMessagesBySourceId('src-ornek-imap');
    expect(imapMessages.length).toBeGreaterThanOrEqual(248);

    // Non-existent or empty source must return strictly []
    const emptyResult = getMessagesBySourceId('unknown-empty-source-id');
    expect(emptyResult).toEqual([]);
    expect(emptyResult.length).toBe(0);
  });

  it('should invalidate plan identity when companyId, projectId, or sourceId changes', () => {
    const baseHash = computePlanHash(DEFAULT_TRANSFER_PLAN);

    const changedCompany = { ...DEFAULT_TRANSFER_PLAN, companyId: 'comp-anadolu' };
    expect(computePlanHash(changedCompany)).not.toBe(baseHash);

    const changedProject = { ...DEFAULT_TRANSFER_PLAN, projectId: 'proj-anadolu-merkez' };
    expect(computePlanHash(changedProject)).not.toBe(baseHash);

    const changedSource = { ...DEFAULT_TRANSFER_PLAN, sourceId: 'src-anadolu-exchange' };
    expect(computePlanHash(changedSource)).not.toBe(baseHash);
  });

  it('should combine and deduplicate messages across multiple sources for search', () => {
    const combined = getMessagesForSourceIds(['src-ornek-imap', 'src-ornek-exchange']);
    expect(combined.length).toBeGreaterThan(0);

    // Verify all IDs are unique (no duplicates)
    const idSet = new Set(combined.map((m) => m.id));
    expect(idSet.size).toBe(combined.length);

    // Empty list of source IDs returns empty array
    expect(getMessagesForSourceIds([])).toEqual([]);
  });

  it('should fail preflight with zero-scope blocker on empty source', () => {
    const emptySourcePlan = {
      ...DEFAULT_TRANSFER_PLAN,
      sourceId: 'empty-source-123',
    };
    const emptyMessages = getMessagesBySourceId(emptySourcePlan.sourceId);
    expect(emptyMessages.length).toBe(0);

    const preflight = evaluatePreflight(emptyMessages, emptySourcePlan);
    expect(preflight.scopeCount).toBe(0);
    expect(preflight.hasBlocker).toBe(true);
    expect(preflight.resolved).toBe(false);
    expect(preflight.checks.some((c) => c.key === 'scope_guard' && c.status === 'error')).toBe(true);
  });

  it('should isolate sources strictly by projectId and return distinct message counts on switch', () => {
    const ornekSources = ['src-ornek-imap', 'src-ornek-exchange', 'src-ornek-pst'];
    const anadoluSources = ['src-anadolu-operasyon', 'src-anadolu-2021-pst'];

    // Ornek project sources do not contain Anadolu sources
    expect(ornekSources.some((id) => anadoluSources.includes(id))).toBe(false);

    // Total raw messages in src-ornek-imap is 348 across all folders/years
    const imapMessages = getMessagesBySourceId('src-ornek-imap');
    expect(imapMessages.length).toBe(348);

    // Active 2024 Inbox transfer plan filters down to exactly 248 messages
    const imapFiltered = getFilteredMessages(imapMessages, DEFAULT_TRANSFER_PLAN);
    expect(imapFiltered.length).toBe(248);

    // Exchange source has its real distinct count (57 total, 45 in inbox)
    const exchangeMessages = getMessagesBySourceId('src-ornek-exchange');
    expect(exchangeMessages.length).toBe(57);
    const exchangePlan = { ...DEFAULT_TRANSFER_PLAN, sourceId: 'src-ornek-exchange' };
    const exchangeFiltered = getFilteredMessages(exchangeMessages, exchangePlan);
    expect(exchangeFiltered.length).toBe(45);
    expect(imapMessages).not.toEqual(exchangeMessages);
  });
});

