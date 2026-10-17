import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { AppNotification } from '../../core/notifications/notifications.models';
import { NotificationsService } from '../../core/notifications/notifications.service';
import { NotificationList } from './notification-list';

const unread: AppNotification = {
  id: 'n1',
  type: 'TaskAssigned',
  category: 'Assignment',
  title: 'TMA-7 was assigned to you',
  body: '“Build login” in Task app is now yours.',
  link: '/tasks/t1',
  createdAt: '2026-10-17T09:00:00Z',
  isRead: false,
};

describe('NotificationList', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [NotificationList],
      providers: [provideRouter([]), provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('lists notifications and opening one marks it read and goes to its page', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(NotificationList);
    fixture.detectChanges();
    await fixture.whenStable();
    backend.expectOne((r) => r.url === '/api/v1/notifications').flush({ items: [unread], page: 1, pageSize: 25, totalCount: 1 });
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('li.unread')?.textContent).toContain('TMA-7 was assigned to you');

    element.querySelector<HTMLButtonElement>('button.item')!.click();
    backend.expectOne('/api/v1/notifications/n1/read').flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/tasks/t1');
  });

  it('keeps the unread count in step with reads', async () => {
    const service = TestBed.inject(NotificationsService);
    const refresh = service.refreshCount();
    backend.expectOne('/api/v1/notifications/unread-count').flush({ count: 3 });
    await refresh;
    expect(service.unreadCount()).toBe(3);

    const read = service.markRead('n1');
    backend.expectOne('/api/v1/notifications/n1/read').flush(null, { status: 204, statusText: 'No Content' });
    await read;
    expect(service.unreadCount()).toBe(2);

    const all = service.markAllRead();
    backend.expectOne('/api/v1/notifications/read-all').flush({ marked: 2 });
    await all;
    expect(service.unreadCount()).toBe(0);
  });
});
