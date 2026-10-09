/// <reference types="vitest" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Arayüzde gösterilen sürüm: yayın betiği (scripts/build-windows-release.ps1 -Version x.y.z)
// BITIGMAIL_VERSION ortam değişkenini verir; doğrudan `npm run build` için son yayın sürümü kullanılır.
const DEFAULT_VERSION = '0.9.4';
const appVersion = /^\d{1,4}(\.\d{1,4}){2,3}$/.test(process.env.BITIGMAIL_VERSION ?? '')
  ? process.env.BITIGMAIL_VERSION as string
  : DEFAULT_VERSION;

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  define: {
    __BITIGMAIL_VERSION__: JSON.stringify(appVersion),
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
  },
  test: {
    environment: 'node',
    globals: true,
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['tests/e2e/**', 'node_modules/**', 'dist/**', '.playwright/**'],
  },
});
