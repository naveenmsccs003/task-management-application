import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
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
import { AuthService } from '../../core/auth/auth.service';
import { ProjectsApi } from './projects.api';
import { PROJECT_STATUS_LABELS, ProjectListItem, ProjectStatus } from './projects.models';

@Component({
  selector: 'app-project-list',
  imports: [
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="header">
      <h1>Projects</h1>
      @if (auth.hasPermission('projects.create')) {
        <a mat-flat-button routerLink="/projects/new">New project</a>
      }
    </div>

    <form class="filters" (ngSubmit)="search()">
      <mat-form-field>
        <mat-label>Search name or key</mat-label>
        <input matInput name="search" [(ngModel)]="searchText" />
      </mat-form-field>
      <mat-form-field>
        <mat-label>Status</mat-label>
        <mat-select name="status" [(ngModel)]="status" (selectionChange)="search()">
          <mat-option value="">All</mat-option>
          @for (s of statuses; track s) {
            <mat-option [value]="s">{{ statusLabels[s] }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <button mat-stroked-button type="submit">Search</button>
    </form>

    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading projects" />
    }
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    <table mat-table [dataSource]="result().items" aria-label="Projects">
      <ng-container matColumnDef="key">
        <th mat-header-cell *matHeaderCellDef>Key</th>
        <td mat-cell *matCellDef="let p">{{ p.key }}</td>
      </ng-container>
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>Name</th>
        <td mat-cell *matCellDef="let p"><a [routerLink]="['/projects', p.id]">{{ p.name }}</a></td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let p">{{ statusLabel(p.status) }}</td>
      </ng-container>
      <ng-container matColumnDef="role">
        <th mat-header-cell *matHeaderCellDef>My role</th>
        <td mat-cell *matCellDef="let p">{{ p.myRole?.name ?? '—' }}</td>
      </ng-container>
      <ng-container matColumnDef="openTasks">
        <th mat-header-cell *matHeaderCellDef>Open tasks</th>
        <td mat-cell *matCellDef="let p">{{ p.openTaskCount }}</td>
      </ng-container>
      <ng-container matColumnDef="target">
        <th mat-header-cell *matHeaderCellDef>Target end</th>
        <td mat-cell *matCellDef="let p">{{ p.targetEndDate ? (p.targetEndDate | date: 'mediumDate') : '—' }}</td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
      <tr class="mat-row" *matNoDataRow>
        <td class="mat-cell" [attr.colspan]="columns.length">No projects found.</td>
      </tr>
    </table>
    <mat-paginator
      [length]="result().totalCount"
      [pageIndex]="result().page - 1"
      [pageSize]="result().pageSize"
      [pageSizeOptions]="[25, 50, 100]"
      (page)="changePage($event)"
      aria-label="Project pages"
    />
  `,
  styles: `
    .header {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    .error {
      color: var(--mat-sys-error);
    }
    table {
      width: 100%;
    }
  `,
})
export class ProjectList {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ProjectsApi);

  protected readonly columns = ['key', 'name', 'status', 'role', 'openTasks', 'target'];
  protected readonly statuses = Object.keys(PROJECT_STATUS_LABELS) as ProjectStatus[];
  protected readonly statusLabels = PROJECT_STATUS_LABELS;
  protected readonly statusLabel = (status: ProjectStatus): string => PROJECT_STATUS_LABELS[status];
  protected readonly loading = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly result = signal<PagedResult<ProjectListItem>>({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  protected searchText = '';
  protected status: ProjectStatus | '' = '';

  constructor() {
    void this.load(1, 25);
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
          this.api.list({ search: this.searchText.trim() || undefined, status: this.status || undefined, page, pageSize }),
        ),
      );
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.loading.set(false);
    }
  }
}
