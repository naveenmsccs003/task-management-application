import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { PagedResult } from '../../core/api/paged';
import { TasksApi } from './tasks.api';
import { TASK_PRIORITIES, TASK_STATUS_LABELS, TASK_STATUSES, TaskListItem, TaskPriority, TaskStatus } from './tasks.models';

/**
 * Server-side filtered, sorted and paginated task list (FR-TASK-07). Route /tasks shows every task the member can
 * see; the project page embeds it with a fixed project.
 */
@Component({
  selector: 'app-task-list',
  imports: [
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule,
    MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!projectId()) {
      <h1>Tasks</h1>
    }

    <form class="filters" (ngSubmit)="search()">
      <mat-form-field>
        <mat-label>Search key or title</mat-label>
        <input matInput name="q" [(ngModel)]="q" />
      </mat-form-field>
      <mat-form-field>
        <mat-label>Status</mat-label>
        <mat-select name="status" multiple [(ngModel)]="statuses" (selectionChange)="search()">
          @for (s of allStatuses; track s) {
            <mat-option [value]="s">{{ statusLabels[s] }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field>
        <mat-label>Priority</mat-label>
        <mat-select name="priority" multiple [(ngModel)]="priorities" (selectionChange)="search()">
          @for (p of allPriorities; track p) {
            <mat-option [value]="p">{{ p }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field>
        <mat-label>Sort by</mat-label>
        <mat-select name="sort" [(ngModel)]="sort" (selectionChange)="search()">
          <mat-option value="dueDate">Due date</mat-option>
          <mat-option value="-priority">Priority (highest first)</mat-option>
          <mat-option value="-updatedAt">Recently updated</mat-option>
          <mat-option value="-createdAt">Newest</mat-option>
          <mat-option value="key">Key</mat-option>
        </mat-select>
      </mat-form-field>
      <mat-checkbox name="overdue" [(ngModel)]="overdueOnly" (change)="search()">Overdue only</mat-checkbox>
      <button mat-stroked-button type="submit">Search</button>
    </form>

    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading tasks" />
    }
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    <table mat-table [dataSource]="result().items" aria-label="Tasks">
      <ng-container matColumnDef="key">
        <th mat-header-cell *matHeaderCellDef>Key</th>
        <td mat-cell *matCellDef="let t"><a [routerLink]="['/tasks', t.id]">{{ t.key }}</a></td>
      </ng-container>
      <ng-container matColumnDef="title">
        <th mat-header-cell *matHeaderCellDef>Title</th>
        <td mat-cell *matCellDef="let t">
          {{ t.title }}
          @for (label of t.labels; track label) {
            <span class="label">{{ label }}</span>
          }
        </td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let t">{{ statusLabel(t.status) }}</td>
      </ng-container>
      <ng-container matColumnDef="priority">
        <th mat-header-cell *matHeaderCellDef>Priority</th>
        <td mat-cell *matCellDef="let t">{{ t.priority }}</td>
      </ng-container>
      <ng-container matColumnDef="assignee">
        <th mat-header-cell *matHeaderCellDef>Assignee</th>
        <td mat-cell *matCellDef="let t">{{ t.assignee?.name ?? 'Unassigned' }}</td>
      </ng-container>
      <ng-container matColumnDef="due">
        <th mat-header-cell *matHeaderCellDef>Due</th>
        <td mat-cell *matCellDef="let t" [class.overdue]="t.overdue">
          {{ t.dueDate ? (t.dueDate | date: 'mediumDate') : '—' }}
          @if (t.overdue) {
            <span>(overdue)</span>
          }
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
      <tr class="mat-row" *matNoDataRow>
        <td class="mat-cell" [attr.colspan]="columns.length">No tasks found.</td>
      </tr>
    </table>
    <mat-paginator
      [length]="result().totalCount"
      [pageIndex]="result().page - 1"
      [pageSize]="result().pageSize"
      [pageSizeOptions]="[25, 50, 100]"
      (page)="changePage($event)"
      aria-label="Task pages"
    />
  `,
  styles: `
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    .label {
      display: inline-block;
      margin-left: 6px;
      padding: 0 6px;
      border-radius: 8px;
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
      font: var(--mat-sys-label-small);
    }
    .overdue {
      color: var(--mat-sys-error);
    }
    .error {
      color: var(--mat-sys-error);
    }
    table {
      width: 100%;
    }
  `,
})
export class TaskList implements OnInit {
  private readonly api = inject(TasksApi);

  /** Set when embedded in a project page. */
  readonly projectId = input<string>();

  protected readonly allStatuses = TASK_STATUSES;
  protected readonly allPriorities = TASK_PRIORITIES;
  protected readonly statusLabels = TASK_STATUS_LABELS;
  protected readonly statusLabel = (status: TaskStatus): string => TASK_STATUS_LABELS[status];
  protected readonly columns = ['key', 'title', 'status', 'priority', 'assignee', 'due'];
  protected readonly loading = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly result = signal<PagedResult<TaskListItem>>({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  protected q = '';
  protected statuses: TaskStatus[] = [];
  protected priorities: TaskPriority[] = [];
  protected sort = 'dueDate';
  protected overdueOnly = false;

  ngOnInit(): void {
    void this.load(1, 25);
  }

  /** Reloads the current page (e.g. after a task is created elsewhere on the page). */
  reload(): void {
    void this.load(this.result().page, this.result().pageSize);
  }

  protected search(): void {
    void this.load(1, this.result().pageSize);
  }

  protected changePage(event: PageEvent): void {
    void this.load(event.pageIndex + 1, event.pageSize);
  }

  private async load(page: number, pageSize: number): Promise<void> {
    this.loading.set(true);
    this.failure.set(null);
    try {
      this.result.set(
        await firstValueFrom(
          this.api.list({
            projectId: this.projectId(),
            status: this.statuses,
            priority: this.priorities,
            overdue: this.overdueOnly || undefined,
            q: this.q.trim() || undefined,
            sort: this.sort,
            page,
            pageSize,
          }),
        ),
      );
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.loading.set(false);
    }
  }
}
