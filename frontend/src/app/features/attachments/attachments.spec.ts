import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { AttachmentList } from './attachment-list';
import { AttachmentPicker } from './attachment-picker';
import { acceptList, Attachment, AttachmentRules, checkFile, formatFileSize } from './attachments.models';
import { TaskAttachmentsStore } from './task-attachments.store';

const rules: AttachmentRules = {
  allowedTypes: [
    { code: 'pdf', category: 'Document', extensions: ['.pdf'], maxBytes: 1024 * 1024 },
    { code: 'jpeg', category: 'Image', extensions: ['.jpg', '.jpeg'], maxBytes: 100 },
  ],
  maxFilesPerRecord: 2,
};

function attachment(overrides: Partial<Attachment> = {}): Attachment {
  return {
    id: 'f1', ownerType: 'Task', ownerId: 't1', fileName: 'spec.pdf', contentType: 'application/pdf', fileType: 'pdf',
    category: 'Document', sizeBytes: 2048, scanStatus: 'Clean', uploadedBy: { id: 'u1', name: 'Dev One' }, uploadedAt: '',
    canPreview: false, canDelete: false, ...overrides,
  };
}

describe('attachment helpers', () => {
  it('formats sizes', () => {
    expect(formatFileSize(900)).toBe('900 B');
    expect(formatFileSize(1536)).toBe('1.5 KB');
    expect(formatFileSize(25 * 1024 * 1024)).toBe('25 MB');
  });

  it('checks the extension and the size limit of its type before uploading', () => {
    expect(checkFile({ name: 'a.PDF', size: 10 }, rules)).toBeNull();
    expect(checkFile({ name: 'a.exe', size: 10 }, rules)).toContain('not an allowed file type');
    expect(checkFile({ name: 'noextension', size: 10 }, rules)).toContain('not an allowed file type');
    expect(checkFile({ name: 'a.jpg', size: 101 }, rules)).toContain('too large');
    expect(checkFile({ name: 'a.pdf', size: 0 }, rules)).toContain('empty');
    expect(acceptList(rules)).toBe('.pdf,.jpg,.jpeg');
  });
});

describe('attachment components', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting(), TaskAttachmentsStore],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('links only scanned files for download and shows why others are held', () => {
    const fixture = TestBed.createComponent(AttachmentList);
    fixture.componentRef.setInput('attachments', [
      attachment(),
      attachment({ id: 'f2', fileName: 'wait.pdf', scanStatus: 'Pending' }),
      attachment({ id: 'f3', fileName: 'bad.pdf', scanStatus: 'Infected', canDelete: true }),
    ]);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const links = [...element.querySelectorAll('a')];
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/api/v1/attachments/f1/download']);
    expect(element.textContent).toContain('Checking for malware…');
    expect(element.textContent).toContain('Blocked: malware found');
    expect([...element.querySelectorAll('button')].map((b) => b.getAttribute('aria-label'))).toEqual(['Delete bad.pdf']);
  });

  it('refuses a wrong file type without uploading, and keeps uploaded drafts for the form', async () => {
    const fixture = TestBed.createComponent(AttachmentPicker);
    fixture.componentRef.setInput('taskId', 't1');
    fixture.componentRef.setInput('ownerType', 'Comment');
    fixture.detectChanges();
    backend.expectOne('/api/v1/attachment-rules').flush(rules);
    await fixture.whenStable();

    const picker = fixture.componentInstance;
    await picker.uploadAll([new File(['MZ'], 'tool.exe')]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('“tool.exe” is not an allowed file type.');

    const done = picker.uploadAll([new File(['%PDF-1.7'], 'notes.pdf')]);
    const request = backend.expectOne((r) => r.url === '/api/v1/tasks/t1/attachments');
    expect(request.request.params.get('ownerType')).toBe('Comment');
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);
    request.flush(attachment({ id: 'd1', ownerType: 'Comment', ownerId: null, fileName: 'notes.pdf' }));
    await done;
    fixture.detectChanges();
    expect(picker.drafts().map((d) => d.id)).toEqual(['d1']);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('notes.pdf');
  });

  it('refreshes the task page files after a task upload', async () => {
    const fixture = TestBed.createComponent(AttachmentPicker);
    fixture.componentRef.setInput('taskId', 't1');
    fixture.detectChanges();
    backend.expectOne('/api/v1/attachment-rules').flush(rules);
    await fixture.whenStable();
    const store = TestBed.inject(TaskAttachmentsStore);
    const loading = store.load('t1');
    backend.expectOne('/api/v1/tasks/t1/attachments').flush([]);
    await loading;

    const done = fixture.componentInstance.uploadAll([new File(['%PDF-1.7'], 'spec.pdf')]);
    backend.expectOne((r) => r.url === '/api/v1/tasks/t1/attachments' && r.method === 'POST').flush(attachment());
    await Promise.resolve();
    await new Promise((resolve) => setTimeout(resolve));
    backend.expectOne((r) => r.url === '/api/v1/tasks/t1/attachments' && r.method === 'GET').flush([attachment()]);
    await done;
    expect(store.attachments().map((a) => a.id)).toEqual(['f1']);
    expect(fixture.componentInstance.drafts()).toEqual([]);
  });
});
