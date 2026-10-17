import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { TasksApi } from './tasks.api';
import { formatAllocation, formatDuration, SessionEndReason, Task, TaskTime } from './tasks.models';

const END_REASONS: Record<SessionEndReason, string> = {
  Paused: 'Paused',
  Stopped: 'Stopped',
  Submitted: 'Submitted',
  AutoClosed: 'Auto-closed',
  Reassigned: 'Reassigned',
  Cancelled: 'Cancelled',
  MemberDeactivated: 'Member deactivated',
};

/** Time spent against the allocation, the work sessions, and adjustments by TL/PM (FR-TIME-04/05/08). */
@Component({
  selector: 'app-task-time',
  imports: [DatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Time</h2>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (time(); as t) {
      <p data-testid="time-summary">
        <strong>{{ duration(t.spentSeconds) }}</strong> spent of {{ allocation(t.allocatedMinutes) }}
        @if (t.percentOfAllocation !== null) {
          ({{ t.percentOfAllocation }}%)
        }
        @if (t.overrunSeconds > 0) {
          <span class="overrun">· {{ duration(t.overrunSeconds) }} over the allocation</span>
        }
      </p>
      @if (t.byRevision.length > 1) {
        <p class="muted">
          @for (r of t.byRevision; track r.revision) {
            <span>Revision {{ r.revision }}: {{ duration(r.seconds) }} </span>
          }
        </p>
      }
      <table class="sessions" aria-label="Work sessions">
        <thead>
          <tr><th>Who</th><th>Started</th><th>Ended</th><th>Duration</th><th>Why it ended</th></tr>
        </thead>
        <tbody>
          @for (s of t.sessions; track s.id) {
            <tr>
              <td>{{ s.user?.name }}</td>
              <td>{{ s.startedAt | date: 'short' }}</td>
              <td>{{ s.endedAt ? (s.endedAt | date: 'short') : 'Running' }}</td>
              <td>{{ duration(s.seconds) }}</td>
              <td>{{ s.endReason ? endReasons[s.endReason] : '—' }}</td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="muted">No work sessions yet.</td></tr>
          }
        </tbody>
      </table>
      @if (t.adjustments.length) {
        <h3>Adjustments</h3>
        <ul>
          @for (a of t.adjustments; track a.id) {
            <li>
              {{ a.minutes > 0 ? '+' : '' }}{{ a.minutes }} min for {{ a.user?.name }} — “{{ a.reason }}”
              <span class="muted">by {{ a.createdBy?.name }}, {{ a.createdAt | date: 'short' }}</span>
            </li>
          }
        </ul>
      }
      @if (canAdjust()) {
        <form class="adjust" (ngSubmit)="adjust()" aria-label="Adjust time">
          <mat-form-field>
            <mat-label>Member</mat-label>
            <mat-select name="member" [(ngModel)]="adjustUserId" required>
              @for (m of people(); track m.id) {
                <mat-option [value]="m.id">{{ m.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Minutes (+/−)</mat-label>
            <input matInput type="number" name="minutes" [(ngModel)]="adjustMinutes" required />
          </mat-form-field>
          <mat-form-field>
            <mat-label>Reason</mat-label>
            <input matInput name="reason" [(ngModel)]="adjustReason" required maxlength="1000" />
          </mat-form-field>
          <button mat-stroked-button type="submit" [disabled]="busy() || !adjustUserId || !adjustMinutes || !adjustReason.trim()">
            Add adjustment
          </button>
        </form>
      }
    }
  `,
  styles: `
    .sessions {
      width: 100%;
      border-collapse: collapse;
    }
    .sessions th,
    .sessions td {
      text-align: left;
      padding: 4px 8px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .overrun {
      color: var(--mat-sys-error);
    }
    .adjust {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
      margin-top: 12px;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class TaskTimePanel {
  private readonly api = inject(TasksApi);

  readonly task = input.required<Task>();

  protected readonly endReasons = END_REASONS;
  protected readonly time = signal<TaskTime | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly canAdjust = computed(() => this.task().allowedActions.includes('adjustTime'));
  /** People whose time can be adjusted: the assignee and anyone with sessions. */
  protected readonly people = computed(() => {
    const byId = new Map<string, string>();
    const assignee = this.task().assignee;
    if (assignee) byId.set(assignee.id, assignee.name);
    for (const s of this.time()?.sessions ?? []) {
      if (s.user) byId.set(s.user.id, s.user.name);
    }
    return [...byId].map(([id, name]) => ({ id, name }));
  });
  protected adjustUserId = '';
  protected adjustMinutes: number | null = null;
  protected adjustReason = '';

  constructor() {
    // Reload whenever the task changes (e.g. after start/pause/stop), so totals and sessions stay current.
    effect(() => {
      const task = this.task();
      void this.run(async () => this.time.set(await firstValueFrom(this.api.time(task.id))));
    });
  }

  protected duration(seconds: number): string {
    return formatDuration(seconds);
  }

  protected allocation(minutes: number | null): string {
    return formatAllocation(minutes);
  }

  protected async adjust(): Promise<void> {
    await this.run(async () => {
      this.time.set(await firstValueFrom(this.api.adjustTime(this.task().id, this.adjustUserId, this.adjustMinutes ?? 0, this.adjustReason)));
      this.adjustMinutes = null;
      this.adjustReason = '';
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.failure.set(null);
    try {
      await action();
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
