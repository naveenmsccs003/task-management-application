import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { apiErrorInterceptor } from './api-error.interceptor';

describe('apiErrorInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('turns an HTTP error into an ApiError', async () => {
    const result = firstValueFrom(http.get('/api/v1/tasks/1'));

    backend
      .expectOne('/api/v1/tasks/1')
      .flush({ status: 404, title: 'Not found', code: 'task.not_found' }, { status: 404, statusText: 'Not Found' });

    await expect(result).rejects.toMatchObject({ status: 404, code: 'task.not_found', message: 'Not found' });
  });

  it('passes successful responses through', async () => {
    const result = firstValueFrom(http.get<{ ok: boolean }>('/api/v1/ping'));

    backend.expectOne('/api/v1/ping').flush({ ok: true });

    await expect(result).resolves.toEqual({ ok: true });
  });
});
