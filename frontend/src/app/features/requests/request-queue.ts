import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { TasksApi } from '../tasks/tasks.api';
import { ApprovalRequest } from '../tasks/tasks.models';

/** The member's approval queue: reassignment and extension requests they may decide (docs/api-design.md §4.8). */
@Component({
  selector: 'app-request-queue',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Approvals</h1>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    <ul class="queue" aria-label="Requests waiting for your decision">
      @for (r of requests(); track r.id) {
        <li>
          <div>
            <a [routerLink]="['/tasks', r.taskId]">{{ r.taskKey }} · {{ r.taskTitle }}</a>
          </div>
          <div>
            <strong>{{ r.type === 'Reassignment' ? 'Reassign to ' + r.proposedAssignee?.name : 'Extend from ' + r.currentDueDate + ' to ' + r.proposedDueDate }}</strong>
          </div>
          <div class="muted">“{{ r.reason }}” — {{ r.requestedBy?.name }}, {{ r.requestedAt | date: 'medium' }}</div>
          <form class="decide" (ngSubmit)="decide(r, 'approve')" [attr.aria-label]="'Decide ' + r.taskKey">
            <mat-form-field>
              <mat-label>Comment (required to reject)</mat-label>
              <input matInput [name]="'comment-' + r.id" [(ngModel)]="comments[r.id]" maxlength="1000" />
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="busy()">Approve</button>
            <button mat-stroked-button type="button" (click)="decide(r, 'reject')" [disabled]="busy() || !comments[r.id]?.trim()">Reject</button>
          </form>
        </li>
      } @empty {
        <li class="muted">Nothing is waiting for your decision.</li>
      }
    </ul>
  `,
  styles: `
    .queue {
      list-style: none;
      padding: 0;
    }
    .queue li {
      padding: 12px 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .decide {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class RequestQueue implements OnInit {
  private readonly api = inject(TasksApi);

  protected readonly requests = signal<ApprovalRequest[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly comments: Record<string, string> = {};

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async decide(request: ApprovalRequest, decision: 'approve' | 'reject'): Promise<void> {
    this.busy.set(true);
    this.failure.set(null);
    try {
      await firstValueFrom(this.api.decideRequest(request.id, decision, this.comments[request.id]?.trim() || null));
      await this.load();
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.requests.set(await firstValueFrom(this.api.requestQueue()));
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }
}
