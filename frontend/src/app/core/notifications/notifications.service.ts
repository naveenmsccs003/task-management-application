import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import type { HubConnection } from '@microsoft/signalr';
import { Observable, firstValueFrom } from 'rxjs';
import { toParams } from '../api/http-params';
import { PagedResult } from '../api/paged';
import { AppNotification, NotificationCategory, NotificationPreference } from './notifications.models';

const POLL_INTERVAL_MS = 60_000;

/**
 * The member's notifications (FR-NOT-01) with live updates (FR-NOT-07): a SignalR connection pushes new notifications,
 * and the unread count is also polled every minute in case the connection is down.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly unread = signal(0);
  private readonly latest = signal<AppNotification | null>(null);
  private connection: HubConnection | undefined;
  private poll: ReturnType<typeof setInterval> | undefined;

  readonly unreadCount = this.unread.asReadonly();
  /** The most recently pushed notification, so pages can react (e.g. the list refreshes). */
  readonly lastPushed = this.latest.asReadonly();

  /** Starts live updates after sign-in. Safe to call more than once. */
  async connect(): Promise<void> {
    await this.refreshCount();
    this.poll ??= setInterval(() => void this.refreshCount(), POLL_INTERVAL_MS);
    if (this.connection) return;
    // Loaded on demand so the SignalR client is not part of the initial bundle (sign-in page).
    const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
    if (this.connection) return;
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection.on('notification', (notification: AppNotification) => {
      this.latest.set(notification);
      this.unread.update((n) => n + 1);
    });
    try {
      await this.connection.start();
    } catch {
      // Polling keeps the count current until the next reconnect attempt.
    }
  }

  /** Stops live updates on sign-out. */
  async disconnect(): Promise<void> {
    clearInterval(this.poll);
    this.poll = undefined;
    this.unread.set(0);
    const connection = this.connection;
    this.connection = undefined;
    if (connection && connection.state !== 'Disconnected') {
      await connection.stop().catch(() => undefined);
    }
  }

  async refreshCount(): Promise<void> {
    try {
      this.unread.set((await firstValueFrom(this.http.get<{ count: number }>('/api/v1/notifications/unread-count'))).count);
    } catch {
      // Keep the last known count; a 401 is handled by the auth interceptor.
    }
  }

  list(unreadOnly: boolean, page: number, pageSize: number): Observable<PagedResult<AppNotification>> {
    return this.http.get<PagedResult<AppNotification>>('/api/v1/notifications', { params: toParams({ unreadOnly, page, pageSize }) });
  }

  async markRead(id: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`/api/v1/notifications/${id}/read`, {}));
    this.unread.update((n) => Math.max(0, n - 1));
  }

  async markAllRead(): Promise<void> {
    await firstValueFrom(this.http.post('/api/v1/notifications/read-all', {}));
    this.unread.set(0);
  }

  preferences(): Observable<NotificationPreference[]> {
    return this.http.get<NotificationPreference[]>('/api/v1/me/notification-preferences');
  }

  savePreferences(preferences: { category: NotificationCategory; emailEnabled: boolean }[]): Observable<NotificationPreference[]> {
    return this.http.put<NotificationPreference[]>('/api/v1/me/notification-preferences', { preferences });
  }
}
