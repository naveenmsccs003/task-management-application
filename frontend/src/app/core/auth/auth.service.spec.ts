import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { apiErrorInterceptor } from '../api/api-error.interceptor';
import { Me } from './auth.models';
import { AuthService } from './auth.service';

const me: Me = {
  id: '1',
  email: 'dev@example.com',
  fullName: 'Dev',
  timeZone: 'UTC',
  globalRole: { id: 'r', name: 'Developer' },
  permissions: [
    { code: 'profile.manage', scope: 'Own' },
    { code: 'tasks.view', scope: 'Project' },
  ],
};

describe('AuthService', () => {
  let auth: AuthService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    auth = TestBed.inject(AuthService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  /** Waits for the service to send the next request (it awaits between calls), then answers it. */
  async function flushNext(url: string, body: object, status = 200): Promise<void> {
    const request = await vi.waitFor(() => backend.expectOne(url));
    request.flush(body, { status, statusText: String(status) });
  }

  it('starts unknown, then loads the current session', async () => {
    expect(auth.me()).toBeUndefined();
    const done = auth.init();
    await flushNext('/api/v1/auth/antiforgery', { requestToken: 't' });
    await flushNext('/api/v1/me', me);
    await done;

    expect(auth.isSignedIn()).toBe(true);
    expect(auth.me()?.email).toBe('dev@example.com');
  });

  it('treats a 401 from /me as signed out', async () => {
    const done = auth.init();
    await flushNext('/api/v1/auth/antiforgery', { requestToken: 't' });
    await flushNext('/api/v1/me', { status: 401, title: 'Please sign in', code: 'auth.required' }, 401);
    await done;

    expect(auth.me()).toBeNull();
    expect(auth.isSignedIn()).toBe(false);
  });

  it('refreshes the antiforgery token after signing in', async () => {
    const done = auth.signIn('dev@example.com', 'pw');
    const login = backend.expectOne('/api/v1/auth/login');
    expect(login.request.body).toEqual({ email: 'dev@example.com', password: 'pw' });
    login.flush(me);
    await flushNext('/api/v1/auth/antiforgery', { requestToken: 't2' });
    await done;

    expect(auth.isSignedIn()).toBe(true);
  });

  it('compares permission scopes: Global > Project > Own', () => {
    auth.setMe(me);

    expect(auth.hasPermission('profile.manage', 'Own')).toBe(true);
    expect(auth.hasPermission('profile.manage', 'Global')).toBe(false);
    expect(auth.hasPermission('tasks.view', 'Own')).toBe(true);
    expect(auth.hasPermission('tasks.view', 'Project')).toBe(true);
    expect(auth.hasPermission('tasks.view')).toBe(false);
    expect(auth.hasPermission('members.create')).toBe(false);
  });
});
