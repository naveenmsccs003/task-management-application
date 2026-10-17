import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { ProjectsApi } from '../projects/projects.api';
import { ProjectMember } from '../projects/projects.models';
import { TasksApi } from './tasks.api';
import { ApprovalRequest, Task } from './tasks.models';

/**
 * Reassignment and deadline-extension requests on one task (task-workflow.md §5–6): ask, and — for approvers — decide.
 * The server decides who may approve; the buttons follow its canDecide / canWithdraw flags.
 */
@Component({
  selector: 'app-task-requests',
  imports: [DatePipe, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Requests</h2>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    <ul class="requests">
      @for (r of requests(); track r.id) {
        <li>
          <div>
            <strong>{{ r.type === 'Reassignment' ? 'Reassign to ' + r.proposedAssignee?.name : 'Extend to ' + r.proposedDueDate }}</strong>
            <span class="status" [class]="r.status.toLowerCase()">{{ statusLabel(r) }}</span>
          </div>
          <div class="muted">“{{ r.reason }}” — {{ r.requestedBy?.name }}, {{ r.requestedAt | date: 'medium' }}</div>
          @if (r.decidedBy || r.decisionComment || r.approvedDueDate) {
            <div class="muted">
              {{ r.decidedBy ? 'Decided by ' + r.decidedBy.name : '' }}
              @if (r.approvedDueDate && r.approvedDueDate !== r.proposedDueDate) {
                · approved date {{ r.approvedDueDate }}
              }
              @if (r.decisionComment) {
                · “{{ r.decisionComment }}”
              }
            </div>
          }
          @if (r.canDecide) {
            <form class="decide" (ngSubmit)="decide(r, 'approve')" [attr.aria-label]="'Decide request ' + r.id">
              @if (r.type === 'DeadlineExtension') {
                <mat-form-field>
                  <mat-label>Approve with date</mat-label>
                  <input matInput type="date" name="approvedDate" [(ngModel)]="approvedDate" [placeholder]="r.proposedDueDate ?? ''" />
                  <mat-hint>Leave empty to approve {{ r.proposedDueDate }}</mat-hint>
                </mat-form-field>
              }
              <mat-form-field>
                <mat-label>Comment (required to reject)</mat-label>
                <input matInput name="decisionComment" [(ngModel)]="decisionComment" maxlength="1000" />
              </mat-form-field>
              <button mat-flat-button type="submit" [disabled]="busy()">Approve</button>
              <button mat-stroked-button type="button" (click)="decide(r, 'reject')" [disabled]="busy() || !decisionComment.trim()">Reject</button>
            </form>
          }
          @if (r.canWithdraw) {
            <button mat-button type="button" (click)="withdraw(r)" [disabled]="busy()">Withdraw</button>
          }
        </li>
      } @empty {
        <li class="muted">No requests.</li>
      }
    </ul>

    @if (can('requestReassignment')) {
      <form class="ask" (ngSubmit)="requestReassignment()" aria-label="Request reassignment">
        <h3>Request reassignment</h3>
        <mat-form-field>
          <mat-label>New assignee</mat-label>
          <mat-select name="newAssignee" [(ngModel)]="newAssigneeId" (openedChange)="loadMembers()">
            @for (m of candidates(); track m.userId) {
              <mat-option [value]="m.userId">{{ m.fullName }} ({{ m.projectRole.name }})</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field class="wide">
          <mat-label>Reason (at least 10 characters)</mat-label>
          <input matInput name="reassignReason" [(ngModel)]="reassignReason" maxlength="1000" />
        </mat-form-field>
        @if (task().algorithmRequired) {
          <mat-checkbox name="reassignReapproval" [(ngModel)]="reassignReapproval">The new assignee must get the algorithm approved again</mat-checkbox>
        }
        <button mat-stroked-button type="submit" [disabled]="busy() || !newAssigneeId || reassignReason.trim().length < 10">Request reassignment</button>
      </form>
    }

    @if (can('requestExtension')) {
      <form class="ask" (ngSubmit)="requestExtension()" aria-label="Request a deadline extension">
        <h3>Request a later due date</h3>
        <mat-form-field>
          <mat-label>New due date</mat-label>
          <input matInput type="date" name="proposedDate" [(ngModel)]="proposedDate" [min]="task().dueDate ?? ''" />
        </mat-form-field>
        <mat-form-field class="wide">
          <mat-label>Reason (at least 10 characters)</mat-label>
          <input matInput name="extensionReason" [(ngModel)]="extensionReason" maxlength="1000" />
        </mat-form-field>
        <button mat-stroked-button type="submit" [disabled]="busy() || !proposedDate || extensionReason.trim().length < 10">Request extension</button>
      </form>
    }
  `,
  styles: `
    .requests {
      list-style: none;
      padding: 0;
    }
    .requests li {
      padding: 8px 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .status {
      margin-left: 8px;
      padding: 0 8px;
      border-radius: 8px;
      background: var(--mat-sys-surface-variant);
      font: var(--mat-sys-label-small);
    }
    .status.pending {
      background: var(--mat-sys-tertiary-container);
    }
    .status.approved {
      background: var(--mat-sys-primary-container);
    }
    .status.rejected {
      background: var(--mat-sys-error-container);
    }
    .decide,
    .ask {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
      margin-top: 8px;
    }
    .ask h3 {
      width: 100%;
      margin: 8px 0 0;
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
export class TaskRequests {
  private readonly api = inject(TasksApi);
  private readonly projectsApi = inject(ProjectsApi);

  readonly task = input.required<Task>();
  /** Emitted after a decision that may have changed the task (assignee, due date). */
  readonly changed = output<void>();

  protected readonly requests = signal<ApprovalRequest[]>([]);
  protected readonly members = signal<ProjectMember[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly candidates = computed(() =>
    this.members().filter((m) => m.isActive && m.canWorkOnTasks && m.userId !== this.task().assignee?.id),
  );
  protected newAssigneeId = '';
  protected reassignReason = '';
  protected reassignReapproval = false;
  protected proposedDate = '';
  protected extensionReason = '';
  protected approvedDate = '';
  protected decisionComment = '';

  constructor() {
    effect(() => {
      const task = this.task();
      void this.run(async () => this.requests.set(await firstValueFrom(this.api.requests(task.id))));
    });
  }

  protected can(action: 'requestReassignment' | 'requestExtension'): boolean {
    return this.task().allowedActions.includes(action);
  }

  protected statusLabel(r: ApprovalRequest): string {
    return r.status === 'AutoCancelled' ? 'Cancelled automatically' : r.status;
  }

  protected async loadMembers(): Promise<void> {
    if (!this.members().length) {
      await this.run(async () => this.members.set(await firstValueFrom(this.projectsApi.members(this.task().projectId))));
    }
  }

  protected async requestReassignment(): Promise<void> {
    await this.save(() => this.api.requestReassignment(this.task().id, this.newAssigneeId, this.reassignReason, this.reassignReapproval));
    if (!this.failure()) {
      this.newAssigneeId = '';
      this.reassignReason = '';
      this.reassignReapproval = false;
    }
  }

  protected async requestExtension(): Promise<void> {
    await this.save(() => this.api.requestExtension(this.task().id, this.proposedDate, this.extensionReason));
    if (!this.failure()) {
      this.proposedDate = '';
      this.extensionReason = '';
    }
  }

  protected async decide(request: ApprovalRequest, decision: 'approve' | 'reject'): Promise<void> {
    await this.save(() => this.api.decideRequest(request.id, decision, this.decisionComment.trim() || null, decision === 'approve' ? this.approvedDate : null));
    if (!this.failure()) {
      this.decisionComment = '';
      this.approvedDate = '';
    }
  }

  protected async withdraw(request: ApprovalRequest): Promise<void> {
    await this.save(() => this.api.withdrawRequest(request.id));
  }

  private async save(call: () => ReturnType<TasksApi['withdrawRequest']>): Promise<void> {
    await this.run(async () => {
      this.requests.set(await firstValueFrom(call()));
      this.changed.emit();
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
