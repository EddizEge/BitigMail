// @vitest-environment jsdom
import { render,screen } from '@testing-library/react';import { vi,test,expect } from 'vitest';
vi.mock('../api/localEngineClient',()=>({localEngineClient:{getSdkStatus:vi.fn().mockResolvedValue({licenseState:'unlicensed',initializationState:'initialized',restartRequired:false,qualification:'LICENSED_OUTPUT_ACCEPTANCE_PENDING'}),selectSdkLicense:vi.fn()}}));
import { SettingsDrawer } from '../components/settings/SettingsDrawer';
test('TASK-035 renders actionable SDK status',async()=>{render(<SettingsDrawer isOpen onClose={vi.fn()} onResetDemoData={vi.fn()}/>);const status=await screen.findByTestId('sdk-license-status');expect(status.textContent).toContain('Lisans yapılandırılmadı · SDK başlangıcı hazır');expect(status.textContent).toContain('Lisanslı çıktı kabulü henüz yapılmadı');expect(status.textContent).toContain('yeniden başlatılması gerekir');});
