import { APIRequestContext, expect, Page, request } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { mailDirectory } from '../playwright.config';
import { adminFile } from './global-setup';
import { latestLinkFor } from './mail';

export const adminPassword = 'e2e-admin-lantern-orbit-91';

/** An API client that sends the antiforgery header like the Angular app does. */
async function apiClient(): Promise<{ context: APIRequestContext; post: (url: string, data: object) => Promise<number> }> {
  const context = await request.newContext({ baseURL: 'http://localhost:4200' });
  let token = '';
  const refresh = async () => {
    token = ((await (await context.get('/api/v1/auth/antiforgery')).json()) as { requestToken: string }).requestToken;
  };
  await refresh();
  return {
    context,
    post: async (url: string, data: object) => {
      const response = await context.post(url, { data, headers: { 'X-XSRF-TOKEN': token } });
      if (url.endsWith('/auth/login')) await refresh();
      return response.status();
    },
  };
}

/** Sets an account's password from the newest invite email. */
async function activate(email: string, password: string): Promise<void> {
  const link = await latestLinkFor(mailDirectory, email, 'set-password');
  const token = decodeURIComponent(new URL(link).searchParams.get('token')!);
  const client = await apiClient();
  expect(await client.post('/api/v1/auth/reset-password', { token, newPassword: password })).toBe(204);
  await client.context.dispose();
}

/**
 * Signs in as the e2e Admin through the API, activating the account from its invite email first if no earlier test did.
 * Returns a client for creating test data quickly instead of clicking through the admin screens again.
 */
export async function adminApi() {
  const { email } = JSON.parse(readFileSync(adminFile, 'utf8')) as { email: string };
  let client = await apiClient();
  if ((await client.post('/api/v1/auth/login', { email, password: adminPassword })) !== 200) {
    await activate(email, adminPassword);
    client = await apiClient();
    expect(await client.post('/api/v1/auth/login', { email, password: adminPassword })).toBe(200);
  }

  const roles = (await (await client.context.get('/api/v1/roles')).json()) as { id: string; name: string }[];
  return {
    /** Creates an account with the given global role and sets its password from the invite email. */
    async createMember(email: string, fullName: string, roleName: string, password: string): Promise<void> {
      const globalRoleId = roles.find((r) => r.name === roleName)!.id;
      expect(await client.post('/api/v1/members', { email, fullName, globalRoleId, timeZone: 'UTC', employeeCode: null })).toBe(201);
      await activate(email, password);
    },
    dispose: () => client.context.dispose(),
  };
}

export async function signIn(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: /^Welcome/ })).toBeVisible();
}

export async function signOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Account menu' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/sign-in/);
}
