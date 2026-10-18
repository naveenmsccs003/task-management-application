import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { GitLabConnections } from './gitlab-connections';
import { describeRun, GitLabConnection, GitLabIssuePage, GitLabProjectLink, ImportRun } from './gitlab.models';
import { ProjectGitLab } from './project-gitlab';

const link: GitLabProjectLink = {
  id: 'l1', connectionId: 'c1', connectionName: 'Main GitLab', gitLabProjectId: 42, pathWithNamespace: 'acme/app',
  webUrl: 'https://gitlab.test/acme/app', syncEnabled: true, lastSyncedAt: null, hasWebhookSecret: false, webhookPath: '/api/v1/webhooks/gitlab/l1',
};

const page: GitLabIssuePage = {
  page: 1,
  hasMore: false,
  items: [
    { iid: 2, title: 'Password reset', isClosed: false, labels: [], milestoneDueDate: null, webUrl: 'u2', updatedAt: '', taskId: null, taskKey: null },
    { iid: 1, title: 'Sign-up form', isClosed: false, labels: ['bug'], milestoneDueDate: '2026-11-01', webUrl: 'u1', updatedAt: '', taskId: 't9', taskKey: 'TMA-9' },
  ],
};

const run: ImportRun = {
  id: 'r1', kind: 'Import', startedBy: { id: 'u', name: 'Pat' }, startedAt: '', finishedAt: '', created: 1, updated: 0, skipped: 0, failed: 0, errors: [],
};

describe('GitLab', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  /** Lets awaited HTTP calls and signal updates finish (they are plain promises, not tracked by the fixture). */
  async function settle(fixture: { detectChanges(): void }): Promise<void> {
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
  }

  it('describes import runs', () => {
    expect(describeRun({ ...run, created: 2, updated: 1, skipped: 3, failed: 0 })).toBe('2 created, 1 updated, 3 skipped, 0 failed');
  });

  it('marks imported issues and imports only the selected new ones', async () => {
    const fixture = TestBed.createComponent(ProjectGitLab);
    fixture.componentRef.setInput('projectId', 'p1');
    fixture.detectChanges();
    backend.expectOne('/api/v1/projects/p1/gitlab-link').flush(link);
    await settle(fixture);
    backend.expectOne((r) => r.url === '/api/v1/projects/p1/gitlab/issues').flush(page);
    backend.expectOne('/api/v1/projects/p1/gitlab/import-runs').flush([]);
    await settle(fixture);

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('acme/app');
    const boxes = [...element.querySelectorAll<HTMLInputElement>('input[type=checkbox]')];
    expect(boxes.map((b) => b.disabled)).toEqual([false, true]); // #1 is already TMA-9
    expect(element.querySelector('a[href="/tasks/t9"]')?.textContent).toContain('TMA-9');

    boxes[0].click();
    fixture.detectChanges();
    const importButton = [...element.querySelectorAll('button')].find((b) => b.textContent!.includes('as draft tasks'))!;
    importButton.click();
    const request = backend.expectOne('/api/v1/projects/p1/gitlab/import');
    expect(request.request.body).toEqual({ issueIids: [2] });
    request.flush(run);
    await settle(fixture);
    backend.expectOne((r) => r.url === '/api/v1/projects/p1/gitlab/issues').flush(page);
    await settle(fixture);
    expect(element.textContent).toContain('1 created, 0 updated, 0 skipped, 0 failed');
  });

  it('never shows or pre-fills tokens, and sends a new token only when one is typed', async () => {
    const connection: GitLabConnection = {
      id: 'c1', name: 'Main', baseUrl: 'https://gitlab.test', tokenHint: '…abcd', status: 'Healthy', lastCheckedAt: null, lastError: null,
      linkedProjects: 1, version: 3,
    };
    const fixture = TestBed.createComponent(GitLabConnections);
    fixture.detectChanges();
    backend.expectOne('/api/v1/gitlab/connections').flush([connection]);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('token …abcd');
    const remove = [...element.querySelectorAll('button')].find((b) => b.textContent!.trim() === 'Delete')!;
    expect(remove.disabled).toBe(true); // still linked

    [...element.querySelectorAll('button')].find((b) => b.textContent!.trim() === 'Edit')!.click();
    fixture.detectChanges();
    const token = element.querySelector<HTMLInputElement>('input[type=password]')!;
    expect(token.value).toBe('');
    [...element.querySelectorAll('button')].find((b) => b.textContent!.trim() === 'Save')!.click();
    const request = backend.expectOne('/api/v1/gitlab/connections/c1');
    expect(request.request.body).toEqual({ name: 'Main', baseUrl: 'https://gitlab.test', token: null, version: 3 });
    request.flush(connection);
    await settle(fixture);
  });
});
