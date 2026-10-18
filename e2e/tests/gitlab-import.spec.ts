import { expect, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { createServer, Server } from 'node:http';
import { AddressInfo } from 'node:net';
import { adminApi, adminPassword, signIn, signOut } from '../support/accounts';
import { adminFile } from '../support/global-setup';

const password = 'e2e-gitlab-meadow-canyon-27';
const token = 'glpat-e2e-fake-token-7781';

interface Issue {
  id: number;
  iid: number;
  title: string;
  description: string;
  state: 'opened' | 'closed';
  labels: string[];
  updated_at: string;
}

/** A tiny GitLab REST API v4 with one project and two issues, checking the PRIVATE-TOKEN header. */
function fakeGitLab(issues: Issue[]): Server {
  const project = { id: 4242, name: 'website', path_with_namespace: 'acme/website', web_url: 'http://127.0.0.1/acme/website' };
  return createServer((request, response) => {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1');
    const send = (status: number, body?: unknown) => {
      response.writeHead(status, { 'Content-Type': 'application/json' });
      response.end(body === undefined ? '' : JSON.stringify(body));
    };
    if (request.headers['private-token'] !== token) return send(401, { message: '401 Unauthorized' });
    if (url.pathname === '/api/v4/user') return send(200, { id: 1, username: 'e2e' });
    if (url.pathname === '/api/v4/projects') return send(200, [project]);
    if (url.pathname === `/api/v4/projects/${project.id}`) return send(200, project);
    if (url.pathname === `/api/v4/projects/${project.id}/issues`) {
      const state = url.searchParams.get('state') ?? 'opened';
      const iids = url.searchParams.getAll('iids[]').map(Number);
      const list = issues
        .filter((i) => state === 'all' || i.state === state)
        .filter((i) => !iids.length || iids.includes(i.iid))
        .map((i) => ({ ...i, web_url: `http://127.0.0.1/acme/website/-/issues/${i.iid}`, milestone: null }));
      return send(200, list);
    }
    return send(404, { message: '404 Not Found' });
  });
}

/** Journey 6 (FR-IMP): an Admin adds a GitLab connection, a PM links a project and imports an issue, GitLab closes it. */
test('GitLab: connection, link, import as draft, closed issue only flags the task', async ({ page }) => {
  test.setTimeout(300_000);
  const issues: Issue[] = [
    { id: 11, iid: 1, title: 'Cookie banner', description: 'Show the banner once.', state: 'opened', labels: ['legal'], updated_at: new Date().toISOString() },
    { id: 12, iid: 2, title: 'Dark mode', description: '', state: 'opened', labels: [], updated_at: new Date().toISOString() },
  ];
  const server = fakeGitLab(issues);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const gitLabUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  try {
    const stamp = Date.now();
    const key = `G${String(stamp).slice(-6)}`;
    const connectionName = `Fake GitLab ${stamp}`;
    const pm = { email: `e2e-gl-pm-${stamp}@example.com`, name: 'Gale Manager' };
    const admin = await adminApi();
    await admin.createMember(pm.email, pm.name, 'Project Manager', password);
    await admin.dispose();

    // The Admin adds and tests the connection; the token is never shown again.
    const { email: adminEmail } = JSON.parse(readFileSync(adminFile, 'utf8')) as { email: string };
    await signIn(page, adminEmail, adminPassword);
    await page.getByRole('link', { name: 'GitLab' }).click();
    await page.getByLabel('Name', { exact: true }).fill(connectionName);
    await page.getByLabel('GitLab address', { exact: true }).fill(gitLabUrl);
    await page.getByLabel('Access token', { exact: true }).fill(token);
    await page.getByRole('button', { name: 'Add connection' }).click();
    const card = page.locator('mat-card').filter({ hasText: connectionName });
    await expect(card).toContainText('token …7781');
    await card.getByRole('button', { name: 'Test' }).click();
    await expect(page.getByTestId(`status-${connectionName}`)).toHaveText('Working');
    await expect(page.getByText(token)).toHaveCount(0);
    await signOut(page);

    // The PM creates a project, links it and imports issue #1 as a draft task.
    await signIn(page, pm.email, password);
    await page.getByRole('link', { name: 'Projects' }).click();
    await page.getByRole('link', { name: 'New project' }).click();
    await expect(page.getByRole('heading', { name: 'New project' })).toBeVisible();
    await page.getByLabel('Key', { exact: true }).fill(key.toLowerCase());
    await page.getByLabel('Name', { exact: true }).fill('E2E GitLab site');
    await page.getByRole('button', { name: 'Create project' }).click();
    await expect(page.getByRole('heading', { name: `${key} · E2E GitLab site` })).toBeVisible();
    await page.getByRole('tab', { name: 'GitLab' }).click();
    await page.getByLabel('GitLab connection', { exact: true }).click();
    await page.getByRole('option', { name: connectionName }).click();
    await page.getByRole('button', { name: 'Find', exact: true }).click();
    await page.getByRole('button', { name: 'Link acme/website' }).click();
    await expect(page.getByRole('link', { name: 'acme/website' })).toBeVisible();
    const table = page.getByRole('table', { name: 'GitLab issues' });
    await expect(table.getByRole('row')).toHaveCount(3); // header + 2 issues
    await page.getByRole('checkbox', { name: 'Select #1 Cookie banner' }).check();
    await page.getByRole('button', { name: /Import 1 as draft tasks/ }).click();
    await expect(page.getByText('1 created, 0 updated, 0 skipped, 0 failed')).toBeVisible();
    await expect(page.getByRole('checkbox', { name: 'Select #1 Cookie banner' })).toBeDisabled(); // never imported twice

    await table.getByRole('link', { name: `${key}-1` }).click();
    await expect(page.getByRole('heading', { name: `${key}-1 · Cookie banner` })).toBeVisible();
    await expect(page.getByTestId('task-status')).toHaveText('Draft');
    await expect(page.getByRole('link', { name: 'GitLab issue #1' })).toBeVisible();
    const taskUrl = page.url();

    // GitLab closes the issue: after a sync the task is flagged, its state unchanged (AC-IMP-2).
    issues[0].state = 'closed';
    issues[0].updated_at = new Date().toISOString();
    await page.goBack();
    await page.getByRole('tab', { name: 'GitLab' }).click();
    await page.getByRole('button', { name: 'Sync now' }).click();
    await expect(page.getByText('0 created, 1 updated, 0 skipped, 0 failed')).toBeVisible();
    // A closed issue leaves the list of open issues, so go back to the task itself.
    await page.goto(taskUrl);
    await expect(page.getByText('GitLab issue closed')).toBeVisible();
    await expect(page.getByTestId('task-status')).toHaveText('Draft');
    await signOut(page);
  } finally {
    server.close();
  }
});
