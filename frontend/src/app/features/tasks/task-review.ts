import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { TasksApi } from './tasks.api';
import { ChecklistItem, ReviewSubmission, Task } from './tasks.models';

/** Submit for review, TL review, closure and reopening (FR-RVW-01..05), with the review history. */
@Component({
  selector: 'app-task-review',
  imports: [DatePipe, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Review</h2>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    @if (can('submitReview')) {
      <form (ngSubmit)="submit()" aria-label="Submit for review">
        <mat-form-field class="full">
          <mat-label>Summary of the work</mat-label>
          <textarea matInput name="summary" [(ngModel)]="summary" rows="3" maxlength="10000"></textarea>
          <mat-hint>All required checklist items must be ticked. Submitting ends your work session.</mat-hint>
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy() || !summary.trim()">Submit for review</button>
      </form>
    }

    @if (can('review')) {
      <form (ngSubmit)="approve()" aria-label="Review the work">
        <mat-form-field class="full">
          <mat-label>Review comment (required to request changes)</mat-label>
          <textarea matInput name="reviewComment" [(ngModel)]="comment" rows="3" maxlength="2000"></textarea>
        </mat-form-field>
        @if (checklist().length) {
          <fieldset>
            <legend>Checklist items to reopen</legend>
            @for (item of checklist(); track item.id) {
              <mat-checkbox [checked]="reopen.has(item.id)" (change)="toggleReopen(item.id, $event.checked)">{{ item.text }}</mat-checkbox>
            }
          </fieldset>
        }
        @if (task().algorithmRequired) {
          <mat-checkbox name="reapproval" [(ngModel)]="reapproval">The approach changes: the algorithm must be approved again</mat-checkbox>
        }
        <div class="buttons">
          <button mat-flat-button type="submit" [disabled]="busy()">Approve</button>
          <button mat-stroked-button type="button" (click)="requestRevision()" [disabled]="busy() || !comment.trim()">Request changes</button>
        </div>
      </form>
    }

    @if (can('close')) {
      <div class="buttons">
        <button mat-flat-button type="button" (click)="close()" [disabled]="busy()">Close task</button>
        <span class="muted">Revision {{ task().approvedRevision }} was approved.</span>
      </div>
    }

    @if (can('reopen')) {
      <form (ngSubmit)="reopenTask()" aria-label="Reopen the task">
        <mat-form-field class="full">
          <mat-label>Why reopen it?</mat-label>
          <input matInput name="reopenReason" [(ngModel)]="reopenReason" maxlength="1000" />
          <mat-hint>Reopening starts revision {{ task().currentRevision + 1 }}.</mat-hint>
        </mat-form-field>
        <button mat-stroked-button type="submit" [disabled]="busy() || !reopenReason.trim()">Reopen task</button>
      </form>
    }

    <ol class="reviews" reversed>
      @for (r of reviews(); track r.id) {
        <li>
          <strong>Revision {{ r.revision }}</strong> submitted by {{ r.submittedBy?.name }} · {{ r.submittedAt | date: 'medium' }}
          <p class="summary">{{ r.summary }}</p>
          @if (r.decision !== 'Pending') {
            <p [class.changes]="r.decision === 'RevisionRequested'">
              {{ r.decision === 'Approved' ? 'Approved' : 'Changes requested' }} by {{ r.reviewedBy?.name }} · {{ r.reviewedAt | date: 'medium' }}
              @if (r.comment) {
                — “{{ r.comment }}”
              }
              @if (r.algorithmReapprovalRequired) {
                (new algorithm required)
              }
            </p>
          } @else {
            <p class="muted">Waiting for review.</p>
          }
        </li>
      } @empty {
        <li class="muted">Not submitted for review yet.</li>
      }
    </ol>
  `,
  styles: `
    .full {
      width: 100%;
    }
    fieldset {
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 8px;
      margin: 8px 0;
      display: flex;
      flex-direction: column;
    }
    .buttons {
      display: flex;
      gap: 12px;
      align-items: center;
      flex-wrap: wrap;
      margin: 8px 0;
    }
    .reviews {
      padding-left: 20px;
    }
    .summary {
      white-space: pre-wrap;
      margin: 4px 0;
    }
    .changes,
    .error {
      color: var(--mat-sys-error);
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class TaskReview {
  private readonly api = inject(TasksApi);

  readonly task = input.required<Task>();
  readonly changed = output<Task>();

  protected readonly reviews = signal<ReviewSubmission[]>([]);
  protected readonly checklist = signal<ChecklistItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly reopen = new Set<string>();
  protected summary = '';
  protected comment = '';
  protected reopenReason = '';
  protected reapproval = false;

  constructor() {
    effect(() => {
      const task = this.task();
      void this.run(async () => {
        this.reviews.set(await firstValueFrom(this.api.reviews(task.id)));
        if (task.allowedActions.includes('review')) {
          this.checklist.set(await firstValueFrom(this.api.checklist(task.id)));
        }
      });
    });
  }

  protected can(action: 'submitReview' | 'review' | 'close' | 'reopen'): boolean {
    return this.task().allowedActions.includes(action);
  }

  protected toggleReopen(id: string, checked: boolean): void {
    if (checked) this.reopen.add(id);
    else this.reopen.delete(id);
  }

  protected async submit(): Promise<void> {
    await this.act(() => this.api.submitReview(this.task().id, this.summary));
    if (!this.failure()) this.summary = '';
  }

  protected async approve(): Promise<void> {
    await this.act(() => this.api.approveReview(this.task().id, this.comment.trim() || null));
  }

  protected async requestRevision(): Promise<void> {
    await this.act(() => this.api.requestRevision(this.task().id, this.comment, [...this.reopen], this.reapproval));
    if (!this.failure()) {
      this.comment = '';
      this.reopen.clear();
      this.reapproval = false;
    }
  }

  protected async close(): Promise<void> {
    await this.act(() => this.api.close(this.task().id));
  }

  protected async reopenTask(): Promise<void> {
    await this.act(() => this.api.reopen(this.task().id, this.reopenReason));
    if (!this.failure()) this.reopenReason = '';
  }

  private async act(call: () => ReturnType<TasksApi['close']>): Promise<void> {
    await this.run(async () => this.changed.emit(await firstValueFrom(call())));
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
