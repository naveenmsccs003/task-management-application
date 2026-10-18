import { expect, Page, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { adminApi, signIn, signOut } from '../support/accounts';

const password = 'e2e-team-harbor-willow-63';

async function choose(page: Page, label: string, option: string | RegExp): Promise<void> {
  await page.getByLabel(label, { exact: true }).click();
  await page.getByRole('option', { name: option }).click();
}

async function addMember(page: Page, email: string, name: string, role: string): Promise<void> {
  await page.getByLabel('Find a person', { exact: true }).fill(email);
  await choose(page, 'Person', email); // emails are unique; names repeat across runs on a reused database
  await choose(page, 'Project role', role);
  await page.getByRole('button', { name: 'Add member' }).click();
  await expect(page.getByRole('cell', { name: new RegExp(`^${name}`) })).toBeVisible();
}

/**
 * Journey 2 (project, task, assignment, algorithm rejected then approved, comment and task files), journey 3 (work sessions, checklist, review
 * with a revision, approval and closure), journey 4 (deadline extension approved) and journey 5 (another developer gets
 * "not found" by URL) and journey 7 (the PM's dashboard and reports, with a CSV export).
 */
test('task lifecycle: setup, algorithm, work, review with revision, extension, closure; others cannot open it', async ({ page }) => {
  test.setTimeout(600_000); // four accounts and many sign-ins
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
  await page.getByLabel('An approved algorithm is required before work starts').check();
  await page.getByRole('button', { name: 'Create task' }).click();
  await expect(page.getByRole('heading', { name: `${key}-1 · Build the sign-up form` })).toBeVisible();
  await expect(page.getByTestId('task-status')).toHaveText('Draft');
  const taskUrl = page.url();
  await signOut(page);

  // The TL assigns it to the developer and adds a required checklist item.
  await signIn(page, tl.email, password);
  await page.goto(taskUrl);
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await choose(page, 'Assignee', new RegExp(dev.name));
  await page.getByRole('button', { name: 'Assign task' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Awaiting algorithm');
  await expect(page.getByText(`Assigned to ${dev.name}`)).toBeVisible();
  await page.getByLabel('New checklist item', { exact: true }).fill('Form validates email');
  await page.getByRole('button', { name: 'Add item' }).click();
  await expect(page.getByText('0 of 1 required items done.')).toBeVisible();
  await signOut(page);

  // The developer sees the assignment notification, opens it, comments and submits an algorithm.
  await signIn(page, dev.email, password);
  await expect(page.getByTestId('notifications-link')).toHaveAccessibleName(/Notifications, [1-9]\d* unread/);
  await page.getByTestId('notifications-link').click();
  await page.getByRole('button', { name: new RegExp(`${key}-1 was assigned to you`) }).click();
  await expect(page.getByRole('heading', { name: `${key}-1 · Build the sign-up form` })).toBeVisible();
  await page.getByRole('link', { name: 'Tasks', exact: true }).click();
  await page.getByRole('link', { name: `${key}-1` }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Awaiting algorithm');
  await expect(page.getByRole('button', { name: 'Assign', exact: true })).toHaveCount(0);
  await page.getByLabel('Add a comment', { exact: true }).fill('Starting tomorrow morning.');
  await page.getByLabel('Attach a file to the comment').setInputFiles({ name: 'plan.txt', mimeType: 'text/plain', buffer: Buffer.from('Day 1: form\nDay 2: tests\n') });
  await expect(page.getByRole('list', { name: 'Files to attach' })).toContainText('plan.txt');
  await page.getByRole('button', { name: 'Post comment' }).click();
  await expect(page.getByText('Starting tomorrow morning.')).toBeVisible();
  await expect(page.getByRole('list', { name: 'Comment files' }).getByRole('link', { name: 'plan.txt' })).toBeVisible();

  // Phase 9: task files are checked by content (an executable named .pdf is refused) and listed once stored.
  await page.getByLabel('Attach a file to the task').setInputFiles({ name: 'tool.pdf', mimeType: 'application/pdf', buffer: Buffer.from([0x4d, 0x5a, 0x90, 0, 3, 0]) });
  await expect(page.getByRole('alert').filter({ hasText: 'is not a supported file type' })).toBeVisible();
  await page.getByLabel('Attach a file to the task').setInputFiles({ name: 'mock-up.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.7\n%%EOF\n') });
  await expect(page.getByRole('list', { name: 'Task files' }).getByRole('link', { name: 'mock-up.pdf' })).toBeVisible();
  await page.getByLabel('Your algorithm', { exact: true }).fill('1. Render the form\n2. Submit to the API');
  await page.getByRole('button', { name: 'Submit algorithm' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Algorithm submitted');
  await signOut(page);

  // The TL rejects version 1 with a comment, after downloading the developer's file.
  await signIn(page, tl.email, password);
  await page.goto(taskUrl);
  const downloading = page.waitForEvent('download');
  await page.getByRole('list', { name: 'Task files' }).getByRole('link', { name: 'mock-up.pdf' }).click();
  expect((await downloading).suggestedFilename()).toBe('mock-up.pdf');
  await page.getByLabel('Comment (required to reject)', { exact: true }).fill('Validate the email before submitting.');
  await page.getByRole('button', { name: 'Reject' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Awaiting algorithm');
  await signOut(page);

  // The developer resubmits; the TL approves version 2.
  await signIn(page, dev.email, password);
  await page.goto(taskUrl);
  await expect(page.getByText('Validate the email before submitting.').first()).toBeVisible(); // algorithm section and history
  await page.getByLabel('Your algorithm', { exact: true }).fill('1. Render the form\n2. Validate email\n3. Submit to the API');
  await page.getByRole('button', { name: 'Submit algorithm' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Algorithm submitted');
  await signOut(page);
  await signIn(page, tl.email, password);
  await page.goto(taskUrl);
  await page.getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Ready to start');
  await expect(page.getByText('Approved — work can start.')).toBeVisible();
  await signOut(page);

  // Journey 3 (until review, Phase 8): start, pause, resume, tick the checklist, stop. The timer bar follows the session.
  await signIn(page, dev.email, password);
  await page.goto(taskUrl);
  await page.getByRole('button', { name: 'Start work' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('In progress');
  await expect(page.getByTestId('work-timer')).toBeVisible();
  await page.getByRole('button', { name: 'Pause', exact: true }).click();
  await page.getByLabel(/Reason for the pause/).fill('Lunch');
  await page.getByRole('button', { name: 'Pause work' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Paused');
  await expect(page.getByTestId('work-timer')).toHaveCount(0);
  await page.getByRole('button', { name: 'Resume' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('In progress');
  await page.getByRole('checkbox', { name: /Form validates email/ }).check();
  await expect(page.getByText('1 of 1 required items done.')).toBeVisible();
  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await page.getByLabel(/Why are you stopping/).fill('End of day');
  await page.getByRole('button', { name: 'Stop work' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Stopped');
  await expect(page.getByRole('table', { name: 'Work sessions' }).getByRole('row')).toHaveCount(3); // header + 2 sessions

  // Journey 3 continued: submit for review.
  await page.getByLabel('Summary of the work', { exact: true }).fill('Form built and validated.');
  await page.getByRole('button', { name: 'Submit for review' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('In review');
  await signOut(page);

  // The TL asks for changes and reopens the checklist item.
  await signIn(page, tl.email, password);
  await page.goto(taskUrl);
  await page.getByLabel('Review comment (required to request changes)', { exact: true }).fill('Show an error message for invalid emails.');
  // The reopen list appears once the review form has loaded the checklist; scope to it so the step waits for it.
  await page.getByRole('form', { name: 'Review the work' }).getByRole('checkbox', { name: 'Form validates email' }).check();
  await page.getByRole('button', { name: 'Request changes' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Revision requested');
  await signOut(page);

  // The developer reworks it, resubmits, and asks for more time (journey 4).
  await signIn(page, dev.email, password);
  await page.goto(taskUrl);
  await expect(page.getByText('0 of 1 required items done.')).toBeVisible();
  await page.getByRole('button', { name: 'Start work' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('In progress');
  await page.getByRole('checkbox', { name: /Form validates email/ }).first().check();
  await expect(page.getByText('1 of 1 required items done.')).toBeVisible();
  await page.getByLabel('New due date', { exact: true }).fill('2027-03-15');
  await page.getByLabel('Reason (at least 10 characters)', { exact: true }).last().fill('The extra validation needs more testing.');
  await page.getByRole('button', { name: 'Request extension' }).click();
  await expect(page.getByText('Extension pending')).toBeVisible();
  await page.getByLabel('Summary of the work', { exact: true }).fill('Error message added.');
  await page.getByRole('button', { name: 'Submit for review' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('In review');
  await signOut(page);

  // The TL approves the extension and the second revision; the PM closes the task.
  await signIn(page, tl.email, password);
  await page.getByRole('link', { name: 'Approvals' }).click();
  await page.getByRole('button', { name: 'Approve', exact: true }).first().click();
  await expect(page.getByText('Nothing is waiting for your decision.')).toBeVisible();
  await page.goto(taskUrl);
  await expect(page.getByText('Mar 15, 2027')).toBeVisible();
  await page.getByRole('button', { name: 'Approve', exact: true }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Approved');
  await signOut(page);
  await signIn(page, pm.email, password);
  await page.goto(taskUrl);
  await page.getByRole('button', { name: 'Close task' }).click();
  await expect(page.getByTestId('task-status')).toHaveText('Closed');
  await signOut(page);

  // Journey 5: another developer of the same project cannot open the task by URL.
  await signIn(page, other.email, password);
  await page.goto(taskUrl);
  await expect(page.getByRole('alert')).toContainText('not found');
  await expect(page.getByRole('heading', { name: /Build the sign-up form/ })).toHaveCount(0);
  await signOut(page);

  // Journey 7: the PM's dashboard shows the project's progress; reports show the work and export it as CSV.
  await signIn(page, pm.email, password);
  const card = page.getByTestId(`project-${key}`);
  await expect(card).toContainText('Closed 1');
  await expect(card.getByText('Extensions approved').locator('xpath=following-sibling::dd[1]')).toHaveText('1');
  await expect(card.getByText('Revisions requested').locator('xpath=following-sibling::dd[1]')).toHaveText('1');
  await page.getByRole('link', { name: 'Reports' }).click();
  await page.getByRole('button', { name: 'Show report' }).click();
  await expect(page.getByRole('table', { name: 'Time spent' }).getByRole('link', { name: `${key}-1` })).toBeVisible();
  await choose(page, 'Report', 'Revisions');
  await page.getByRole('button', { name: 'Show report' }).click();
  const revisions = page.getByRole('table', { name: 'Revisions' });
  await expect(revisions.getByRole('row').filter({ hasText: `${key}-1` })).toContainText('Closed');
  const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('link', { name: 'Export CSV' }).click()]);
  expect(download.suggestedFilename()).toMatch(/^revisions-\d{4}-\d{2}-\d{2}-to-\d{4}-\d{2}-\d{2}\.csv$/);
  const csv = readFileSync(await download.path(), 'utf8');
  expect(csv).toContain('Project,Task,Title,Assignee,Revisions requested,Current revision,Status,Last requested');
  expect(csv).toContain(`${key}-1,Build the sign-up form,Dana Developer,1,1,Closed,`);
});
