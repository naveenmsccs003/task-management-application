import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { apiErrorInterceptor } from '../api/api-error.interceptor';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([apiErrorInterceptor, authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    auth.setMe({ id: '1', email: 'a@example.com', fullName: 'A', timeZone: 'UTC', globalRole: { id: 'r', name: 'Admin' }, permissions: [] });
  });

  afterEach(() => backend.verify());

  it('signs out locally and redirects when the session has ended', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const result = firstValueFrom(http.get('/api/v1/members'));

    backend.expectOne('/api/v1/members').flush({ status: 401, code: 'auth.required', title: 'Sign in' }, { status: 401, statusText: 'Unauthorized' });

    await expect(result).rejects.toMatchObject({ status: 401, code: 'auth.required' });
    expect(auth.isSignedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/sign-in'], expect.objectContaining({ queryParams: expect.objectContaining({ expired: 1 }) }));
  });

  it('leaves the session alone for 403 responses', async () => {
    const result = firstValueFrom(http.get('/api/v1/roles'));

    backend.expectOne('/api/v1/roles').flush({ status: 403, code: 'auth.forbidden', title: 'No' }, { status: 403, statusText: 'Forbidden' });

    await expect(result).rejects.toMatchObject({ status: 403 });
    expect(auth.isSignedIn()).toBe(true);
  });
});
