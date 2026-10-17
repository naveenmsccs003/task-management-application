import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { ProjectDetail } from './project-detail';
import { Project } from './projects.models';

const project: Project = {
  id: 'p1',
  key: 'TMA',
  name: 'Task app',
  description: '',
  status: 'Active',
  startDate: null,
  targetEndDate: null,
  algorithmRequiredDefault: false,
  closeOnApproval: false,
  defaultChecklistTemplateId: null,
  memberCount: 3,
  openTaskCount: 2,
  myRole: { id: 'pm', name: 'Project Manager' },
  myPermissions: [
    { code: 'projects.view', scope: 'Project' },
    { code: 'projects.update', scope: 'Project' },
    { code: 'tasks.create', scope: 'Project' },
  ],
  createdAt: '2026-10-17T09:00:00Z',
  version: 1,
};

describe('ProjectDetail', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProjectDetail],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(value: Project) {
    const fixture = TestBed.createComponent(ProjectDetail);
    fixture.componentRef.setInput('id', value.id);
    fixture.detectChanges();
    backend.expectOne('/api/v1/projects/p1').flush(value);
    await fixture.whenStable();
    fixture.detectChanges();
    // The embedded task list loads the project's tasks.
    backend.match((r) => r.url === '/api/v1/tasks').forEach((r) => r.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 }));
    await fixture.whenStable();
    return fixture;
  }

  function click(element: HTMLElement, label: string): void {
    [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.trim() === label)!.click();
  }

  it('asks for confirmation before completing a project with open tasks', async () => {
    const fixture = await render(project);
    const element = fixture.nativeElement as HTMLElement;

    click(element, 'Mark completed');
    backend.expectOne('/api/v1/projects/p1/complete').flush(
      { status: 409, title: 'This project has 2 open tasks. They will stay as they are but become read-only. Confirm to continue.', code: 'project.has_open_tasks' },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('[role="alertdialog"]')?.textContent).toContain('2 open tasks');

    click(element, 'Confirm');
    const confirmed = backend.expectOne('/api/v1/projects/p1/complete');
    expect(confirmed.request.body).toEqual({ confirmOpenTasks: true });
    confirmed.flush({ ...project, status: 'Completed' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('[role="alertdialog"]')).toBeNull();
    expect(element.textContent).toContain('read-only');
    expect(element.textContent).not.toContain('New task');
  });

  it('hides management actions from members without projects.update', async () => {
    const fixture = await render({ ...project, myRole: { id: 'dev', name: 'Developer' }, myPermissions: [{ code: 'projects.view', scope: 'Project' }] });

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).not.toContain('Edit details');
    expect(element.textContent).not.toContain('Archive');
    expect(element.textContent).not.toContain('New task');
  });
});
