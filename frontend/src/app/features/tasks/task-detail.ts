import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { ActiveSessionService } from '../../core/work/active-session.service';
import { TaskAttachments } from '../attachments/task-attachments';
import { TaskAttachmentsStore } from '../attachments/task-attachments.store';
import { GitLabApi } from '../gitlab/gitlab.api';
import { ProjectsApi } from '../projects/projects.api';
import { ProjectMember } from '../projects/projects.models';
import { TaskAlgorithm } from './task-algorithm';
import { TaskChecklist } from './task-checklist';
import { TaskComments } from './task-comments';
import { TaskDelays } from './task-delays';
import { TaskRequests } from './task-requests';
import { TaskReview } from './task-review';
import { TaskTimePanel } from './task-time';
import { TasksApi, WorkAction } from './tasks.api';
import { describeHistory, formatAllocation, formatDuration, Task, TASK_STATUS_LABELS, TaskActionName, TaskHistoryEntry } from './tasks.models';

type Panel = 'assign' | 'cancel' | 'delete' | 'pause' | 'stop' | null;

/** One task: its fields, the actions the server allows for the caller, comments and history. */
@Component({
  selector: 'app-task-detail',
  imports: [
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, TaskAlgorithm, TaskAttachments, TaskChecklist, TaskComments, TaskDelays, TaskRequests, TaskReview, TaskTimePanel,
  ],
  providers: [TaskAttachmentsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    @if (task(); as t) {
      <a [routerLink]="['/projects', t.projectId]">← {{ t.projectKey }} · {{ t.projectName }}</a>
      <div class="header">
        <h1>{{ t.key }} · {{ t.title }}</h1>
        <span class="status" data-testid="task-status">{{ statusLabels[t.status] }}</span>
        @if (t.flags.overdue) {
          <span class="flag">Overdue</span>
        }
        @if (t.flags.extensionPending) {
          <span class="flag info">Extension pending</span>
        }
        @if (t.flags.reassignmentPending) {
          <span class="flag info">Reassignment pending</span>
        }
        @if (t.flags.gitLabIssueClosed) {
          <span class="flag info">GitLab issue closed</span>
        }
        @if (t.gitLab?.state === 'Missing') {
          <span class="flag">GitLab issue missing</span>
        }
      </div>
      @if (t.projectReadOnly) {
        <p class="notice">The project is completed or archived, so this task is read-only.</p>
      }

      @if (hasWorkActions()) {
        <div class="actions work" aria-label="Work">
          @if (can('start')) {
            <button mat-flat-button type="button" (click)="work('start')" [disabled]="busy()">Start work</button>
          }
          @if (can('resume')) {
            <button mat-flat-button type="button" (click)="work('resume')" [disabled]="busy()">Resume</button>
          }
          @if (can('pause')) {
            <button mat-stroked-button type="button" (click)="openPanel('pause')" [disabled]="busy()">Pause</button>
          }
          @if (can('stop')) {
            <button mat-stroked-button type="button" (click)="openPanel('stop')" [disabled]="busy()">Stop</button>
          }
        </div>
      }

      <div class="actions">
        @if (can('update')) {
          <a mat-stroked-button [routerLink]="['/tasks', t.id, 'edit']">Edit</a>
        }
        @if (can('assign')) {
          <button mat-flat-button type="button" (click)="openPanel('assign')">Assign</button>
        }
        @if (can('cancel')) {
          <button mat-stroked-button type="button" (click)="openPanel('cancel')">Cancel task</button>
        }
        @if (can('delete')) {
          <button mat-stroked-button type="button" (click)="openPanel('delete')">Delete draft</button>
        }
      </div>

      @switch (panel()) {
        @case ('assign') {
          <mat-card appearance="outlined" class="panel">
            <mat-card-content>
              <form (ngSubmit)="assign()" aria-label="Assign the task">
                <mat-form-field>
                  <mat-label>Assignee</mat-label>
                  <mat-select name="assignee" [(ngModel)]="assigneeId" required>
                    @for (m of assignable(); track m.userId) {
                      <mat-option [value]="m.userId">{{ m.fullName }} ({{ m.projectRole.name }})</mat-option>
                    } @empty {
                      <mat-option disabled>No project members can work on tasks</mat-option>
                    }
                  </mat-select>
                </mat-form-field>
                @if (!t.dueDate) {
                  <mat-form-field>
                    <mat-label>Due date</mat-label>
                    <input matInput type="date" name="dueDate" [(ngModel)]="assignDueDate" required />
                  </mat-form-field>
                }
                <button mat-flat-button type="submit" [disabled]="busy() || !assigneeId || (!t.dueDate && !assignDueDate)">
                  Assign task
                </button>
                <button mat-button type="button" (click)="panel.set(null)">Close</button>
              </form>
            </mat-card-content>
          </mat-card>
        }
        @case ('cancel') {
          <mat-card appearance="outlined" class="panel">
            <mat-card-content>
              <form (ngSubmit)="cancelTask()" aria-label="Cancel the task">
                <mat-form-field class="full">
                  <mat-label>Reason for cancelling</mat-label>
                  <textarea matInput name="reason" [(ngModel)]="cancelReason" rows="2" required maxlength="1000"></textarea>
                </mat-form-field>
                <button mat-flat-button type="submit" [disabled]="busy() || !cancelReason.trim()">Cancel task</button>
                <button mat-button type="button" (click)="panel.set(null)">Keep task</button>
              </form>
            </mat-card-content>
          </mat-card>
        }
        @case ('pause') {
          <mat-card appearance="outlined" class="panel">
            <mat-card-content>
              <form (ngSubmit)="work('pause', workReason)" aria-label="Pause work">
                <mat-form-field class="full">
                  <mat-label>Reason for the pause (optional unless your admin requires it)</mat-label>
                  <input matInput name="pauseReason" [(ngModel)]="workReason" maxlength="1000" />
                </mat-form-field>
                <button mat-flat-button type="submit" [disabled]="busy()">Pause work</button>
                <button mat-button type="button" (click)="panel.set(null)">Keep working</button>
              </form>
            </mat-card-content>
          </mat-card>
        }
        @case ('stop') {
          <mat-card appearance="outlined" class="panel">
            <mat-card-content>
              <form (ngSubmit)="work('stop', workReason)" aria-label="Stop work">
                <mat-form-field class="full">
                  <mat-label>Why are you stopping? (e.g. end of day, blocked)</mat-label>
                  <input matInput name="stopReason" [(ngModel)]="workReason" required maxlength="1000" />
                </mat-form-field>
                <button mat-flat-button type="submit" [disabled]="busy() || !workReason.trim()">Stop work</button>
                <button mat-button type="button" (click)="panel.set(null)">Keep working</button>
              </form>
            </mat-card-content>
          </mat-card>
        }
        @case ('delete') {
          <mat-card appearance="outlined" class="panel" role="alertdialog" aria-labelledby="delete-text">
            <mat-card-content>
              <p id="delete-text">Delete draft {{ t.key }} permanently? This cannot be undone.</p>
              <button mat-flat-button type="button" (click)="deleteTask()" [disabled]="busy()">Delete</button>
              <button mat-button type="button" (click)="panel.set(null)">Keep task</button>
            </mat-card-content>
          </mat-card>
        }
      }

      <mat-card appearance="outlined">
        <mat-card-content>
          <dl>
            <dt>Assignee</dt>
            <dd>{{ t.assignee?.name ?? 'Unassigned' }}</dd>
            <dt>Type · priority</dt>
            <dd>{{ t.type }} · {{ t.priority }}</dd>
            <dt>Due date</dt>
            <dd>
              {{ t.dueDate ? (t.dueDate | date: 'mediumDate') : 'Not set' }}
              @if (t.originalDueDate && t.originalDueDate !== t.dueDate) {
                <span class="muted">(originally {{ t.originalDueDate | date: 'mediumDate' }})</span>
              }
            </dd>
            <dt>Start date</dt>
            <dd>{{ t.startDate ? (t.startDate | date: 'mediumDate') : 'Not set' }}</dd>
            <dt>Allocated</dt>
            <dd>{{ allocation(t.allocatedMinutes) }}</dd>
            <dt>Spent</dt>
            <dd [class.overrun]="t.flags.overAllocated">
              {{ duration(t.spentSeconds) }}
              @if (t.flags.overAllocated) {
                (over the allocation)
              }
            </dd>
            @if (t.checklist.total) {
              <dt>Checklist</dt>
              <dd>{{ t.checklist.mandatoryDone }}/{{ t.checklist.mandatoryTotal }} required, {{ t.checklist.done }}/{{ t.checklist.total }} in total</dd>
            }
            <dt>Algorithm</dt>
            <dd>{{ t.algorithmRequired ? 'Required' : 'Not required' }}</dd>
            <dt>Revision</dt>
            <dd>{{ t.currentRevision }}</dd>
            @if (t.labels.length) {
              <dt>Labels</dt>
              <dd>{{ t.labels.join(', ') }}</dd>
            }
            @if (t.cancelReason) {
              <dt>Cancel reason</dt>
              <dd>{{ t.cancelReason }}</dd>
            }
            <dt>Created</dt>
            <dd>{{ t.createdAt | date: 'medium' }} by {{ t.createdBy.name }}</dd>
          </dl>
          @if (t.description) {
            <h2>Description</h2>
            <p class="description">{{ t.description }}</p>
          }
          @if (t.gitLab; as g) {
            <p class="muted">
              Imported from <a [href]="g.webUrl" target="_blank" rel="noopener noreferrer">GitLab issue #{{ g.issueIid }}</a>.
              GitLab never changes this task's status.
            </p>
            @if (g.pendingChanges; as changes) {
              <div class="gitlab-changes" role="status">
                <strong>Changed in GitLab since the task left Draft:</strong>
                @if (changes.title !== null) {
                  <p>Title: “{{ changes.title }}”</p>
                }
                @if (changes.description !== null) {
                  <p class="description">Description: {{ changes.description }}</p>
                }
                @if (can('acceptGitLabChanges')) {
                  <button mat-stroked-button type="button" (click)="acceptGitLabChanges()" [disabled]="busy()">Accept GitLab changes</button>
                }
              </div>
            }
          }
        </mat-card-content>
      </mat-card>

      <section class="columns">
        <div>
          <app-task-attachments [taskId]="t.id" [canAttach]="can('attach')" />
        </div>
        @if (t.algorithmRequired && t.canViewAlgorithm && t.status !== 'Draft') {
          <div>
            <app-task-algorithm [task]="t" (changed)="apply($event)" />
          </div>
        }
        @if (t.status !== 'Draft' || t.checklist.total || can('manageChecklist')) {
          <div>
            <app-task-checklist [task]="t" (changed)="reloadTask()" />
          </div>
        }
        @if (t.canViewTime && t.status !== 'Draft') {
          <div class="wide">
            <app-task-time [task]="t" />
          </div>
        }
        @if (t.status !== 'Draft') {
          <div>
            <app-task-review [task]="t" (changed)="afterReview($event)" />
          </div>
          <div>
            <app-task-requests [task]="t" (changed)="reloadTask()" />
          </div>
          @if (t.canViewDelays || can('reportDelay')) {
            <div>
              <app-task-delays [task]="t" />
            </div>
          }
        }
      </section>

      <section class="columns">
        <div>
          <app-task-comments [taskId]="t.id" [canComment]="can('comment')" />
        </div>
        <div>
          <h2>History</h2>
          <ol class="history">
            @for (entry of history(); track $index) {
              <li>
                <span class="muted">{{ entry.occurredAt | date: 'medium' }}</span>
                {{ describe(entry) }}
                @if (entry.actor) {
                  <span class="muted">by {{ entry.actor.name }}</span>
                }
                @if (entry.comment) {
                  <div class="muted">“{{ entry.comment }}”</div>
                }
              </li>
            }
          </ol>
        </div>
      </section>
    }
  `,
  styles: `
    .header {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }
    .status {
      padding: 2px 10px;
      border-radius: 12px;
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }
    .flag {
      padding: 2px 10px;
      border-radius: 12px;
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
    .flag.info {
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    .actions {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
      margin-bottom: 16px;
    }
    .panel,
    mat-card {
      margin-bottom: 16px;
    }
    .panel form {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    .full {
      width: 100%;
    }
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 16px;
    }
    dd {
      margin: 0;
    }
    .description {
      white-space: pre-wrap;
    }
    .gitlab-changes {
      padding: 8px 12px;
      border-radius: 4px;
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    .columns {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
      gap: 24px;
    }
    .columns .wide {
      grid-column: 1 / -1;
    }
    .actions.work {
      padding: 8px 0;
    }
    .overrun {
      color: var(--mat-sys-error);
    }
    .history {
      padding-left: 16px;
    }
    .history li {
      margin-bottom: 8px;
    }
    .muted,
    .notice {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class TaskDetail implements OnInit {
  private readonly tasksApi = inject(TasksApi);
  private readonly projectsApi = inject(ProjectsApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly activeSession = inject(ActiveSessionService);
  private readonly attachments = inject(TaskAttachmentsStore);
  private readonly gitLabApi = inject(GitLabApi);

  /** Route parameter. */
  readonly id = input.required<string>();

  protected readonly statusLabels = TASK_STATUS_LABELS;
  protected readonly task = signal<Task | null>(null);
  protected readonly history = signal<TaskHistoryEntry[]>([]);
  protected readonly members = signal<ProjectMember[]>([]);
  protected readonly panel = signal<Panel>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly assignable = computed(() => this.members().filter((m) => m.isActive && m.canWorkOnTasks));
  protected readonly hasWorkActions = computed(() =>
    (['start', 'resume', 'pause', 'stop'] as TaskActionName[]).some((a) => this.task()?.allowedActions.includes(a)),
  );
  protected workReason = '';
  protected assigneeId = '';
  protected assignDueDate = '';
  protected cancelReason = '';

  async ngOnInit(): Promise<void> {
    await this.run(async () => {
      this.task.set(await firstValueFrom(this.tasksApi.get(this.id())));
      await Promise.all([this.loadHistory(), this.attachments.load(this.id())]);
    });
  }

  protected can(action: TaskActionName): boolean {
    return this.task()?.allowedActions.includes(action) ?? false;
  }

  protected duration(seconds: number): string {
    return formatDuration(seconds);
  }

  /** Start, resume, pause or stop; then the timer bar and history are refreshed. */
  protected async work(action: WorkAction, reason?: string): Promise<void> {
    await this.run(async () => {
      const withReason = action === 'pause' || action === 'stop';
      this.task.set(await firstValueFrom(this.tasksApi.work(this.id(), action, withReason ? (reason ?? '').trim() : undefined)));
      this.panel.set(null);
      this.workReason = '';
      await this.loadHistory();
    });
    await this.activeSession.refresh();
  }

  /** A section changed the task (e.g. algorithm submitted or approved). */
  protected async apply(task: Task): Promise<void> {
    this.task.set(task);
    await this.loadHistory();
  }

  protected async reloadTask(): Promise<void> {
    await this.run(async () => {
      this.task.set(await firstValueFrom(this.tasksApi.get(this.id())));
      await this.loadHistory();
    });
  }

  /** Submitting for review ends the work session, so the timer bar refreshes too. */
  protected async afterReview(task: Task): Promise<void> {
    await this.apply(task);
    await this.activeSession.refresh();
  }

  protected async acceptGitLabChanges(): Promise<void> {
    await this.run(async () => {
      this.task.set(await firstValueFrom(this.gitLabApi.acceptChanges(this.id())));
      await this.loadHistory();
    });
  }

  protected allocation(minutes: number | null): string {
    return formatAllocation(minutes);
  }

  protected describe(entry: TaskHistoryEntry): string {
    return describeHistory(entry);
  }

  protected async openPanel(panel: Panel): Promise<void> {
    this.panel.set(panel);
    if (panel === 'assign' && !this.members().length) {
      await this.run(async () => this.members.set(await firstValueFrom(this.projectsApi.members(this.task()!.projectId))));
    }
  }

  protected async assign(): Promise<void> {
    await this.run(async () => {
      this.task.set(await firstValueFrom(this.tasksApi.assign(this.id(), this.assigneeId, this.assignDueDate || null)));
      this.panel.set(null);
      this.snackBar.open('Task assigned.', undefined, { duration: 3000 });
      await this.loadHistory();
    });
  }

  protected async cancelTask(): Promise<void> {
    await this.run(async () => {
      this.task.set(await firstValueFrom(this.tasksApi.cancel(this.id(), this.cancelReason)));
      this.panel.set(null);
      await this.loadHistory();
    });
    await this.activeSession.refresh();
  }

  protected async deleteTask(): Promise<void> {
    const projectId = this.task()!.projectId;
    await this.run(async () => {
      await firstValueFrom(this.tasksApi.delete(this.id()));
      this.snackBar.open('Draft deleted.', undefined, { duration: 3000 });
      await this.router.navigate(['/projects', projectId]);
    });
  }

  private async loadHistory(): Promise<void> {
    this.history.set(await firstValueFrom(this.tasksApi.history(this.id())));
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
