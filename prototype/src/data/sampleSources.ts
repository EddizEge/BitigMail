import { Company, DataSource, Project, SourceFolder } from '../types';

export { type SourceFolder } from '../types';

export interface SourceAccount {
  id: string;
  name: string;
  type: 'server' | 'file';
  accountOrFileName: string;
  folders: SourceFolder[];
}

export const SAMPLE_COMPANIES: Company[] = [
  {
    id: 'comp-ornek',
    name: 'Örnek Şirket',
    code: 'ORNEK',
    description: 'Kurumsal BT modernizasyonu ve posta kutusu geçişi',
    contactEmail: 'info@ornek.example',
    projectIds: ['proj-ornek-gecis', 'proj-ornek-hukuk'],
  },
  {
    id: 'comp-anadolu',
    name: 'Anadolu Lojistik A.Ş.',
    code: 'ANADOLU',
    description: 'Lojistik filo iletişimi ve bölge operasyonları',
    contactEmail: 'operasyon@anadolu.example',
    projectIds: ['proj-anadolu-merkez'],
  },
];

export const SAMPLE_PROJECTS: Project[] = [
  {
    id: 'proj-ornek-gecis',
    companyId: 'comp-ornek',
    name: 'Sistem Geçişi 2024',
    description: 'Merkez posta sunucusundan bulut sistemine aktarım',
    sourceIds: ['src-ornek-imap', 'src-ornek-exchange', 'src-ornek-pst'],
  },
  {
    id: 'proj-ornek-hukuk',
    companyId: 'comp-ornek',
    name: 'Hukuk ve Uyum Arşivi',
    description: 'Sözleşmeler ve kurumsal uyum belgeleri arşivleme',
    sourceIds: ['src-ornek-hukuk-box', 'src-ornek-sozlesmeler-mbox'],
  },
  {
    id: 'proj-anadolu-merkez',
    companyId: 'comp-anadolu',
    name: 'Merkez Posta Konsolidasyonu',
    description: 'Bölge müdürlükleri posta ve arşiv aktarımı',
    sourceIds: ['src-anadolu-operasyon', 'src-anadolu-2021-pst'],
  },
];

export const SAMPLE_SOURCES: DataSource[] = [
  {
    id: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    name: 'Birincil IMAP',
    kind: 'mailbox',
    type: 'server',
    accountOrFileName: 'info@ornek.example',
    folders: [
      { id: 'inbox', name: 'Gelen kutusu', icon: 'inbox', count: 248 },
      { id: 'sent', name: 'Gönderilenler', icon: 'send', count: 30 },
      { id: 'projects', name: 'Projeler', icon: 'folder', count: 20 },
    ],
  },
  {
    id: 'src-ornek-exchange',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    name: 'Yönetici Posta Kutusu',
    kind: 'mailbox',
    type: 'server',
    accountOrFileName: 'yonetim@ornek.example',
    folders: [
      { id: 'inbox', name: 'Gelen kutusu', icon: 'inbox', count: 45 },
      { id: 'sent', name: 'Gönderilenler', icon: 'send', count: 12 },
    ],
  },
  {
    id: 'src-ornek-pst',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    name: 'Eski Arşiv 2022-2023.pst',
    kind: 'archive_file',
    type: 'file',
    accountOrFileName: 'Eski Arşiv 2022-2023.pst',
    folders: [
      { id: 'archive', name: 'Arşiv', icon: 'folder', count: 60 },
    ],
  },
  {
    id: 'src-ornek-hukuk-box',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-hukuk',
    name: 'Hukuk Departmanı',
    kind: 'mailbox',
    type: 'server',
    accountOrFileName: 'hukuk@ornek.example',
    folders: [
      { id: 'inbox', name: 'Gelen kutusu', icon: 'inbox', count: 25 },
    ],
  },
  {
    id: 'src-ornek-sozlesmeler-mbox',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-hukuk',
    name: 'Sözleşmeler Arşivi.mbox',
    kind: 'archive_file',
    type: 'file',
    accountOrFileName: 'Sözleşmeler Arşivi.mbox',
    folders: [
      { id: 'archive', name: 'Arşiv', icon: 'folder', count: 35 },
    ],
  },
  {
    id: 'src-anadolu-operasyon',
    companyId: 'comp-anadolu',
    projectId: 'proj-anadolu-merkez',
    name: 'Operasyon IMAP',
    kind: 'mailbox',
    type: 'server',
    accountOrFileName: 'operasyon@anadolu.example',
    folders: [
      { id: 'inbox', name: 'Gelen kutusu', icon: 'inbox', count: 80 },
      { id: 'sent', name: 'Gönderilenler', icon: 'send', count: 15 },
    ],
  },
  {
    id: 'src-anadolu-2021-pst',
    companyId: 'comp-anadolu',
    projectId: 'proj-anadolu-merkez',
    name: '2021_Yedek.pst',
    kind: 'archive_file',
    type: 'file',
    accountOrFileName: '2021_Yedek.pst',
    folders: [
      { id: 'archive', name: 'Arşiv', icon: 'folder', count: 40 },
    ],
  },
];

// Compatibility adapter for existing components
export const SAMPLE_SOURCE_ACCOUNTS: SourceAccount[] = SAMPLE_SOURCES.map((s) => ({
  id: s.id,
  name: s.name,
  type: s.type,
  accountOrFileName: s.accountOrFileName,
  folders: s.folders,
}));

export interface TargetOption {
  id: string;
  name: string;
  type: string;
  accountOrTarget: string;
  maxItemSizeBytes: number;
  maxItemSizeFormatted: string;
}

export const TARGET_OPTIONS: TargetOption[] = [
  {
    id: 'target-m365',
    name: 'Microsoft 365',
    type: 'server',
    accountOrTarget: 'info@hedef.example',
    maxItemSizeBytes: 35 * 1024 * 1024,
    maxItemSizeFormatted: '35 MB',
  },
  {
    id: 'target-imap',
    name: 'Yerel IMAP Sunucusu',
    type: 'server',
    accountOrTarget: 'yedek@kurum.example',
    maxItemSizeBytes: 50 * 1024 * 1024,
    maxItemSizeFormatted: '50 MB',
  },
  {
    id: 'target-gsuite',
    name: 'Google Workspace',
    type: 'server',
    accountOrTarget: 'arsiv@kurum.example',
    maxItemSizeBytes: 25 * 1024 * 1024,
    maxItemSizeFormatted: '25 MB',
  },
  {
    id: 'target-pst',
    name: 'Yeni PST Dosyası',
    type: 'file',
    accountOrTarget: 'Aktarim_Sonuc.pst',
    maxItemSizeBytes: 100 * 1024 * 1024,
    maxItemSizeFormatted: '100 MB',
  },
];

export function getCompanyById(id: string): Company | undefined {
  return SAMPLE_COMPANIES.find((c) => c.id === id);
}

export function getProjectById(id: string): Project | undefined {
  return SAMPLE_PROJECTS.find((p) => p.id === id);
}

export function getSourceById(id: string): DataSource | undefined {
  return SAMPLE_SOURCES.find((s) => s.id === id);
}
