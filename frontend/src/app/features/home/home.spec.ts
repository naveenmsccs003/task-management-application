import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { apiErrorInterceptor } from '../../core/api/api-error.interceptor';
import { SYSTEM_INFO_URL } from '../../core/system/system-info';
import { Home } from './home';

describe('Home', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  // Do not await stability here: the pending HTTP request keeps the app unstable until the test answers it.
  function render() {
    const fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the API version when the API responds', async () => {
    const fixture = render();

    backend
      .expectOne(SYSTEM_INFO_URL)
      .flush({ name: 'Task Management', version: '1.2.3', serverTimeUtc: '2026-10-17T07:00:00Z' });
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Connected');
    expect(element.querySelector('[data-testid="api-version"]')?.textContent).toBe('1.2.3');
  });

  it('shows the error message and trace reference when the API fails', async () => {
    const fixture = render();

    backend
      .expectOne(SYSTEM_INFO_URL)
      .flush(
        { status: 500, title: 'An unexpected error occurred.', code: 'internal_error', traceId: 'trace-123' },
        { status: 500, statusText: 'Server Error' },
      );
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('An unexpected error occurred.');
    expect(element.textContent).toContain('trace-123');
  });
});
