export type NavigationTab = 'jobs' | 'clients' | 'transfers' | 'search' | 'reports';

export type SourceKind = 'mailbox' | 'archive_file';

export interface SourceFolder {
  id: string;
  name: string;
  icon?: 'inbox' | 'send' | 'folder';
  count: number;
}

export interface DataSource {
  id: string;
  companyId: string;
  projectId: string;
  name: string;
  kind: SourceKind;
  type: 'server' | 'file';
  accountOrFileName: string;
  folders: SourceFolder[];
}

export interface Project {
  id: string;
  companyId: string;
  name: string;
  description?: string;
  sourceIds: string[];
}

export interface Company {
  id: string;
  name: string;
  code: string;
  description?: string;
  contactEmail?: string;
  projectIds: string[];
}

export interface Message {
  id: string;
  sourceId: string;
  companyId?: string;
  projectId?: string;
  folderId: string;
  folderName?: string;
  year: number;
  date: string; // DD.MM.YYYY
  dateTime: string; // DD.MM.YYYY HH:mm
  timestamp: number;
  sender: string;
  senderEmail: string;
  subject: string;
  preview: string;
  body: string;
  sizeBytes: number;
  sizeFormatted: string;
  hasAttachment: boolean;
  attachmentName?: string;
  attachmentSizeFormatted?: string;
  isDuplicate?: boolean;
  duplicateOfId?: string;
  isOversized?: boolean;
}

export interface FilterCriteria {
  searchTerm: string;
  year: 'all' | '2024' | '2023' | '2022';
  attachment: 'all' | 'with' | 'without';
  sender: string;
  minSizeBytes: number | null;
  maxSizeBytes: number | null;
}

export type JobType = 'migration' | 'convert' | 'archive' | 'recovery';

export type JobStatus =
  | 'running'
  | 'queued'
  | 'needs_attention'
  | 'draft'
  | 'partially_completed'
  | 'completed'
  | 'paused';

export interface JobEvent {
  time: string;
  text: string;
}

export interface Job {
  id: string;
  title: string;
  client: string;
  type: JobType;
  source: string;
  target: string;
  status: JobStatus;
  progressPercent: number;
  progressPhase?: string | null;
  phaseCompleted?: number | null;
  phaseTotal?: number | null;
  engineStatus?: string;
  engineError?: string | null;
  processedCount: number;
  totalCount: number;
  transferredCount: number;
  skippedCount: number;
  failedCount: number;
  pendingCount: number;
  recentEvents: JobEvent[];
  companyId?: string;
  projectId?: string;
  sourceId?: string;
  isLocalEngine?: boolean;
  jobKind?: string;
  archiveId?: string;
  archiveName?: string;
  reportAvailable?: boolean;
  waitingAtShutdown?: boolean;
  neverStartedQueued?: boolean;
  waitingPriority?: number;
}

export type DuplicatePolicy = 'skip' | 'overwrite' | 'separate_folder';
export type OversizedResolution = 'none' | 'skip_and_report' | 'change_target';

export interface TransferPlan {
  version: 1;
  id: string;
  name: string;
  companyId: string;
  projectId: string;
  sourceId: string;
  client: string;
  projectName?: string;
  operationType?: JobType;
  sourceType: string;
  sourceAccount: string;
  sourceFolderId: string;
  sourceFolderName: string;
  targetType: string;
  targetAccount: string;
  targetFolderName: string;
  filters: FilterCriteria;
  manualSelectionMode: boolean;
  selectedMessageIds: string[];
  duplicatePolicy: DuplicatePolicy;
  preserveSource: true;
  oversizedResolution: OversizedResolution;
  planHash: string;
  updatedAt: string;
}

export type CheckStatus = 'passed' | 'warning' | 'error';

export interface PreflightCheck {
  key: string;
  title: string;
  status: CheckStatus;
  resultLabel: string;
  detail?: string;
}

export interface PreflightState {
  planHash: string;
  checkedAt: string;
  isStale: boolean;
  hasBlocker: boolean;
  resolved: boolean;
  checks: PreflightCheck[];
  oversizedItem?: Message;
  duplicateCount: number;
  scopeCount: number;
  scopeBytes: number;
}

export type ItemTransferStatus = 'pending' | 'transferred' | 'skipped' | 'failed';

export interface RunItemState {
  messageId: string;
  subject: string;
  sender: string;
  sizeBytes: number;
  sizeFormatted: string;
  status: ItemTransferStatus;
  reason?: string;
  processedAt?: string;
}

export interface ActiveRunState {
  runId: string;
  jobId: string;
  status: 'running' | 'paused' | 'completed';
  startedAt: string;
  updatedAt: string;
  completedAt?: string;
  planSnapshot: TransferPlan;
  items: RunItemState[];
  retryHistoryCount: number;
}
