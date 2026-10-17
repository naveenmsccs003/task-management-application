import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { TaskRequests } from './task-requests';
import { TaskReview } from './task-review';
import { ApprovalRequest, Task, TaskActionName } from './tasks.models';

function task(allowedActions: TaskActionName[], status: Task['status']): Task {
  return {
    id: 't1', key: 'TMA-7', projectId: 'p1', projectKey: 'TMA', projectName: 'Task app', projectReadOnly: false,
    title: 'Build login', description: '', type: 'Feature', priority: 'High', status,
    assignee: { id: 'd1', name: 'Dev One' }, startDate: null, dueDate: '2026-11-01', originalDueDate: '2026-11-01',
    allocatedMinutes: null, spentSeconds: 0, algorithmRequired: false, algorithmApproved: false, labels: [], currentRevision: 0,
    approvedRevision: null, flags: { overdue: false, overAllocated: false, extensionPending: true, reassignmentPending: false },
    checklist: { total: 0, done: 0, mandatoryTotal: 0, mandatoryDone: 0 }, cancelReason: null,
    createdBy: { id: 'u1', name: 'Pat' }, createdAt: '', updatedAt: '', allowedActions,
    canViewAlgorithm: true, canViewTime: true, canViewDelays: true, version: 1,
  };
}

const extension: ApprovalRequest = {
  id: 'r1', taskId: 't1', taskKey: 'TMA-7', taskTitle: 'Build login', projectId: 'p1', type: 'DeadlineExtension', status: 'Pending',
  reason: 'Waiting for the API team.', requestedBy: { id: 'd1', name: 'Dev One' }, requestedAt: '', proposedAssignee: null,
  algorithmReapprovalRequired: false, currentDueDate: '2026-11-01', proposedDueDate: '2026-11-08', approvedDueDate: null,
  decidedBy: null, decidedAt: null, decisionComment: null, canDecide: true, canWithdraw: false,
};

describe('review and request sections', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.match('/api/v1/attachment-rules').forEach((r) => r.flush({ allowedTypes: [{ code: 'pdf', category: 'Document', extensions: ['.pdf'], maxBytes: 1048576 }], maxFilesPerRecord: 10 }));
    backend.verify();
  });

  function buttons(element: HTMLElement): HTMLButtonElement[] {
    return [...element.querySelectorAll<HTMLButtonElement>('button')];
  }

  it('shows reviewers approve and request-changes, and requesting changes needs a comment', async () => {
    const fixture = TestBed.createComponent(TaskReview);
    fixture.componentRef.setInput('task', task(['review'], 'InReview'));
    fixture.detectChanges();
    backend.expectOne('/api/v1/tasks/t1/reviews').flush([]);
    await fixture.whenStable();
    backend.expectOne('/api/v1/tasks/t1/checklist').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(buttons(element).map((b) => b.textContent!.trim())).toEqual(['Approve', 'Request changes']);
    expect(buttons(element).find((b) => b.textContent!.includes('Request changes'))!.disabled).toBe(true);
    expect(element.textContent).not.toContain('Submit for review');
  });

  it('lets an approver grant a different date and sends it with the approval', async () => {
    const fixture = TestBed.createComponent(TaskRequests);
    fixture.componentRef.setInput('task', task([], 'InProgress'));
    fixture.detectChanges();
    backend.expectOne('/api/v1/tasks/t1/requests').flush([extension]);
    await fixture.whenStable();
    fixture.detectChanges();

    const component = fixture.componentInstance as unknown as { approvedDate: string; decide: (r: ApprovalRequest, d: 'approve' | 'reject') => Promise<void> };
    component.approvedDate = '2026-11-05';
    const done = component.decide(extension, 'approve');
    const request = backend.expectOne('/api/v1/requests/r1/approve');
    expect(request.request.body).toEqual({ comment: null, approvedDueDate: '2026-11-05' });
    request.flush([{ ...extension, status: 'Approved', approvedDueDate: '2026-11-05', canDecide: false }]);
    await done;
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('approved date 2026-11-05');
  });
});
