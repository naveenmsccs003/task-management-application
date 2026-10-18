import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../../core/api/api-error';

interface JobRun {
  jobName: string;
  lastStartedAt: string;
  lastFinishedAt: string;
  lastSucceeded: boolean;
  lastSucceededAt: string | null;
  lastMessage: string | null;
}

interface SystemStatus {
  serverTime: string;
  jobs: JobRun[];
  email: {
    pending: number;
    failed: number;
    sentLast24Hours: number;
    oldestPendingAt: string | null;
    recentFailures: { id: string; recipientEmail: string; subject: string; attempts: number; lastError: string | null; createdAt: string }[];
  };
}

const JOB_LABELS: Record<string, string> = {
  'session-monitor': 'Session monitor (every minute)',
  'email-delivery': 'Email delivery (every few seconds)',
  cleanup: 'Clean-up (hourly)',
  'due-date-monitor': 'Due dates and overdue (hourly)',
  'attachment-scan': 'Attachment malware scan (every 10 seconds)',
  'gitlab-sync': 'GitLab sync (every 15 minutes)',
};

/** Background jobs and the email queue for Admins (FR-ADM-01, FR-NOT-05). */
@Component({
  selector: 'app-system-status',
  imports: [DatePipe, MatButtonModule, MatCardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="header">
      <h1>System status</h1>
      <button mat-stroked-button type="button" (click)="load()">Refresh</button>
    </div>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (status(); as s) {
      <p class="muted">Server time {{ s.serverTime | date: 'medium' }}</p>
      <mat-card appearance="outlined">
        <mat-card-header><mat-card-title>Background jobs</mat-card-title></mat-card-header>
        <mat-card-content>
          <table aria-label="Background jobs">
            <thead><tr><th>Job</th><th>Last run</th><th>Result</th><th>Last success</th></tr></thead>
            <tbody>
              @for (j of s.jobs; track j.jobName) {
                <tr>
                  <td>{{ label(j.jobName) }}</td>
                  <td>{{ j.lastFinishedAt | date: 'medium' }}</td>
                  <td [class.bad]="!j.lastSucceeded">{{ j.lastSucceeded ? 'OK' : 'Failed' }} — {{ j.lastMessage }}</td>
                  <td>{{ j.lastSucceededAt ? (j.lastSucceededAt | date: 'medium') : 'never' }}</td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="muted">No job has run yet.</td></tr>
              }
            </tbody>
          </table>
        </mat-card-content>
      </mat-card>
      <mat-card appearance="outlined">
        <mat-card-header><mat-card-title>Email queue</mat-card-title></mat-card-header>
        <mat-card-content>
          <p>
            {{ s.email.pending }} waiting · {{ s.email.sentLast24Hours }} sent in the last 24 hours ·
            <span [class.bad]="s.email.failed > 0">{{ s.email.failed }} failed permanently</span>
            @if (s.email.oldestPendingAt) {
              · oldest waiting since {{ s.email.oldestPendingAt | date: 'medium' }}
            }
          </p>
          @if (s.email.recentFailures.length) {
            <table aria-label="Failed emails">
              <thead><tr><th>Created</th><th>To</th><th>Subject</th><th>Attempts</th><th>Last error</th></tr></thead>
              <tbody>
                @for (f of s.email.recentFailures; track f.id) {
                  <tr>
                    <td>{{ f.createdAt | date: 'short' }}</td>
                    <td>{{ f.recipientEmail }}</td>
                    <td>{{ f.subject }}</td>
                    <td>{{ f.attempts }}</td>
                    <td>{{ f.lastError }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    .header {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    mat-card {
      margin-bottom: 16px;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th,
    td {
      text-align: left;
      padding: 4px 8px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .bad,
    .error {
      color: var(--mat-sys-error);
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class SystemStatusPage implements OnInit {
  private readonly http = inject(HttpClient);

  protected readonly status = signal<SystemStatus | null>(null);
  protected readonly failure = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected label(jobName: string): string {
    return JOB_LABELS[jobName] ?? jobName;
  }

  protected async load(): Promise<void> {
    this.failure.set(null);
    try {
      this.status.set(await firstValueFrom(this.http.get<SystemStatus>('/api/v1/admin/system-status')));
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }
}
