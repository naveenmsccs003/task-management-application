import { expect, Page, test } from '@playwright/test';
import { adminApi, signIn, signOut } from '../support/accounts';

const password = 'e2e-team-harbor-willow-63';

async function choose(page: Page, label: string, option: string | RegExp): Promise<void> {
  await page.getByLabel(label, { exact: true }).click();
  await page.getByRole('option', { name: option }).click();
}

async function addMember(page: Page, email: string, name: string, role: string): Promise<void> {
  await page.getByLabel('Find a person', { exact: true }).fill(email);
  await choose(page, 'Person', new RegExp(name));
  await choose(page, 'Project role', role);
  await page.getByRole('button', { name: 'Add member' }).click();
  await expect(page.getByRole('cell', { name: new RegExp(`^${name}`) })).toBeVisible();
}

/**
 * Journey 2 up to assignment (the algorithm steps arrive in Phase 6) and journey 5: a PM sets up a project and a task in
 * the UI, the TL assigns it, the assignee sees and comments on it, and another developer gets "not found" by URL.
 */
test('PM creates a project and task, TL assigns it, and only the assignee can open it', async ({ page }) => {
  test.slow(); // four accounts and five sign-ins
  const stamp = Date.now();
  const key = `E${String(stamp).slice(-6)}`;
  const pm = { email: `e2e-pm-${stamp}@example.com`, name: 'Pat Manager' };
  const tl = { email: `e2e-tl-${stamp}@example.com`, name: 'Terry Lead' };
  const dev = { email: `e2e-dev1-${stamp}@example.com`, name: 'Dana Developer' };
  const other = { email: `e2e-dev2-${stamp}@example.com`, name: 'Olly Other' };

  const admin = await adminApi();
  await admin.createMember(pm.email, pm.name, 'Project Manager', password);
  await admin.createMember(tl.email, tl.name, 'Team Leader', password);
  await admin.createMember(dev.email, dev.name, 'Developer', password);
  await admin.createMember(other.email, other.name, 'Developer', password);
  await admin.dispose();

  // The PM creates the project and becomes its Project Manager.
  await signIn(page, pm.email, password);
  await page.getByRole('link', { name: 'Projects' }).click();
  await page.getByRole('link', { name: 'New project' }).click();
  // Wait for the form: the list page's "Search name or key" box would also match a loose label.
  await expect(page.getByRole('heading', { name: 'New project' })).toBeVisible();
  await page.getByLabel('Key', { exact: true }).fill(key.toLowerCase());
  await page.getByLabel('Name', { exact: true }).fill('E2E Website');
  await page.getByRole('button', { name: 'Create project' }).click();
  await expect(page.getByRole('heading', { name: `${key} · E2E Website` })).toBeVisible();
  await expect(page.getByText('Project Manager', { exact: true })).toBeVisible();

  // Members get project roles.
  await page.getByRole('tab', { name: /Members/ }).click();
  await addMember(page, tl.email, tl.name, 'Team Leader');
  await addMember(page, dev.email, dev.name, 'Developer');
  await addMember(page, other.email, other.name, 'Developer');

  // The PM creates a draft task.
  await page.getByRole('tab', { name: 'Tasks' }).click();
  await page.getByRole('link', { name: 'New task' }).click();
  await expect(page.getByRole('heading', { name: `New task in ${key}` })).toBeVisible();
  await page.getByLabel('Title', { exact: true }).fill('Build the sign-up form');
  await page.getByLabel('Due date', { exact: true }).fill('2027-03-01');
  await page.getByLabel('Allocated hours', { exact: true }).fill('6');
  await page.getByRole('button', { name: 'Create task' }).click();
  await expect(page.getByRole('heading', { name: `${key}-1 · Build the sign-up form` })).toBeVisible();
  await expect(page.getByTestId('task-status')).toHaveText('Draft');
  const taskUrl = page.url();
  await signOut(page);

  // The TL assigns it to the developer.
  await signIn(page, tl.email, password);
  await page.goto(taskUrl);
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await choose(page, 'Assignee', new RegExp(dev.name));
  await page.getByRole('button', { name: 'Assign task' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Ready to start');
  await expect(page.getByText(`Assigned to ${dev.name}`)).toBeVisible();
  await signOut(page);

  // The developer finds it in their task list and comments.
  await signIn(page, dev.email, password);
  await page.getByRole('link', { name: 'Tasks' }).click();
  await page.getByRole('link', { name: `${key}-1` }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Ready to start');
  await expect(page.getByRole('button', { name: 'Assign', exact: true })).toHaveCount(0);
  await page.getByLabel('Add a comment', { exact: true }).fill('Starting tomorrow morning.');
  await page.getByRole('button', { name: 'Post comment' }).click();
  await expect(page.getByText('Starting tomorrow morning.')).toBeVisible();
  await signOut(page);

  // Journey 5: another developer of the same project cannot open the task by URL.
  await signIn(page, other.email, password);
  await page.goto(taskUrl);
  await expect(page.getByRole('alert')).toContainText('not found');
  await expect(page.getByRole('heading', { name: /Build the sign-up form/ })).toHaveCount(0);
});
