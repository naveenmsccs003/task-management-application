import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** GET /api/v1/me/active-session */
export interface ActiveSession {
  sessionId: string;
  taskId: string;
  taskKey: string;
  taskTitle: string;
  startedAt: string;
  serverTime: string;
}

/**
 * The member's running work session, for the timer bar. Elapsed time is measured from the server's clock (the offset to
 * the browser clock is captured when the session is loaded), so a wrong computer clock does not distort the timer.
 */
@Injectable({ providedIn: 'root' })
export class ActiveSessionService {
  private readonly http = inject(HttpClient);
  private readonly state = signal<{ session: ActiveSession; offsetMs: number } | null>(null);
  private readonly now = signal(Date.now());
  private timer: ReturnType<typeof setInterval> | undefined;

  readonly session = computed(() => this.state()?.session ?? null);

  /** Seconds since the session started, updated every second while a session runs. */
  readonly elapsedSeconds = computed(() => {
    const state = this.state();
    if (!state) return 0;
    const serverNow = this.now() + state.offsetMs;
    return Math.max(0, Math.floor((serverNow - Date.parse(state.session.startedAt)) / 1000));
  });

  async refresh(): Promise<void> {
    try {
      const response: HttpResponse<ActiveSession | null> = await firstValueFrom(
        this.http.get<ActiveSession | null>('/api/v1/me/active-session', { observe: 'response' }),
      );
      const session = response.status === 200 ? response.body : null;
      this.state.set(session ? { session, offsetMs: Date.parse(session.serverTime) - Date.now() } : null);
    } catch {
      this.state.set(null);
    }
    this.syncTimer();
  }

  clear(): void {
    this.state.set(null);
    this.syncTimer();
  }

  private syncTimer(): void {
    if (this.state() && !this.timer) {
      this.timer = setInterval(() => this.now.set(Date.now()), 1000);
    } else if (!this.state() && this.timer) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
  }
}
