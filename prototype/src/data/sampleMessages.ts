import { Message } from '../types';

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
}

// Generate deterministic 2024 Inbox messages for src-ornek-imap (exactly 248 items)
function generateDeterministic2024InboxMessages(): Message[] {
  const senders = [
    { name: 'Deniz Akın', email: 'deniz@ornek.example' },
    { name: 'Ece Yılmaz', email: 'ece@ornek.example' },
    { name: 'Murat Kaya', email: 'murat@ornek.example' },
    { name: 'Selin Demir', email: 'selin@ornek.example' },
    { name: 'Barış Öz', email: 'baris@ornek.example' },
    { name: 'Elif Yıldız', email: 'elif@ornek.example' },
    { name: 'Can Öztürk', email: 'can@ornek.example' },
    { name: 'Zeynep Aydın', email: 'zeynep@ornek.example' },
    { name: 'Ahmet Çelik', email: 'ahmet@ornek.example' },
    { name: 'Ayşe Korkmaz', email: 'ayse@ornek.example' },
  ];

  const subjects = [
    'Proje teslim belgeleri',
    'Aralık toplantı notları',
    'Teklif dosyası',
    'Hizmet sözleşmesi',
    'Kasım durum raporu',
    'Toplantı daveti',
    'Sistem geçiş planı',
    'Sunucu bakım bilgilendirmesi',
    'Finansal mutabakat özeti',
    'Q3 hedefleri ve bütçe',
    'Müşteri onay belgesi',
    'Saha test sonuçları',
    'Yeni lisans bildirimleri',
    'Arşiv aktarım protokolü',
    'Yedekleme planlaması',
    'Güvenlik denetim bulguları',
    'Ekim ayı fatura dökümü',
    'Kullanıcı yetkilendirme talebi',
    'Eğitim takvimi duyurusu',
    'Sözleşme eki imza süreci',
  ];

  const messages: Message[] = [];

  // Item 0: Exactly matches the visual mockup in bitigmail-workspace-v1.png!
  // Deniz Akın | Proje teslim belgeleri | 18.12.2024 | 2,4 MB
  messages.push({
    id: 'msg-2024-inbox-001',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '18.12.2024',
    dateTime: '18.12.2024 10:24',
    timestamp: new Date('2024-12-18T10:24:00').getTime(),
    sender: 'Deniz Akın',
    senderEmail: 'deniz@ornek.example',
    subject: 'Proje teslim belgeleri',
    preview: 'Merhaba, proje teslim belgelerini ekte paylaşıyorum.',
    body: 'Merhaba,\nproje teslim belgelerini ekte paylaşıyorum.\n\nİyi çalışmalar.',
    sizeBytes: 2516582, // ~2.4 MB
    sizeFormatted: '2,4 MB',
    hasAttachment: true,
    attachmentName: 'Teslim-belgeleri.pdf',
    attachmentSizeFormatted: '2,4 MB',
    isDuplicate: false,
    isOversized: false,
  });

  // Items from mockup table:
  // Ece Yılmaz | Aralık toplantı notları | 12.12.2024 | 84 KB
  messages.push({
    id: 'msg-2024-inbox-002',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '12.12.2024',
    dateTime: '12.12.2024 14:15',
    timestamp: new Date('2024-12-12T14:15:00').getTime(),
    sender: 'Ece Yılmaz',
    senderEmail: 'ece@ornek.example',
    subject: 'Aralık toplantı notları',
    preview: 'Geçtiğimiz hafta yapılan koordinasyon toplantısı kararları.',
    body: 'Sayın Yetkili,\n\nAralık ayı başında gerçekleştirilen koordinasyon toplantısında alınan kararlar ve aksiyon maddeleri ekteki belgede özetlenmiştir.\n\nBilgilerinize sunarız.',
    sizeBytes: 86016, // 84 KB
    sizeFormatted: '84 KB',
    hasAttachment: false,
    isDuplicate: false,
    isOversized: false,
  });

  // Murat Kaya | Teklif dosyası | 05.12.2024 | 1,2 MB
  messages.push({
    id: 'msg-2024-inbox-003',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '05.12.2024',
    dateTime: '05.12.2024 09:30',
    timestamp: new Date('2024-12-05T09:30:00').getTime(),
    sender: 'Murat Kaya',
    senderEmail: 'murat@ornek.example',
    subject: 'Teklif dosyası',
    preview: 'Örnek Şirket için güncellenen BT altyapı hizmet teklifi.',
    body: 'Merhabalar,\n\nTalep edilen BT altyapı modernizasyonu kapsamında hazırlanan teklif dosyasını ekte iletiyoruz.\n\nİyi günler.',
    sizeBytes: 1258291, // 1.2 MB
    sizeFormatted: '1,2 MB',
    hasAttachment: true,
    attachmentName: 'BT-Hizmet-Teklifi-v2.pdf',
    attachmentSizeFormatted: '1,2 MB',
    isDuplicate: false,
    isOversized: false,
  });

  // Selin Demir | Hizmet sözleşmesi | 21.11.2024 | 3,1 MB
  messages.push({
    id: 'msg-2024-inbox-004',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '21.11.2024',
    dateTime: '21.11.2024 16:45',
    timestamp: new Date('2024-11-21T16:45:00').getTime(),
    sender: 'Selin Demir',
    senderEmail: 'selin@ornek.example',
    subject: 'Hizmet sözleşmesi',
    preview: 'Hukuk birimimizce onaylanan sözleşme metni.',
    body: 'Merhaba,\n\nHukuk müşavirliği tarafından incelenip onaylanan hizmet sözleşmesinin imzalı kopyası ekte yer almaktadır.',
    sizeBytes: 3250585, // 3.1 MB
    sizeFormatted: '3,1 MB',
    hasAttachment: true,
    attachmentName: 'Hizmet-Sozlesmesi-Imzali.pdf',
    attachmentSizeFormatted: '3,1 MB',
    isDuplicate: false,
    isOversized: false,
  });

  // Deniz Akın | Kasım durum raporu | 08.11.2024 | 650 KB
  messages.push({
    id: 'msg-2024-inbox-005',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '08.11.2024',
    dateTime: '08.11.2024 11:10',
    timestamp: new Date('2024-11-08T11:10:00').getTime(),
    sender: 'Deniz Akın',
    senderEmail: 'deniz@ornek.example',
    subject: 'Kasım durum raporu',
    preview: 'Kasım ayı ilk hafta sistem metrikleri ve geçiş hazırlığı.',
    body: 'Selamlar,\n\nKasım ayının ilk haftasına ait sunucu metrikleri ve posta geçiş hazırlık durumunu ekli raporda inceleyebilirsiniz.',
    sizeBytes: 665600, // 650 KB
    sizeFormatted: '650 KB',
    hasAttachment: true,
    attachmentName: 'Kasim-Durum-Raporu.xlsx',
    attachmentSizeFormatted: '650 KB',
    isDuplicate: false,
    isOversized: false,
  });

  // Ece Yılmaz | Toplantı daveti | 01.11.2024 | 42 KB
  messages.push({
    id: 'msg-2024-inbox-006',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '01.11.2024',
    dateTime: '01.11.2024 08:50',
    timestamp: new Date('2024-11-01T08:50:00').getTime(),
    sender: 'Ece Yılmaz',
    senderEmail: 'ece@ornek.example',
    subject: 'Toplantı daveti',
    preview: 'Pazartesi günü saat 10:00 posta geçişi planlama toplantısı.',
    body: 'Herkese merhaba,\n\nPazartesi saat 10:00’da çevrim içi yapılacak geçiş planlama toplantısı takvim daveti eklenmiştir.',
    sizeBytes: 43008, // 42 KB
    sizeFormatted: '42 KB',
    hasAttachment: false,
    isDuplicate: false,
    isOversized: false,
  });

  // SPEC REQUIRED SPECIAL ITEM 1: Distinct Oversized Item (38 MB vs 35 MB Limit)
  // Matching preflight mockup: "Proje teslim arşivi" - 38 MB!
  messages.push({
    id: 'msg-2024-inbox-oversized',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '28.10.2024',
    dateTime: '28.10.2024 17:30',
    timestamp: new Date('2024-10-28T17:30:00').getTime(),
    sender: 'Murat Kaya',
    senderEmail: 'murat@ornek.example',
    subject: 'Proje teslim arşivi',
    preview: 'Tüm proje çizimleri ve ham tescil dokümanlarının toplu arşivi.',
    body: 'Sayın Yetkili,\n\nÖrnek Şirket geçiş öncesi projenin tüm ham çizim, vektör ve tescil dokümanları 38 MB boyutundaki arşiv olarak ekte sunulmuştur.\n\nNot: Hedef posta kutusu tekil ileti boyutu sınırı (35 MB) aşıldığı için ön kontrol aşamasında işlem gerektirir.',
    sizeBytes: 39845888, // exactly 38 MB
    sizeFormatted: '38 MB',
    hasAttachment: true,
    attachmentName: 'Proje-Teslim-Arsivi.zip',
    attachmentSizeFormatted: '38 MB',
    isDuplicate: false,
    isOversized: true,
  });

  // SPEC REQUIRED SPECIAL ITEMS 2, 3, 4: Exactly 3 Duplicates
  // Duplicate 1
  messages.push({
    id: 'msg-2024-inbox-dup-1',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '15.10.2024',
    dateTime: '15.10.2024 11:20',
    timestamp: new Date('2024-10-15T11:20:00').getTime(),
    sender: 'Selin Demir',
    senderEmail: 'selin@ornek.example',
    subject: 'Hizmet sözleşmesi (Kopya iletim)',
    preview: 'Önceki iletinin tekrar gönderilmiş kopyasıdır.',
    body: 'Merhaba,\n\nSistem kayıtlarında önceki iletinin tekrarı olarak algılanan yinelenen sözleşme nüshası.',
    sizeBytes: 154000,
    sizeFormatted: '150 KB',
    hasAttachment: false,
    isDuplicate: true,
    duplicateOfId: 'msg-2024-inbox-004',
    isOversized: false,
  });

  // Duplicate 2
  messages.push({
    id: 'msg-2024-inbox-dup-2',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '14.10.2024',
    dateTime: '14.10.2024 09:15',
    timestamp: new Date('2024-10-14T09:15:00').getTime(),
    sender: 'Murat Kaya',
    senderEmail: 'murat@ornek.example',
    subject: 'Teklif dosyası (Yinelenen kayıt)',
    preview: 'Aynı başlık ve içerikle gönderilmiş ikinci kopya.',
    body: 'Merhabalar,\n\nE-posta sunucusu senkronizasyonu kaynaklı yinelenen teklif iletisi.',
    sizeBytes: 320000,
    sizeFormatted: '312 KB',
    hasAttachment: false,
    isDuplicate: true,
    duplicateOfId: 'msg-2024-inbox-003',
    isOversized: false,
  });

  // Duplicate 3
  messages.push({
    id: 'msg-2024-inbox-dup-3',
    sourceId: 'src-ornek-imap',
    companyId: 'comp-ornek',
    projectId: 'proj-ornek-gecis',
    folderId: 'inbox',
    folderName: 'Gelen kutusu',
    year: 2024,
    date: '10.10.2024',
    dateTime: '10.10.2024 15:40',
    timestamp: new Date('2024-10-10T15:40:00').getTime(),
    sender: 'Ece Yılmaz',
    senderEmail: 'ece@ornek.example',
    subject: 'Aralık toplantı notları (Otomatik arşiv yedeği)',
    preview: 'Önceki toplantı notunun arşivdeki çift kaydı.',
    body: 'Merhaba,\n\nYinelenen posta kuralı test senaryosu için üretilmiş kopya ileti.',
    sizeBytes: 86016,
    sizeFormatted: '84 KB',
    hasAttachment: false,
    isDuplicate: true,
    duplicateOfId: 'msg-2024-inbox-002',
    isOversized: false,
  });

  // Generate remaining messages up to exactly 248 items
  const months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
  for (let i = 11; i <= 248; i++) {
    const sender = senders[i % senders.length];
    const subjectBase = subjects[i % subjects.length];
    const month = months[i % months.length];
    const day = (i % 27) + 1;
    const hour = (i % 9) + 9;
    const min = (i * 7) % 60;

    const dateStr = `${day < 10 ? '0' : ''}${day}.${month < 10 ? '0' : ''}${month}.2024`;
    const timeStr = `${hour < 10 ? '0' : ''}${hour}:${min < 10 ? '0' : ''}${min}`;
    const isoDateStr = `2024-${month < 10 ? '0' : ''}${month}-${day < 10 ? '0' : ''}${day}T${timeStr}:00`;

    const hasAttach = i % 3 === 0;
    const baseSize = hasAttach ? 450000 + (i * 12345) % 3500000 : 15000 + (i * 3421) % 95000;

    messages.push({
      id: `msg-2024-inbox-${i < 100 ? (i < 10 ? '00' + i : '0' + i) : i}`,
      sourceId: 'src-ornek-imap',
      companyId: 'comp-ornek',
      projectId: 'proj-ornek-gecis',
      folderId: 'inbox',
      folderName: 'Gelen kutusu',
      year: 2024,
      date: dateStr,
      dateTime: `${dateStr} ${timeStr}`,
      timestamp: new Date(isoDateStr).getTime(),
      sender: sender.name,
      senderEmail: sender.email,
      subject: `${subjectBase} #${i}`,
      preview: `2024 yılı kurumsal posta ileti örneği ${i}. Sentetik veri amaçlı üretilmiştir.`,
      body: `Sayın İlgili,\n\nBu ileti BitigMail P1 prototipi kapsamında oluşturulmuş deterministik sentetik bir örnektir (Kayıt #${i}).\nKonu: ${subjectBase}\nGönderen: ${sender.name}\n\nSaygılarımızla.`,
      sizeBytes: baseSize,
      sizeFormatted: formatBytes(baseSize),
      hasAttachment: hasAttach,
      attachmentName: hasAttach ? `Ek-Belge-${i}.pdf` : undefined,
      attachmentSizeFormatted: hasAttach ? formatBytes(baseSize - 12000) : undefined,
      isDuplicate: false,
      isOversized: false,
    });
  }

  return messages;
}

// Generate other folders for src-ornek-imap: 2023, 2022 Inbox + Sent + Projects
function generateOtherFolderMessages(): Message[] {
  const otherMessages: Message[] = [];

  // 2023 Inbox (35 messages)
  for (let i = 1; i <= 35; i++) {
    const day = (i % 27) + 1;
    const month = (i % 12) + 1;
    const dateStr = `${day < 10 ? '0' : ''}${day}.${month < 10 ? '0' : ''}${month}.2023`;
    const size = 35000 + i * 15000;
    otherMessages.push({
      id: `msg-2023-inbox-${i}`,
      sourceId: 'src-ornek-imap',
      companyId: 'comp-ornek',
      projectId: 'proj-ornek-gecis',
      folderId: 'inbox',
      folderName: 'Gelen kutusu',
      year: 2023,
      date: dateStr,
      dateTime: `${dateStr} 11:00`,
      timestamp: new Date(`2023-${month < 10 ? '0' : ''}${month}-${day < 10 ? '0' : ''}${day}T11:00:00`).getTime(),
      sender: 'Barış Öz',
      senderEmail: 'baris@ornek.example',
      subject: `2023 Arşiv Kaydı #${i}`,
      preview: `2023 yılı arşivlenmiş posta örneği #${i}.`,
      body: `2023 yılı kurumsal arşiv içeriği.\nKayıt no: ${i}`,
      sizeBytes: size,
      sizeFormatted: formatBytes(size),
      hasAttachment: i % 4 === 0,
      isDuplicate: false,
      isOversized: false,
    });
  }

  // 2022 Inbox (15 messages)
  for (let i = 1; i <= 15; i++) {
    const day = (i % 27) + 1;
    const month = (i % 12) + 1;
    const dateStr = `${day < 10 ? '0' : ''}${day}.${month < 10 ? '0' : ''}${month}.2022`;
    const size = 25000 + i * 8000;
    otherMessages.push({
      id: `msg-2022-inbox-${i}`,
      sourceId: 'src-ornek-imap',
      companyId: 'comp-ornek',
      projectId: 'proj-ornek-gecis',
      folderId: 'inbox',
      folderName: 'Gelen kutusu',
      year: 2022,
      date: dateStr,
      dateTime: `${dateStr} 09:30`,
      timestamp: new Date(`2022-${month < 10 ? '0' : ''}${month}-${day < 10 ? '0' : ''}${day}T09:30:00`).getTime(),
      sender: 'Elif Yıldız',
      senderEmail: 'elif@ornek.example',
      subject: `2022 Eski Arşiv İletisi #${i}`,
      preview: `2022 yılı eski sistem kayıt örneği #${i}.`,
      body: `2022 yılı eski posta kaydı.\nKayıt no: ${i}`,
      sizeBytes: size,
      sizeFormatted: formatBytes(size),
      hasAttachment: false,
      isDuplicate: false,
      isOversized: false,
    });
  }

  // Sent Folder (Gönderilenler - 30 messages)
  for (let i = 1; i <= 30; i++) {
    const day = (i % 27) + 1;
    const dateStr = `${day < 10 ? '0' : ''}${day}.11.2024`;
    const size = 45000 + i * 18000;
    otherMessages.push({
      id: `msg-sent-${i}`,
      sourceId: 'src-ornek-imap',
      companyId: 'comp-ornek',
      projectId: 'proj-ornek-gecis',
      folderId: 'sent',
      folderName: 'Gönderilenler',
      year: 2024,
      date: dateStr,
      dateTime: `${dateStr} 15:20`,
      timestamp: new Date(`2024-11-${day < 10 ? '0' : ''}${day}T15:20:00`).getTime(),
      sender: 'info@ornek.example',
      senderEmail: 'info@ornek.example',
      subject: `İletilen Yanıt #${i}`,
      preview: `Giden posta örneği #${i}.`,
      body: `Merhaba,\n\nTarafınıza gönderilmiş yanıt metnidir.\nKayıt #${i}`,
      sizeBytes: size,
      sizeFormatted: formatBytes(size),
      hasAttachment: i % 2 === 0,
      isDuplicate: false,
      isOversized: false,
    });
  }

  // Projects Folder (Projeler - 20 messages)
  for (let i = 1; i <= 20; i++) {
    const day = (i % 27) + 1;
    const dateStr = `${day < 10 ? '0' : ''}${day}.10.2024`;
    const size = 95000 + i * 45000;
    otherMessages.push({
      id: `msg-projects-${i}`,
      sourceId: 'src-ornek-imap',
      companyId: 'comp-ornek',
      projectId: 'proj-ornek-gecis',
      folderId: 'projects',
      folderName: 'Projeler',
      year: 2024,
      date: dateStr,
      dateTime: `${dateStr} 16:40`,
      timestamp: new Date(`2024-10-${day < 10 ? '0' : ''}${day}T16:40:00`).getTime(),
      sender: 'Zeynep Aydın',
      senderEmail: 'zeynep@ornek.example',
      subject: `Özel Proje Dokümanı #${i}`,
      preview: `Özel proje klasöründe saklanan ileti #${i}.`,
      body: `Proje çalışma notları ve teknik gereksinimler.\nKayıt #${i}`,
      sizeBytes: size,
      sizeFormatted: formatBytes(size),
      hasAttachment: true,
      attachmentName: `Proje-Dokuman-${i}.pdf`,
      attachmentSizeFormatted: formatBytes(size - 8000),
      isDuplicate: false,
      isOversized: false,
    });
  }

  return otherMessages;
}

// Generate distinct messages for additional sample sources
function generateSourceMessages(
  sourceId: string,
  companyId: string,
  projectId: string,
  prefix: string,
  senderName: string,
  senderEmail: string,
  inboxCount: number,
  sentCount: number,
  archiveCount: number
): Message[] {
  const list: Message[] = [];

  for (let i = 1; i <= inboxCount; i++) {
    const day = (i % 26) + 1;
    const dateStr = `${day < 10 ? '0' : ''}${day}.09.2024`;
    const size = 42000 + i * 14000;
    list.push({
      id: `${prefix}-inbox-${i}`,
      sourceId,
      companyId,
      projectId,
      folderId: 'inbox',
      folderName: 'Gelen kutusu',
      year: 2024,
      date: dateStr,
      dateTime: `${dateStr} 10:15`,
      timestamp: new Date(`2024-09-${day < 10 ? '0' : ''}${day}T10:15:00`).getTime(),
      sender: senderName,
      senderEmail,
      subject: `${prefix.toUpperCase()} İleti #${i}`,
      preview: `${senderName} tarafından gönderilen kurumsal ileti örneği #${i}.`,
      body: `Merhaba,\n\n${sourceId} kaynağına ait deneme iletisidir.\nSıra no: ${i}`,
      sizeBytes: size,
      sizeFormatted: formatBytes(size),
      hasAttachment: i % 3 === 0,
      attachmentName: i % 3 === 0 ? `Ek-${i}.pdf` : undefined,
      attachmentSizeFormatted: i % 3 === 0 ? formatBytes(size / 2) : undefined,
      isDuplicate: false,
      isOversized: false,
    });
  }

  for (let i = 1; i <= sentCount; i++) {
    const day = (i % 26) + 1;
    const dateStr = `${day < 10 ? '0' : ''}${day}.08.2024`;
    const size = 30000 + i * 11000;
    list.push({
      id: `${prefix}-sent-${i}`,
      sourceId,
      companyId,
      projectId,
      folderId: 'sent',
      folderName: 'Gönderilenler',
      year: 2024,
      date: dateStr,
      dateTime: `${dateStr} 14:45`,
      timestamp: new Date(`2024-08-${day < 10 ? '0' : ''}${day}T14:45:00`).getTime(),
      sender: senderEmail,
      senderEmail,
      subject: `${prefix.toUpperCase()} Giden Yanıt #${i}`,
      preview: `Giden e-posta iletimi #${i}.`,
      body: `Bilgilerinize sunulmuş giden e-posta metnidir.\nKayıt: ${i}`,
      sizeBytes: size,
      sizeFormatted: formatBytes(size),
      hasAttachment: false,
      isDuplicate: false,
      isOversized: false,
    });
  }

  for (let i = 1; i <= archiveCount; i++) {
    const day = (i % 26) + 1;
    const dateStr = `${day < 10 ? '0' : ''}${day}.05.2023`;
    const size = 85000 + i * 22000;
    list.push({
      id: `${prefix}-archive-${i}`,
      sourceId,
      companyId,
      projectId,
      folderId: 'archive',
      folderName: 'Arşiv',
      year: 2023,
      date: dateStr,
      dateTime: `${dateStr} 16:30`,
      timestamp: new Date(`2023-05-${day < 10 ? '0' : ''}${day}T16:30:00`).getTime(),
      sender: senderName,
      senderEmail,
      subject: `${prefix.toUpperCase()} Arşiv Belgesi #${i}`,
      preview: `Arşivlenmiş dosya iletisi #${i}.`,
      body: `Eski dönem arşiv dosyası kaydıdır.\nBelge No: ${i}`,
      sizeBytes: size,
      sizeFormatted: formatBytes(size),
      hasAttachment: true,
      attachmentName: `Arsiv-Belge-${i}.pdf`,
      attachmentSizeFormatted: formatBytes(size - 15000),
      isDuplicate: false,
      isOversized: false,
    });
  }

  return list;
}

export const DETERMINISTIC_2024_INBOX_MESSAGES = generateDeterministic2024InboxMessages();
export const OTHER_FOLDER_MESSAGES = generateOtherFolderMessages();

const ORNEK_IMAP_MESSAGES = [...DETERMINISTIC_2024_INBOX_MESSAGES, ...OTHER_FOLDER_MESSAGES];

const ORNEK_EXCHANGE_MESSAGES = generateSourceMessages(
  'src-ornek-exchange',
  'comp-ornek',
  'proj-ornek-gecis',
  'exch',
  'Yönetim Kurulu',
  'yonetim@ornek.example',
  45,
  12,
  0
);

const ORNEK_PST_MESSAGES = generateSourceMessages(
  'src-ornek-pst',
  'comp-ornek',
  'proj-ornek-gecis',
  'pst',
  'Arşiv Sorumlusu',
  'arsiv@ornek.example',
  0,
  0,
  60
);

const ORNEK_HUKUK_BOX_MESSAGES = generateSourceMessages(
  'src-ornek-hukuk-box',
  'comp-ornek',
  'proj-ornek-hukuk',
  'hukuk',
  'Hukuk Müşavirliği',
  'hukuk@ornek.example',
  25,
  0,
  0
);

const ORNEK_SOZLESMELER_MBOX_MESSAGES = generateSourceMessages(
  'src-ornek-sozlesmeler-mbox',
  'comp-ornek',
  'proj-ornek-hukuk',
  'sozlesme',
  'Sözleşme Arşivi',
  'sozlesme@ornek.example',
  0,
  0,
  35
);

const ANADOLU_OPERASYON_MESSAGES = generateSourceMessages(
  'src-anadolu-operasyon',
  'comp-anadolu',
  'proj-anadolu-merkez',
  'anadolu-op',
  'Lojistik Koordinasyon',
  'operasyon@anadolu.example',
  80,
  15,
  0
);

const ANADOLU_2021_PST_MESSAGES = generateSourceMessages(
  'src-anadolu-2021-pst',
  'comp-anadolu',
  'proj-anadolu-merkez',
  'anadolu-pst',
  'Filo Arşiv Yedeği',
  'filo-arsiv@anadolu.example',
  0,
  0,
  40
);

// Map of all messages by sourceId
export const ALL_MESSAGES_BY_SOURCE: Record<string, Message[]> = {
  'src-ornek-imap': ORNEK_IMAP_MESSAGES,
  'src-ornek-exchange': ORNEK_EXCHANGE_MESSAGES,
  'src-ornek-pst': ORNEK_PST_MESSAGES,
  'src-ornek-hukuk-box': ORNEK_HUKUK_BOX_MESSAGES,
  'src-ornek-sozlesmeler-mbox': ORNEK_SOZLESMELER_MBOX_MESSAGES,
  'src-anadolu-operasyon': ANADOLU_OPERASYON_MESSAGES,
  'src-anadolu-2021-pst': ANADOLU_2021_PST_MESSAGES,
};

// Returns messages strictly for the given sourceId. Returns [] if not found or empty.
// CRITICAL: NO FALLBACK TO ALL_SAMPLE_MESSAGES!
export function getMessagesBySourceId(sourceId: string): Message[] {
  if (!sourceId || !ALL_MESSAGES_BY_SOURCE[sourceId]) {
    return [];
  }
  return ALL_MESSAGES_BY_SOURCE[sourceId];
}

// Returns union of messages for a set of selected source IDs, deduplicated by ID.
export function getMessagesForSourceIds(sourceIds: string[]): Message[] {
  const result: Message[] = [];
  const seenIds = new Set<string>();

  for (const sId of sourceIds) {
    const list = ALL_MESSAGES_BY_SOURCE[sId] || [];
    for (const msg of list) {
      if (!seenIds.has(msg.id)) {
        seenIds.add(msg.id);
        result.push(msg);
      }
    }
  }
  return result;
}

// Backward-compatible export for existing legacy tests
export const ALL_SAMPLE_MESSAGES = ORNEK_IMAP_MESSAGES;

// Verify contract assertion helper
export function verifyDeterministicDataContract(): {
  inbox2024Count: number;
  duplicateCount: number;
  oversizedCount: number;
  totalBytes2024: number;
  formattedTotalSize: string;
} {
  const inbox2024 = DETERMINISTIC_2024_INBOX_MESSAGES.filter(
    (m) => m.folderId === 'inbox' && m.year === 2024
  );
  const duplicates = inbox2024.filter((m) => m.isDuplicate);
  const oversized = inbox2024.filter((m) => m.isOversized);
  const totalBytes = inbox2024.reduce((acc, m) => acc + m.sizeBytes, 0);

  return {
    inbox2024Count: inbox2024.length, // Exactly 248
    duplicateCount: duplicates.length, // Exactly 3
    oversizedCount: oversized.length, // Exactly 1
    totalBytes2024: totalBytes,
    formattedTotalSize: formatBytes(totalBytes),
  };
}
