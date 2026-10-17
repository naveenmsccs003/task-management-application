import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { TaskList } from '../tasks/task-list';
import { ProjectMembers } from './project-members';
import { ProjectsApi } from './projects.api';
import { isReadOnly, Project, projectAllows, PROJECT_STATUS_LABELS, ProjectStatusAction } from './projects.models';

/** One project: details, status actions, its tasks and its members. */
@Component({
  selector: 'app-project-detail',
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatTabsModule, ProjectMembers, TaskList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/projects">← Projects</a>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    @if (project(); as p) {
      <div class="header">
        <h1>{{ p.key }} · {{ p.name }}</h1>
        <span class="status" [class.read-only]="readOnly()">{{ statusLabels[p.status] }}</span>
      </div>
      @if (readOnly()) {
        <p class="notice">This project is {{ statusLabels[p.status].toLowerCase() }}, so it and its tasks are read-only.</p>
      }

      <mat-card appearance="outlined">
        <mat-card-content>
          @if (p.description) {
            <p class="description">{{ p.description }}</p>
          }
          <dl>
            <dt>My role</dt>
            <dd>{{ p.myRole?.name ?? 'Not a member' }}</dd>
            <dt>Dates</dt>
            <dd>
              {{ p.startDate ? (p.startDate | date: 'mediumDate') : 'No start date' }} –
              {{ p.targetEndDate ? (p.targetEndDate | date: 'mediumDate') : 'no target end date' }}
            </dd>
            <dt>Algorithm</dt>
            <dd>{{ p.algorithmRequiredDefault ? 'Required for new tasks by default' : 'Not required by default' }}</dd>
            <dt>Closure</dt>
            <dd>{{ p.closeOnApproval ? 'Closed on Team Leader approval' : 'Closed separately after approval' }}</dd>
            <dt>Open tasks</dt>
            <dd>{{ p.openTaskCount }}</dd>
          </dl>

          @if (canUpdate()) {
            <div class="actions">
              @if (!readOnly()) {
                <a mat-stroked-button [routerLink]="['/projects', p.id, 'edit']">Edit details</a>
              }
              @if (p.status === 'Active') {
                <button mat-stroked-button type="button" (click)="changeStatus('hold')" [disabled]="busy()">Put on hold</button>
              }
              @if (p.status === 'Active' || p.status === 'OnHold') {
                <button mat-stroked-button type="button" (click)="changeStatus('complete')" [disabled]="busy()">Mark completed</button>
              }
              @if (p.status !== 'Archived') {
                <button mat-stroked-button type="button" (click)="changeStatus('archive')" [disabled]="busy()">Archive</button>
              }
              @if (p.status !== 'Active') {
                <button mat-stroked-button type="button" (click)="changeStatus('reactivate')" [disabled]="busy()">Reactivate</button>
              }
            </div>
          }

          @if (pendingConfirmation(); as pending) {
            <div class="confirm" role="alertdialog" aria-labelledby="confirm-text">
              <p id="confirm-text">{{ pending.message }}</p>
              <button mat-flat-button type="button" (click)="changeStatus(pending.action, true)" [disabled]="busy()">Confirm</button>
              <button mat-button type="button" (click)="pendingConfirmation.set(null)">Keep it as it is</button>
            </div>
          }
        </mat-card-content>
      </mat-card>

      <mat-tab-group>
        <mat-tab label="Tasks">
          <div class="tab">
            @if (canCreateTasks()) {
              <a mat-flat-button [routerLink]="['/projects', p.id, 'tasks', 'new']">New task</a>
            }
            <app-task-list [projectId]="p.id" />
          </div>
        </mat-tab>
        <mat-tab [label]="'Members (' + p.memberCount + ')'">
          <div class="tab">
            <app-project-members [project]="p" (changed)="reload()" />
          </div>
        </mat-tab>
      </mat-tab-group>
    }
  `,
  styles: `
    .header {
      display: flex;
      align-items: center;
      gap: 16px;
      flex-wrap: wrap;
    }
    .status {
      padding: 2px 10px;
      border-radius: 12px;
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }
    .status.read-only {
      background: var(--mat-sys-surface-variant);
      color: var(--mat-sys-on-surface-variant);
    }
    .notice {
      color: var(--mat-sys-on-surface-variant);
    }
    .description {
      white-space: pre-wrap;
    }
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 16px;
    }
    dd {
      margin: 0;
    }
    .actions,
    .confirm {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
      align-items: center;
      margin-top: 12px;
    }
    mat-card {
      margin-bottom: 16px;
    }
    .tab {
      padding-top: 16px;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class ProjectDetail implements OnInit {
  private readonly api = inject(ProjectsApi);

  /** Route parameter. */
  readonly id = input.required<string>();

  protected readonly statusLabels = PROJECT_STATUS_LABELS;
  protected readonly project = signal<Project | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly pendingConfirmation = signal<{ action: ProjectStatusAction; message: string } | null>(null);
  protected readonly readOnly = computed(() => isReadOnly(this.project()));
  protected readonly canUpdate = computed(() => projectAllows(this.project(), 'projects.update'));
  protected readonly canCreateTasks = computed(
    () => this.project()?.status === 'Active' && projectAllows(this.project(), 'tasks.create'),
  );

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    try {
      this.project.set(await firstValueFrom(this.api.get(this.id())));
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }

  protected async changeStatus(action: ProjectStatusAction, confirmOpenTasks = false): Promise<void> {
    this.busy.set(true);
    this.failure.set(null);
    try {
      this.project.set(await firstValueFrom(this.api.changeStatus(this.id(), action, confirmOpenTasks)));
      this.pendingConfirmation.set(null);
    } catch (error) {
      const apiError = asApiError(error);
      if (apiError.code === 'project.has_open_tasks') {
        this.pendingConfirmation.set({ action, message: apiError.message });
      } else {
        this.failure.set(apiError.message);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
