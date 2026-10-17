import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActiveSessionService } from './active-session.service';

describe('ActiveSessionService', () => {
  let service: ActiveSessionService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ActiveSessionService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    service.clear();
    backend.verify();
  });

  it('measures elapsed time from the server clock, not the browser clock', async () => {
    // The browser clock is 1 hour behind the server: elapsed time must still be 90 seconds.
    const browserNow = Date.now();
    const serverNow = browserNow + 3_600_000;
    const pending = service.refresh();
    backend.expectOne('/api/v1/me/active-session').flush({
      sessionId: 's1',
      taskId: 't1',
      taskKey: 'TMA-7',
      taskTitle: 'Build login',
      startedAt: new Date(serverNow - 90_000).toISOString(),
      serverTime: new Date(serverNow).toISOString(),
    });
    await pending;

    expect(service.session()?.taskKey).toBe('TMA-7');
    expect(service.elapsedSeconds()).toBeGreaterThanOrEqual(89);
    expect(service.elapsedSeconds()).toBeLessThanOrEqual(91);
  });

  it('shows no session when the server answers 204', async () => {
    const pending = service.refresh();
    backend.expectOne('/api/v1/me/active-session').flush(null, { status: 204, statusText: 'No Content' });
    await pending;

    expect(service.session()).toBeNull();
    expect(service.elapsedSeconds()).toBe(0);
  });
});
