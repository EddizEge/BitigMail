import { test, type Page } from '@playwright/test';
import { spawn, spawnSync, type ChildProcessWithoutNullStreams } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Ortak TestingHost yardımcıları: derlenmiş TestingHost DLL'i proje SDK'sıyla doğrudan başlatılır (dotnet run yok),
// yalnız bu dosyanın başlattığı süreç ağacı kapatılır. Kullanıcı profiline ve 6174'teki gerçek motora dokunulmaz.
export const TESTING_HOST_PORT = 6175;
export const TESTING_HOST_URL = `http://127.0.0.1:${TESTING_HOST_PORT}`;
export const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const DOTNET_EXE = path.join(REPO_ROOT, '.tools', 'dotnet', 'dotnet.exe');
const TESTING_HOST_DLL = path.join(REPO_ROOT, 'engine', 'BitigMail.TestingHost', 'bin', 'Release', 'net8.0-windows', 'BitigMail.TestingHost.dll');
const UI_ORIGIN = 'http://127.0.0.1:5173';

export type TestingHostOptions = {
  /** Üretim kimlik denetimi (BITIGMAIL_TEST_PRODUCTION_SECURITY=1); ilk yönetici kanıtı stdin ile verilir. */
  secure?: { proof: Buffer; mimeFixture?: string };
};

export type TestingHost = {
  pid: number;
  stop: () => Promise<void>;
  output: () => string;
};

function portInUse(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = net.connect({ host: '127.0.0.1', port });
    socket.once('connect', () => { socket.destroy(); resolve(true); });
    socket.once('error', () => resolve(false));
    socket.setTimeout(1000, () => { socket.destroy(); resolve(false); });
  });
}

/** Yalnız verilen PID ve onun alt süreçleri kapatılır (ad / komut satırı ile toplu kapatma yok). */
function killOwnProcessTree(pid: number): void {
  spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
}

export async function startTestingHost(options: TestingHostOptions = {}): Promise<TestingHost> {
  if (!fs.existsSync(TESTING_HOST_DLL)) throw new Error(`TestingHost derlemesi bulunamadı: ${TESTING_HOST_DLL}`);
  if (await portInUse(TESTING_HOST_PORT)) {
    throw new Error(`${TESTING_HOST_PORT} portu kullanımda. Başka bir TestingHost çalışıyor olabilir; bu test başkasının sürecini kapatmaz.`);
  }
  const env: NodeJS.ProcessEnv = { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' };
  delete env.BITIGMAIL_TEST_PRODUCTION_SECURITY;
  delete env.BITIGMAIL_TEST_SECURE_MIME_FIXTURE;
  if (options.secure) {
    env.BITIGMAIL_TEST_PRODUCTION_SECURITY = '1';
    if (options.secure.mimeFixture) env.BITIGMAIL_TEST_SECURE_MIME_FIXTURE = options.secure.mimeFixture;
  }
  const child = spawn(DOTNET_EXE, [TESTING_HOST_DLL], { cwd: REPO_ROOT, env, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] }) as ChildProcessWithoutNullStreams;
  const pid = child.pid;
  if (pid === undefined) throw new Error('TestingHost başlatılamadı.');
  try { os.setPriority(pid, os.constants.priority.PRIORITY_BELOW_NORMAL); } catch { /* öncelik ayarı en iyi çaba */ }

  let log = '';
  const keep = (chunk: Buffer) => { log = (log + chunk.toString()).slice(-16_000); };
  child.stdout.on('data', keep);
  child.stderr.on('data', keep);
  if (options.secure) child.stdin.end(options.secure.proof); else child.stdin.end();

  const alive = () => child.exitCode === null && child.signalCode === null;
  // Test çalıştırıcısı beklenmedik biçimde kapanırsa kendi sürecimiz yetim kalmasın.
  const onExit = () => { if (alive()) killOwnProcessTree(pid); };
  process.once('exit', onExit);

  const runtimeDir = path.join(REPO_ROOT, 'runtime', options.secure ? 'testing-engine-secure' : 'testing-engine');
  const stop = async () => {
    process.removeListener('exit', onExit);
    if (alive()) {
      const exited = new Promise((resolve) => child.once('exit', resolve));
      killOwnProcessTree(pid);
      await Promise.race([exited, new Promise((resolve) => setTimeout(resolve, 10_000))]);
    }
    // Zorla kapatmada TestingHost kendi PID dosyasını silemez; yalnız bizim PID'imizi taşıyorsa temizlenir.
    const pidFile = path.join(runtimeDir, 'testinghost.pid');
    try { if (fs.readFileSync(pidFile, 'utf8').trim() === String(pid)) fs.rmSync(pidFile); } catch { /* dosya yok */ }
  };

  const deadline = Date.now() + 45_000;
  while (Date.now() < deadline) {
    if (!alive()) { await stop(); throw new Error(`TestingHost erken kapandı (çıkış ${child.exitCode}).\n${log}`); }
    const status = await fetch(`${TESTING_HOST_URL}/api/setup/status`, { headers: { Origin: UI_ORIGIN } }).then((r) => r.status).catch(() => 0);
    if (status === 200) return { pid, stop, output: () => log };
    await new Promise((resolve) => setTimeout(resolve, 300));
  }
  await stop();
  throw new Error(`TestingHost ${TESTING_HOST_PORT} portunda hazır olmadı.\n${log}`);
}

/** Dosyadaki testler için geliştirme TestingHost'unu beforeAll'da başlatır, afterAll'da kapatır. */
export function useDevelopmentTestingHost(): void {
  let host: TestingHost | null = null;
  test.beforeAll(async () => { host = await startTestingHost(); });
  test.afterAll(async () => { await host?.stop(); host = null; });
}

/**
 * Geliştirme TestingHost'unda oturum: bu motorda kimlik kataloğu boştur ve yerel kurulum kanıtı kanalı yoktur;
 * ilk yönetici orada bilerek oluşturulamaz. Böyle bir motora arayüz, IdentityGate'in mevcut geliştirme dalıyla
 * (anonim /api/session belirteci) bağlanır. Önce motorun gerçekten geliştirme TestingHost'u olduğu doğrulanır
 * (üretim motoru /api/session için 404 döner), sonra yalnız bu sayfada kurulum durumu yanıtı boş döndürülür.
 * IdentityGate, ilk yönetici kanıtı ve motorun belirteç / Origin / Host denetimleri değişmez.
 */
export async function openDevelopmentSession(page: Page): Promise<void> {
  const status = await fetch(`${TESTING_HOST_URL}/api/setup/status`, { headers: { Origin: UI_ORIGIN } }).then((r) => r.json()) as { initialized: boolean; nativeProofAvailable: boolean };
  if (status.initialized || status.nativeProofAvailable) {
    throw new Error('6175 portundaki motor kimlik kurulu ya da kurulum kanıtı açık; bu yardımcı yalnız geliştirme TestingHost içindir.');
  }
  const session = await fetch(`${TESTING_HOST_URL}/api/session`, { method: 'POST', headers: { Origin: UI_ORIGIN, 'Content-Type': 'application/json' }, body: '{}' });
  if (session.status !== 200) throw new Error(`Geliştirme oturumu açılamadı (HTTP ${session.status}); motor geliştirme TestingHost'u değil.`);
  await page.addInitScript((url) => { (window as any).__BITIGMAIL_ENGINE_URL__ = url; }, TESTING_HOST_URL);
  // 204 (gövdesiz): kurulum durumu okunamaz, tarayıcı konsoluna kaynak hatası düşmez.
  await page.route(`${TESTING_HOST_URL}/api/setup/status`, (route) => route.fulfill({ status: 204 }));
}
