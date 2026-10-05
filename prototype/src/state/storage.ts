import { ActiveRunState, Company, DataSource, Job, Project, TransferPlan } from '../types';
import { INITIAL_SAMPLE_JOBS } from '../data/sampleJobs';
import { SAMPLE_COMPANIES, SAMPLE_PROJECTS, SAMPLE_SOURCES } from '../data/sampleSources';

const STORAGE_KEYS = {
  PLAN: 'bitigmail_plan_v1',
  RUN: 'bitigmail_run_v1',
  JOBS: 'bitigmail_jobs_v1',
  VIEW: 'bitigmail_view_v1',
  COMPANIES: 'bitigmail_companies_v2',
  PROJECTS: 'bitigmail_projects_v2',
  SOURCES: 'bitigmail_sources_v2',
  SEARCH_SCOPE: 'bitigmail_search_scope_v2',
} as const;

export const DEFAULT_TRANSFER_PLAN: TransferPlan = {
  version: 1,
  id: 'plan-default',
  name: 'Posta geçişi',
  companyId: 'comp-ornek',
  projectId: 'proj-ornek-gecis',
  sourceId: 'src-ornek-imap',
  client: 'Örnek Şirket',
  projectName: 'Sistem Geçişi 2024',
  operationType: 'migration',
  sourceType: 'Birincil IMAP',
  sourceAccount: 'info@ornek.example',
  sourceFolderId: 'inbox',
  sourceFolderName: 'Gelen kutusu',
  targetType: 'Microsoft 365',
  targetAccount: 'info@hedef.example',
  targetFolderName: 'Gelen kutusu',
  filters: {
    searchTerm: '',
    year: '2024',
    attachment: 'all',
    sender: '',
    minSizeBytes: null,
    maxSizeBytes: null,
  },
  manualSelectionMode: false,
  selectedMessageIds: [],
  duplicatePolicy: 'skip',
  preserveSource: true,
  oversizedResolution: 'none',
  planHash: '',
  updatedAt: new Date().toISOString(),
};

export function computePlanHash(plan: TransferPlan): string {
  const parts = [
    plan.companyId || '',
    plan.projectId || '',
    plan.sourceId || '',
    plan.operationType || 'migration',
    plan.sourceAccount,
    plan.sourceFolderId,
    plan.targetType,
    plan.targetAccount,
    plan.targetFolderName,
    plan.filters.year,
    plan.filters.searchTerm.trim().toLowerCase(),
    plan.filters.attachment,
    plan.filters.sender.trim().toLowerCase(),
    plan.filters.minSizeBytes ?? '',
    plan.filters.maxSizeBytes ?? '',
    plan.manualSelectionMode ? 'manual' : 'auto',
    plan.manualSelectionMode ? [...plan.selectedMessageIds].sort().join(',') : '',
    plan.duplicatePolicy,
    plan.oversizedResolution,
  ];
  return parts.join('|');
}

export function loadSavedPlan(): TransferPlan {
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.PLAN);
    if (!raw) {
      const plan = { ...DEFAULT_TRANSFER_PLAN };
      plan.planHash = computePlanHash(plan);
      return plan;
    }
    const parsed = JSON.parse(raw);
    if (parsed && parsed.version === 1 && parsed.targetType) {
      const plan: TransferPlan = {
        ...DEFAULT_TRANSFER_PLAN,
        ...parsed,
        companyId: parsed.companyId || DEFAULT_TRANSFER_PLAN.companyId,
        projectId: parsed.projectId || DEFAULT_TRANSFER_PLAN.projectId,
        sourceId: parsed.sourceId || DEFAULT_TRANSFER_PLAN.sourceId,
        operationType: parsed.operationType || 'migration',
        filters: {
          ...DEFAULT_TRANSFER_PLAN.filters,
          ...(parsed.filters || {}),
        },
      };
      plan.planHash = computePlanHash(plan);
      return plan;
    }
    return { ...DEFAULT_TRANSFER_PLAN, planHash: computePlanHash(DEFAULT_TRANSFER_PLAN) };
  } catch {
    return { ...DEFAULT_TRANSFER_PLAN, planHash: computePlanHash(DEFAULT_TRANSFER_PLAN) };
  }
}

export function savePlan(plan: TransferPlan): boolean {
  try {
    const planWithHash: TransferPlan = {
      ...plan,
      planHash: computePlanHash(plan),
      updatedAt: new Date().toISOString(),
    };
    localStorage.setItem(STORAGE_KEYS.PLAN, JSON.stringify(planWithHash));
    return true;
  } catch {
    return false;
  }
}

export function loadSavedRun(): ActiveRunState | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.RUN);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (parsed && parsed.runId && Array.isArray(parsed.items) && parsed.planSnapshot) {
      return parsed as ActiveRunState;
    }
    return null;
  } catch {
    return null;
  }
}

export function saveActiveRun(run: ActiveRunState | null): void {
  try {
    if (!run) {
      localStorage.removeItem(STORAGE_KEYS.RUN);
    } else {
      localStorage.setItem(STORAGE_KEYS.RUN, JSON.stringify(run));
    }
  } catch {
    // ignore quota error
  }
}

export function loadSavedJobs(): Job[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.JOBS);
    if (!raw) return INITIAL_SAMPLE_JOBS;
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed) && parsed.length > 0) {
      return parsed as Job[];
    }
    return INITIAL_SAMPLE_JOBS;
  } catch {
    return INITIAL_SAMPLE_JOBS;
  }
}

export function saveSavedJobs(jobs: Job[]): void {
  try {
    localStorage.setItem(STORAGE_KEYS.JOBS, JSON.stringify(jobs));
  } catch {
    // ignore quota error
  }
}

export function loadSavedCompanies(): Company[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.COMPANIES);
    if (!raw) return SAMPLE_COMPANIES;
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed) && parsed.length > 0) return parsed;
    return SAMPLE_COMPANIES;
  } catch {
    return SAMPLE_COMPANIES;
  }
}

export function saveSavedCompanies(companies: Company[]): void {
  try {
    localStorage.setItem(STORAGE_KEYS.COMPANIES, JSON.stringify(companies));
  } catch {
    // ignore
  }
}

export function loadSavedProjects(): Project[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.PROJECTS);
    if (!raw) return SAMPLE_PROJECTS;
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed) && parsed.length > 0) return parsed;
    return SAMPLE_PROJECTS;
  } catch {
    return SAMPLE_PROJECTS;
  }
}

export function saveSavedProjects(projects: Project[]): void {
  try {
    localStorage.setItem(STORAGE_KEYS.PROJECTS, JSON.stringify(projects));
  } catch {
    // ignore
  }
}

export function loadSavedSources(): DataSource[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.SOURCES);
    if (!raw) return SAMPLE_SOURCES;
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed) && parsed.length > 0) return parsed;
    return SAMPLE_SOURCES;
  } catch {
    return SAMPLE_SOURCES;
  }
}

export function saveSavedSources(sources: DataSource[]): void {
  try {
    localStorage.setItem(STORAGE_KEYS.SOURCES, JSON.stringify(sources));
  } catch {
    // ignore
  }
}

export function loadSavedSearchScope(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.SEARCH_SCOPE);
    if (!raw) {
      // Default: select sources of the primary company
      return ['src-ornek-imap', 'src-ornek-exchange', 'src-ornek-pst'];
    }
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed)) return parsed;
    return ['src-ornek-imap', 'src-ornek-exchange', 'src-ornek-pst'];
  } catch {
    return ['src-ornek-imap', 'src-ornek-exchange', 'src-ornek-pst'];
  }
}

export function saveSavedSearchScope(scope: string[]): void {
  try {
    localStorage.setItem(STORAGE_KEYS.SEARCH_SCOPE, JSON.stringify(scope));
  } catch {
    // ignore
  }
}

export function loadCurrentView(): 'workspace' | 'preflight' | 'transfer' {
  try {
    const savedRun = loadSavedRun();
    if (savedRun && (savedRun.status === 'running' || savedRun.status === 'paused' || savedRun.status === 'completed')) {
      return 'transfer';
    }
    const raw = localStorage.getItem(STORAGE_KEYS.VIEW);
    if (raw === 'transfer' && savedRun) {
      return 'transfer';
    }
    if (raw === 'preflight' || raw === 'transfer' || raw === 'workspace') {
      return raw;
    }
    return 'workspace';
  } catch {
    return 'workspace';
  }
}

export function saveCurrentView(view: 'workspace' | 'preflight' | 'transfer'): void {
  try {
    localStorage.setItem(STORAGE_KEYS.VIEW, view);
  } catch {
    // ignore
  }
}

export function resetAllStorage(): void {
  try {
    localStorage.removeItem(STORAGE_KEYS.PLAN);
    localStorage.removeItem(STORAGE_KEYS.RUN);
    localStorage.removeItem(STORAGE_KEYS.JOBS);
    localStorage.removeItem(STORAGE_KEYS.VIEW);
    localStorage.removeItem(STORAGE_KEYS.COMPANIES);
    localStorage.removeItem(STORAGE_KEYS.PROJECTS);
    localStorage.removeItem(STORAGE_KEYS.SOURCES);
    localStorage.removeItem(STORAGE_KEYS.SEARCH_SCOPE);
  } catch {
    // ignore
  }
}
