// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ProjectImapAccounts } from '../components/clients/ProjectImapAccounts';
import { LocalEngineClient, validateGoogleAuthorizationUrl } from '../api/localEngineClient';

describe('TASK-031 Google OAuth', () => {
  it('accepts only the exact Google PKCE loopback authorization shape', () => {
    const client = '123-test.apps.googleusercontent.com';
    const valid = `https://accounts.google.com/o/oauth2/v2/auth?client_id=${client}&response_type=code&code_challenge_method=S256&redirect_uri=http%3A%2F%2F127.0.0.1%3A5144%2Fauthorize%2F`;
    expect(validateGoogleAuthorizationUrl(valid, client).valid).toBe(true);
    expect(validateGoogleAuthorizationUrl(valid.replace('accounts.google.com', 'evil.example'), client).valid).toBe(false);
    expect(validateGoogleAuthorizationUrl(valid.replace('S256', 'plain'), client).valid).toBe(false);
    expect(validateGoogleAuthorizationUrl(valid.replace('127.0.0.1', 'localhost'), client).valid).toBe(false);
  });

  it('renders a bounded Gmail/Workspace form and never persists client secret', async () => {
    const client = new LocalEngineClient('http://127.0.0.1:6174');
    client.listAccounts = vi.fn().mockResolvedValue([]);
    render(<ProjectImapAccounts companyId="company" projectId="project" projectName="Project" client={client} />);
    await waitFor(() => expect(client.listAccounts).toHaveBeenCalled());
    fireEvent.click(screen.getByTestId('add-account-btn-project'));
    fireEvent.click(screen.getByTestId('tab-google-oauth'));
    expect(screen.getByTestId('google-oauth-panel').textContent).toContain('imap.gmail.com:993');
    expect(screen.getByTestId('google-clientsecret-input').getAttribute('type')).toBe('password');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});
