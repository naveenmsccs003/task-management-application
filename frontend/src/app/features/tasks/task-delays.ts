import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { TasksApi } from './tasks.api';
import { DELAY_CATEGORY_LABELS, DelayCategory, DelayReport, Task } from './tasks.models';

/** Delay reports (FR-DL-06): the assignee explains a delay; the due date does not change. */
@Component({
  selector: 'app-task-delays',
  imports: [DatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Delays</h2>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (task().canViewDelays) {
      <ul class="reports">
        @for (d of reports(); track d.id) {
          <li>
            <strong>{{ labels[d.category] }}</strong> — {{ d.description }}
            <div class="muted">{{ d.reportedBy?.name }}, {{ d.reportedAt | date: 'medium' }} (revision {{ d.revision }})</div>
          </li>
        } @empty {
          <li class="muted">No delays reported.</li>
        }
      </ul>
    }
    @if (task().allowedActions.includes('reportDelay')) {
      <form class="report" (ngSubmit)="report()" aria-label="Report a delay">
        <mat-form-field>
          <mat-label>Cause</mat-label>
          <mat-select name="category" [(ngModel)]="category">
            @for (c of categories; track c) {
              <mat-option [value]="c">{{ labels[c] }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field class="wide">
          <mat-label>What is delaying the task?</mat-label>
          <input matInput name="description" [(ngModel)]="description" maxlength="2000" />
        </mat-form-field>
        <button mat-stroked-button type="submit" [disabled]="busy() || !category || !description.trim()">Report delay</button>
      </form>
    }
  `,
  styles: `
    .reports {
      list-style: none;
      padding: 0;
    }
    .reports li {
      margin-bottom: 8px;
    }
    .report {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    .wide {
      flex: 1;
      min-width: 240px;
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
export class TaskDelays {
  private readonly api = inject(TasksApi);

  readonly task = input.required<Task>();

  protected readonly labels = DELAY_CATEGORY_LABELS;
  protected readonly categories = Object.keys(DELAY_CATEGORY_LABELS) as DelayCategory[];
  protected readonly reports = signal<DelayReport[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected category: DelayCategory | '' = '';
  protected description = '';

  constructor() {
    effect(() => {
      const task = this.task();
      if (task.canViewDelays) {
        void this.run(async () => this.reports.set(await firstValueFrom(this.api.delays(task.id))));
      }
    });
  }

  protected async report(): Promise<void> {
    await this.run(async () => {
      const reports = await firstValueFrom(this.api.reportDelay(this.task().id, this.category as DelayCategory, this.description));
      if (this.task().canViewDelays) this.reports.set(reports);
      this.category = '';
      this.description = '';
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
