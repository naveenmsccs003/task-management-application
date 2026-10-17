import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { TaskAlgorithm } from './task-algorithm';
import { TaskChecklist } from './task-checklist';
import { AlgorithmSubmission, ChecklistItem, Task, TaskActionName } from './tasks.models';

function task(allowedActions: TaskActionName[], status: Task['status'] = 'ReadyToStart'): Task {
  return {
    id: 't1', key: 'TMA-7', projectId: 'p1', projectKey: 'TMA', projectName: 'Task app', projectReadOnly: false,
    title: 'Build login', description: '', type: 'Feature', priority: 'High', status,
    assignee: { id: 'd1', name: 'Dev One' }, startDate: null, dueDate: '2026-11-01', originalDueDate: '2026-11-01',
    allocatedMinutes: 120, spentSeconds: 0, algorithmRequired: true, algorithmApproved: false, labels: [], currentRevision: 0,
    approvedRevision: null, flags: { overdue: false, overAllocated: false, extensionPending: false, reassignmentPending: false },
    checklist: { total: 1, done: 0, mandatoryTotal: 1, mandatoryDone: 0 },
    cancelReason: null, createdBy: { id: 'u1', name: 'Pat' }, createdAt: '', updatedAt: '', allowedActions,
    canViewAlgorithm: true, canViewTime: true, canViewDelays: true, version: 1,
  };
}

const pending: AlgorithmSubmission = {
  id: 'a1', version: 1, revision: 0, content: '1. Validate', submittedBy: { id: 'd1', name: 'Dev One' }, submittedAt: '',
  decision: 'Pending', decidedBy: null, decidedAt: null, decisionComment: null,
};

const item: ChecklistItem = {
  id: 'c1', sortOrder: 1, text: 'Tests pass', isMandatory: true, fromTemplate: true, isCompleted: false,
  completedBy: null, completedAt: null, note: null, version: 1,
};

describe('task sections', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  function buttons(element: HTMLElement): string[] {
    return [...element.querySelectorAll('button')].map((b) => b.textContent!.trim());
  }

  it('offers approve and reject only to reviewers, and reject needs a comment', async () => {
    const fixture = TestBed.createComponent(TaskAlgorithm);
    fixture.componentRef.setInput('task', task(['reviewAlgorithm'], 'AlgorithmSubmitted'));
    fixture.detectChanges();
    backend.expectOne('/api/v1/tasks/t1/algorithm').flush([pending]);
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(buttons(element)).toEqual(['Approve', 'Reject']);
    const reject = [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.includes('Reject'))!;
    expect(reject.disabled).toBe(true);

    const assigneeView = TestBed.createComponent(TaskAlgorithm);
    assigneeView.componentRef.setInput('task', task(['submitAlgorithm'], 'AwaitingAlgorithm'));
    assigneeView.detectChanges();
    backend.expectOne('/api/v1/tasks/t1/algorithm').flush([]);
    await assigneeView.whenStable();
    assigneeView.detectChanges();
    expect(buttons(assigneeView.nativeElement)).toEqual(['Submit algorithm']);
  });

  it('lets only the assignee tick checklist items and sends the note', async () => {
    const fixture = TestBed.createComponent(TaskChecklist);
    fixture.componentRef.setInput('task', task(['completeChecklist']));
    fixture.detectChanges();
    backend.expectOne('/api/v1/tasks/t1/checklist').flush([item]);
    await fixture.whenStable();
    fixture.detectChanges();

    const component = fixture.componentInstance as unknown as { note: string; toggle: (i: ChecklistItem, c: boolean) => Promise<void> };
    component.note = ' all green ';
    const done = component.toggle(item, true);
    const request = backend.expectOne('/api/v1/tasks/t1/checklist/c1/complete');
    expect(request.request.body).toEqual({ note: 'all green' });
    request.flush([{ ...item, isCompleted: true, completedBy: { id: 'd1', name: 'Dev One' }, note: 'all green' }]);
    await done;
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Ticked by Dev One');

    const viewer = TestBed.createComponent(TaskChecklist);
    viewer.componentRef.setInput('task', task([]));
    viewer.detectChanges();
    backend.expectOne('/api/v1/tasks/t1/checklist').flush([item]);
    await viewer.whenStable();
    viewer.detectChanges();
    expect((viewer.nativeElement as HTMLElement).querySelector('input[type=checkbox]')?.hasAttribute('disabled')).toBe(true);
    expect((viewer.nativeElement as HTMLElement).textContent).not.toContain('Add item');
  });
});
