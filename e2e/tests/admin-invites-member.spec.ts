import { expect, Page, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { mailDirectory } from '../playwright.config';
import { adminFile } from '../support/global-setup';
import { latestLinkFor } from '../support/mail';

const adminPassword = 'e2e-admin-lantern-orbit-91';
const memberPassword = 'e2e-member-copper-river-37';

async function setPassword(page: Page, link: string, password: string): Promise<void> {
  await page.goto(link);
  await page.getByLabel('New password', { exact: true }).fill(password);
  await page.getByLabel('Confirm new password').fill(password);
  await page.getByRole('button', { name: 'Set password' }).click();
  await expect(page.getByRole('status')).toContainText('Your password has been set');
}

async function signIn(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

/** Journey 1 (docs/testing.md §3) plus journey 5's "no access by URL" check for members. */
test('admin invites a member, who sets a password, signs in and cannot reach admin pages', async ({ page }) => {
  test.slow(); // first pages compiled by a cold dev server
  const { email: adminEmail } = JSON.parse(readFileSync(adminFile, 'utf8')) as { email: string };
  const memberEmail = `e2e-dev-${Date.now()}@example.com`;

  // The first Admin activates their account from the create-admin invite.
  await setPassword(page, await latestLinkFor(mailDirectory, adminEmail, 'set-password'), adminPassword);
  await signIn(page, adminEmail, adminPassword);
  await expect(page.getByRole('heading', { name: 'Welcome, E2E Admin' })).toBeVisible();

  // Admin creates a Developer account.
  await page.getByRole('link', { name: 'Members' }).click();
  await page.getByRole('link', { name: 'Add member' }).click();
  // Wait for the form: the member list's "Search name, email or code" box would also match a loose label.
  await expect(page.getByRole('heading', { name: 'Add member' })).toBeVisible();
  await page.getByLabel('Email', { exact: true }).fill(memberEmail);
  await page.getByLabel('Full name', { exact: true }).fill('Dev Eloper');
  await page.getByRole('button', { name: 'Create and send invite' }).click();
  await expect(page.getByRole('heading', { name: 'Dev Eloper' })).toBeVisible();
  await expect(page.getByText('Invited (password not set yet)')).toBeVisible();

  await page.getByRole('button', { name: 'Account menu' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/sign-in/);

  // The member sets a password from their invite and signs in.
  await setPassword(page, await latestLinkFor(mailDirectory, memberEmail, 'set-password'), memberPassword);
  await signIn(page, memberEmail, memberPassword);
  await expect(page.getByRole('heading', { name: 'Welcome, Dev Eloper' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Members' })).toHaveCount(0);

  // Typing an admin URL shows "No access", and the API itself refuses (the UI is not the security boundary).
  await page.goto('/admin/members');
  await expect(page.getByRole('heading', { name: 'No access' })).toBeVisible();
  const apiStatus = await page.evaluate(async () => (await fetch('/api/v1/members')).status);
  expect(apiStatus).toBe(403);
});

test('wrong password shows one generic message', async ({ page }) => {
  await signIn(page, `nobody-${Date.now()}@example.com`, 'whatever-password-1');

  await expect(page.getByRole('alert')).toContainText('Email or password is incorrect');
  await expect(page).toHaveURL(/\/sign-in/);
});
