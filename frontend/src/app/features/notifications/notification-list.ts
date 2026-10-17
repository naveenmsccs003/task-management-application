import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { PagedResult } from '../../core/api/paged';
import { AppNotification } from '../../core/notifications/notifications.models';
import { NotificationsService } from '../../core/notifications/notifications.service';

/** The member's notifications, newest first (FR-NOT-01). Opening one marks it read and goes to its page. */
@Component({
  selector: 'app-notification-list',
  imports: [DatePipe, FormsModule, MatButtonModule, MatPaginatorModule, MatSlideToggleModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="header">
      <h1>Notifications</h1>
      <mat-slide-toggle [(ngModel)]="unreadOnly" (change)="load(1)">Unread only</mat-slide-toggle>
      <button mat-stroked-button type="button" (click)="markAll()" [disabled]="busy() || !notifications.unreadCount()">Mark all as read</button>
    </div>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    <ul class="list" aria-label="Notifications">
      @for (n of result().items; track n.id) {
        <li [class.unread]="!n.isRead">
          <button type="button" class="item" (click)="open(n)">
            <span class="title">{{ n.title }}</span>
            <span class="body">{{ n.body }}</span>
            <span class="meta">{{ n.createdAt | date: 'medium' }}@if (!n.isRead) { · unread }</span>
          </button>
        </li>
      } @empty {
        <li class="empty">{{ unreadOnly ? 'No unread notifications.' : 'No notifications yet.' }}</li>
      }
    </ul>
    <mat-paginator
      [length]="result().totalCount"
      [pageIndex]="result().page - 1"
      [pageSize]="result().pageSize"
      [pageSizeOptions]="[25, 50, 100]"
      (page)="changePage($event)"
      aria-label="Notification pages"
    />
  `,
  styles: `
    .header {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      align-items: center;
    }
    .header h1 {
      margin-right: auto;
    }
    .list {
      list-style: none;
      padding: 0;
    }
    .list li {
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .item {
      display: flex;
      flex-direction: column;
      gap: 2px;
      width: 100%;
      padding: 12px 8px;
      text-align: left;
      background: none;
      border: 0;
      cursor: pointer;
      font: inherit;
      color: inherit;
    }
    .item:hover,
    .item:focus-visible {
      background: var(--mat-sys-surface-container);
    }
    .unread .title {
      font-weight: 600;
    }
    .unread {
      border-left: 4px solid var(--mat-sys-primary);
    }
    .meta,
    .empty {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .empty {
      padding: 12px 0;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class NotificationList {
  protected readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);

  protected readonly result = signal<PagedResult<AppNotification>>({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected unreadOnly = false;

  constructor() {
    // Load now, and again whenever a notification is pushed while the page is open.
    effect(() => {
      this.notifications.lastPushed();
      untracked(() => void this.load(this.result().page));
    });
  }

  protected changePage(event: PageEvent): void {
    void this.load(event.pageIndex + 1, event.pageSize);
  }

  protected async open(notification: AppNotification): Promise<void> {
    if (!notification.isRead) {
      await this.notifications.markRead(notification.id).catch(() => undefined);
    }
    if (notification.link) {
      await this.router.navigateByUrl(notification.link);
    } else {
      await this.load(this.result().page);
    }
  }

  protected async markAll(): Promise<void> {
    this.busy.set(true);
    try {
      await this.notifications.markAllRead();
      await this.load(1);
    } finally {
      this.busy.set(false);
    }
  }

  protected async load(page: number, pageSize = this.result().pageSize): Promise<void> {
    this.failure.set(null);
    try {
      this.result.set(await firstValueFrom(this.notifications.list(this.unreadOnly, page, pageSize)));
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }
}
