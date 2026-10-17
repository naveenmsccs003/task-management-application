import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { TaskDetail } from './task-detail';
import { Task } from './tasks.models';

const task: Task = {
  id: 't1',
  key: 'TMA-7',
  projectId: 'p1',
  projectKey: 'TMA',
  projectName: 'Task app',
  projectReadOnly: false,
  title: 'Build login',
  description: 'Details',
  type: 'Feature',
  priority: 'High',
  status: 'Draft',
  assignee: null,
  startDate: null,
  dueDate: '2026-11-01',
  originalDueDate: null,
  allocatedMinutes: 480,
  spentSeconds: 0,
  algorithmRequired: false,
  algorithmApproved: false,
  labels: ['backend'],
  currentRevision: 0,
  approvedRevision: null,
  flags: { overdue: false, overAllocated: false, extensionPending: false, reassignmentPending: false },
  checklist: { total: 0, done: 0, mandatoryTotal: 0, mandatoryDone: 0 },
  cancelReason: null,
  createdBy: { id: 'u1', name: 'Pat Manager' },
  createdAt: '2026-10-17T09:00:00Z',
  updatedAt: '2026-10-17T09:00:00Z',
  allowedActions: ['update', 'assign', 'cancel', 'delete', 'comment'],
  canViewAlgorithm: true,
  canViewTime: true,
  canViewDelays: true,
  version: 1,
};

describe('TaskDetail', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TaskDetail],
      providers: [provideRouter([]), provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.match('/api/v1/attachment-rules').forEach((r) => r.flush({ allowedTypes: [{ code: 'pdf', category: 'Document', extensions: ['.pdf'], maxBytes: 1048576 }], maxFilesPerRecord: 10 }));
    backend.verify();
  });

  async function render(value: Task) {
    const fixture = TestBed.createComponent(TaskDetail);
    fixture.componentRef.setInput('id', value.id);
    fixture.detectChanges();
    backend.expectOne('/api/v1/tasks/t1').flush(value);
    await fixture.whenStable();
    backend.expectOne('/api/v1/tasks/t1/history').flush([]);
    await fixture.whenStable();
    backend.expectOne('/api/v1/tasks/t1/comments').flush([]);
    await fixture.whenStable();
    flushSections();
    await fixture.whenStable();
    return fixture;
  }

  /** The algorithm, checklist and time sections load their own data when they are shown. */
  function flushSections() {
    backend.match('/api/v1/tasks/t1/algorithm').forEach((r) => r.flush([]));
    backend.match('/api/v1/tasks/t1/checklist').forEach((r) => r.flush([]));
    backend.match('/api/v1/tasks/t1/time').forEach((r) => r.flush(null));
    backend.match('/api/v1/tasks/t1/reviews').forEach((r) => r.flush([]));
    backend.match('/api/v1/tasks/t1/requests').forEach((r) => r.flush([]));
    backend.match('/api/v1/tasks/t1/delay-reports').forEach((r) => r.flush([]));
    backend.match('/api/v1/tasks/t1/attachments').forEach((r) => r.flush([]));
  }

  function buttons(element: HTMLElement): string[] {
    return [...element.querySelectorAll('.actions a, .actions button')].map((b) => b.textContent!.trim());
  }

  it('shows only the actions the server allows', async () => {
    const managerView = await render(task);
    expect(buttons(managerView.nativeElement)).toEqual(['Edit', 'Assign', 'Cancel task', 'Delete draft']);
  });

  it('shows no workflow buttons when the server allows none', async () => {
    const fixture = await render({ ...task, status: 'Cancelled', allowedActions: [] });

    const element = fixture.nativeElement as HTMLElement;
    expect(buttons(element)).toEqual([]);
    expect(element.querySelector('[data-testid="task-status"]')?.textContent).toBe('Cancelled');
    expect(element.textContent).not.toContain('Add a comment');
  });

  it('assigns the task to a member who can work on tasks', async () => {
    const fixture = await render(task);
    const element = fixture.nativeElement as HTMLElement;

    [...element.querySelectorAll<HTMLButtonElement>('.actions button')].find((b) => b.textContent!.includes('Assign'))!.click();
    await fixture.whenStable();
    backend.expectOne('/api/v1/projects/p1/members').flush([
      { userId: 'd1', fullName: 'Dev One', email: 'd1@x', isActive: true, projectRole: { id: 'r', name: 'Developer' }, canWorkOnTasks: true, openTaskCount: 0, addedAt: '' },
      { userId: 'v1', fullName: 'View Only', email: 'v1@x', isActive: true, projectRole: { id: 'v', name: 'Viewer' }, canWorkOnTasks: false, openTaskCount: 0, addedAt: '' },
    ]);
    await fixture.whenStable();

    const component = fixture.componentInstance as unknown as { assignable: () => { userId: string }[]; assigneeId: string; assign: () => Promise<void> };
    expect(component.assignable().map((m) => m.userId)).toEqual(['d1']);
    component.assigneeId = 'd1';
    const done = component.assign();
    const request = backend.expectOne('/api/v1/tasks/t1/assign');
    expect(request.request.body).toEqual({ assigneeId: 'd1', dueDate: null });
    request.flush({ ...task, status: 'ReadyToStart', assignee: { id: 'd1', name: 'Dev One' }, allowedActions: ['update', 'cancel', 'comment'] });
    await fixture.whenStable();
    backend.expectOne('/api/v1/tasks/t1/history').flush([]);
    await done;
    fixture.detectChanges();
    await fixture.whenStable();
    flushSections();

    expect(element.querySelector('[data-testid="task-status"]')?.textContent).toBe('Ready to start');
    expect(buttons(element)).toEqual(['Edit', 'Cancel task']);
  });
});
