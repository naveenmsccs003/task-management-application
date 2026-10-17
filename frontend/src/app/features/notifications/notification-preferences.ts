import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { NOTIFICATION_CATEGORY_LABELS, NotificationPreference } from '../../core/notifications/notifications.models';
import { NotificationsService } from '../../core/notifications/notifications.service';

/** Email on/off per notification category (FR-NOT-02). In-app notifications are always kept; account emails are mandatory. */
@Component({
  selector: 'app-notification-preferences',
  imports: [MatButtonModule, MatCardModule, MatSlideToggleModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card appearance="outlined">
      <mat-card-header><mat-card-title>Email notifications</mat-card-title></mat-card-header>
      <mat-card-content>
        <p class="muted">You always see notifications in the app. Choose which ones are also emailed to you.</p>
        @for (p of preferences(); track p.category) {
          <div class="row">
            <mat-slide-toggle [checked]="p.emailEnabled" [disabled]="p.mandatory || busy()" (change)="toggle(p, $event.checked)">
              {{ labels[p.category] }}
            </mat-slide-toggle>
            @if (p.mandatory) {
              <span class="muted">always sent</span>
            }
          </div>
        }
        @if (failure(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .row {
      display: flex;
      gap: 12px;
      align-items: center;
      margin: 8px 0;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class NotificationPreferences implements OnInit {
  private readonly notifications = inject(NotificationsService);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly labels = NOTIFICATION_CATEGORY_LABELS;
  protected readonly preferences = signal<NotificationPreference[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.preferences.set(await firstValueFrom(this.notifications.preferences()));
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }

  protected async toggle(preference: NotificationPreference, emailEnabled: boolean): Promise<void> {
    this.busy.set(true);
    this.failure.set(null);
    try {
      this.preferences.set(await firstValueFrom(this.notifications.savePreferences([{ category: preference.category, emailEnabled }])));
      this.snackBar.open('Email preferences saved.', undefined, { duration: 2500 });
    } catch (error) {
      this.failure.set(asApiError(error).message);
      this.preferences.set(await firstValueFrom(this.notifications.preferences()));
    } finally {
      this.busy.set(false);
    }
  }
}
