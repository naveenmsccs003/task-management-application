import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { TasksApi } from './tasks.api';
import { AlgorithmSubmission, Task } from './tasks.models';

/** Algorithm versions with submit (assignee) and approve/reject (Team Leader) (FR-ALG-01..05). */
@Component({
  selector: 'app-task-algorithm',
  imports: [DatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Algorithm</h2>
    <p class="muted">
      @if (task().algorithmApproved) {
        Approved — work can start.
      } @else if (task().status === 'AlgorithmSubmitted') {
        Waiting for a Team Leader's decision.
      } @else {
        Required before work can start.
      }
    </p>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    @if (canSubmit()) {
      <form (ngSubmit)="submit()" aria-label="Submit algorithm">
        <mat-form-field class="full">
          <mat-label>Your algorithm</mat-label>
          <textarea matInput name="content" [(ngModel)]="content" rows="6" maxlength="50000"></textarea>
          <mat-hint>Describe the approach step by step. Submitted versions cannot be edited.</mat-hint>
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy() || !content.trim()">Submit algorithm</button>
      </form>
    }

    <ol class="versions" reversed>
      @for (s of submissions(); track s.id) {
        <li>
          <div class="meta">
            <strong>Version {{ s.version }}</strong>
            <span>by {{ s.submittedBy?.name }} · {{ s.submittedAt | date: 'medium' }}</span>
            <span class="decision" [class]="s.decision.toLowerCase()">{{ s.decision }}</span>
          </div>
          <pre class="content">{{ s.content }}</pre>
          @if (s.decidedBy) {
            <p class="muted">{{ s.decision }} by {{ s.decidedBy.name }} · {{ s.decidedAt | date: 'medium' }}</p>
          }
          @if (s.decisionComment) {
            <p class="comment">“{{ s.decisionComment }}”</p>
          }
          @if (s.decision === 'Pending' && canReview()) {
            <form (ngSubmit)="decide(s, 'approve')" aria-label="Review the algorithm">
              <mat-form-field class="full">
                <mat-label>Comment (required to reject)</mat-label>
                <textarea matInput name="decisionComment" [(ngModel)]="comment" rows="2" maxlength="2000"></textarea>
              </mat-form-field>
              <button mat-flat-button type="submit" [disabled]="busy()">Approve</button>
              <button mat-stroked-button type="button" (click)="decide(s, 'reject')" [disabled]="busy() || !comment.trim()">Reject</button>
            </form>
          }
        </li>
      } @empty {
        <li class="muted">No algorithm submitted yet.</li>
      }
    </ol>
  `,
  styles: `
    .full {
      width: 100%;
    }
    .versions {
      padding-left: 20px;
    }
    .versions li {
      margin-bottom: 16px;
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: center;
    }
    .content {
      white-space: pre-wrap;
      font-family: inherit;
      background: var(--mat-sys-surface-container);
      padding: 8px;
      border-radius: 4px;
    }
    .decision {
      padding: 0 8px;
      border-radius: 8px;
      background: var(--mat-sys-surface-variant);
    }
    .decision.approved {
      background: var(--mat-sys-primary-container);
    }
    .decision.rejected {
      background: var(--mat-sys-error-container);
    }
    .muted,
    .comment {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class TaskAlgorithm implements OnInit {
  private readonly api = inject(TasksApi);

  readonly task = input.required<Task>();
  /** The task after a submit or decision, so the page can update its state and buttons. */
  readonly changed = output<Task>();

  protected readonly submissions = signal<AlgorithmSubmission[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly canSubmit = computed(() => this.task().allowedActions.includes('submitAlgorithm'));
  protected readonly canReview = computed(() => this.task().allowedActions.includes('reviewAlgorithm'));
  protected content = '';
  protected comment = '';

  async ngOnInit(): Promise<void> {
    await this.run(async () => this.submissions.set(await firstValueFrom(this.api.algorithms(this.task().id))));
  }

  protected async submit(): Promise<void> {
    await this.run(async () => {
      this.changed.emit(await firstValueFrom(this.api.submitAlgorithm(this.task().id, this.content)));
      this.content = '';
      this.submissions.set(await firstValueFrom(this.api.algorithms(this.task().id)));
    });
  }

  protected async decide(submission: AlgorithmSubmission, decision: 'approve' | 'reject'): Promise<void> {
    await this.run(async () => {
      this.changed.emit(await firstValueFrom(this.api.decideAlgorithm(this.task().id, submission.id, decision, this.comment.trim() || null)));
      this.comment = '';
      this.submissions.set(await firstValueFrom(this.api.algorithms(this.task().id)));
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
