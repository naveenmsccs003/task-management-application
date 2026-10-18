import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionGrant } from '../../core/auth/auth.models';
import { DashboardPage } from '../dashboard/dashboard';
import { Dashboard, ReportTable } from './reports.models';
import { Reports } from './reports';

const emptyList = { total: 0, items: [] };

const dashboard: Dashboard = {
  serverTime: '2026-10-18T10:00:00Z',
  timeZone: 'UTC',
  myWork: {
    byStatus: [{ status: 'InProgress', count: 1 }],
    activeSession: null,
    secondsThisWeek: 3900,
    dueSoon: emptyList,
    overdue: { total: 12, items: [{ id: 't1', key: 'WEB-1', title: 'Late task', status: 'InProgress', dueDate: '2026-10-15', assignee: null }] },
    waitingForFeedback: emptyList,
    revisionRequested: emptyList,
  },
  team: null,
  projects: {
    total: 1,
    items: [
      {
        id: 'p1', key: 'WEB', name: 'Website', status: 'Active', byStatus: [{ status: 'Draft', count: 2 }], allocatedSeconds: 7200, spentSeconds: 3600,
        overdue: 1, extensionsApproved: 2, revisionsRequested: 3,
        overdueTrend: [0, 1, 2].map((count, i) => ({ weekStart: `2026-10-0${i + 1}`, count })),
      },
    ],
  },
  admin: null,
};

const table: ReportTable = {
  report: 'time', title: 'Time spent', from: '2026-10-01', to: '2026-10-18', timeZone: 'UTC', truncated: false, generatedAt: '2026-10-18T10:00:00Z',
  columns: [
    { key: 'project', label: 'Project', kind: 'Text' },
    { key: 'task', label: 'Task', kind: 'Task' },
    { key: 'time', label: 'Time', kind: 'Duration' },
  ],
  rows: [
    ['WEB · Website', { id: 't1', key: 'WEB-1' }, 5400],
    ['WEB · Website', { id: 't2', key: 'WEB-2' }, 1800],
  ],
};

describe('Dashboard and reports', () => {
  let backend: HttpTestingController;

  function signIn(permissions: PermissionGrant[]): void {
    TestBed.inject(AuthService).setMe({ id: 'u1', email: 'a@example.com', fullName: 'Ada', timeZone: 'UTC', globalRole: { id: 'r', name: 'Developer' }, permissions });
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function settle(fixture: { detectChanges(): void }): Promise<void> {
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
  }

  it('shows my work, project progress and an accessible overdue trend', async () => {
    signIn([{ code: 'dashboard.view', scope: 'Project' }]);
    const fixture = TestBed.createComponent(DashboardPage);
    fixture.detectChanges();
    backend.expectOne('/api/v1/dashboard').flush(dashboard);
    await settle(fixture);

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent).toContain('Welcome, Ada');
    expect(element.querySelector('[data-testid="hours-this-week"]')?.textContent).toBe('1 h 05 m');
    expect(element.textContent).toContain('In progress 1');
    expect(element.querySelector('ul[aria-label="Overdue"]')?.textContent).toContain('WEB-1 · Late task');
    expect(element.textContent).toContain('and 11 more');
    const project = element.querySelector('[data-testid="project-WEB"]')!;
    expect(project.textContent).toContain('1 h 00 m spent of 2 h 00 m allocated');
    expect(project.querySelector('svg')?.getAttribute('aria-label')).toBe('Overdue tasks at the end of each of the last 3 weeks, oldest first: 0, 1, 2');
    expect(project.querySelectorAll('rect.mark').length).toBe(3);
    expect(element.textContent).not.toContain('Administration');
  });

  it('only greets members without dashboard.view', async () => {
    signIn([]);
    const fixture = TestBed.createComponent(DashboardPage);
    fixture.detectChanges();
    await settle(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toContain('Welcome, Ada');
    backend.expectNone('/api/v1/dashboard');
  });

  it('runs a report, links tasks, totals the time and offers CSV only when allowed', async () => {
    signIn([{ code: 'reports.view', scope: 'Project' }]);
    const fixture = TestBed.createComponent(Reports);
    fixture.detectChanges();
    backend.expectOne('/api/v1/reports').flush([
      { name: 'time', title: 'Time spent', description: 'Hours worked', usesDateRange: true, groupBy: ['task', 'day'], canExport: true },
      { name: 'overdue', title: 'Overdue tasks', description: 'Late now', usesDateRange: false, groupBy: [], canExport: false },
    ]);
    backend.expectOne((r) => r.url === '/api/v1/projects').flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
    await settle(fixture);

    const element = fixture.nativeElement as HTMLElement;
    (element.querySelector('form button[type="submit"]') as HTMLButtonElement).click();
    const request = backend.expectOne((r) => r.url === '/api/v1/reports/time');
    expect(request.request.params.get('groupBy')).toBe('task');
    expect(request.request.params.get('from')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    request.flush(table);
    await settle(fixture);

    expect(element.querySelector('table a')?.textContent).toBe('WEB-1');
    expect(element.querySelector('tbody')?.textContent).toContain('1 h 30 m');
    expect(element.querySelector('tfoot')?.textContent).toContain('2 h 00 m');
    const csv = element.querySelector('a[download]') as HTMLAnchorElement;
    expect(csv.getAttribute('href')).toContain('/api/v1/reports/time?');
    expect(csv.getAttribute('href')).toContain('format=csv');
  });
});
