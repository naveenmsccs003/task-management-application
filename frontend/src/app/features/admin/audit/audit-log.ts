import { DatePipe, JsonPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../../core/api/api-error';
import { PagedResult } from '../../../core/api/paged';
import { AdminApi } from '../admin.api';
import { AuditEntry } from '../admin.models';

@Component({
  selector: 'app-audit-log',
  imports: [
    DatePipe, JsonPipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatPaginatorModule, MatProgressBarModule, MatTableModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Audit log</h1>
    <form class="filters" (ngSubmit)="load(1)">
      <mat-form-field>
        <mat-label>Action (e.g. member.created)</mat-label>
        <input matInput name="action" [(ngModel)]="action" />
      </mat-form-field>
      <mat-form-field>
        <mat-label>Entity type</mat-label>
        <input matInput name="entityType" [(ngModel)]="entityType" />
      </mat-form-field>
      <mat-form-field>
        <mat-label>Entity id</mat-label>
        <input matInput name="entityId" [(ngModel)]="entityId" />
      </mat-form-field>
      <button mat-stroked-button type="submit">Filter</button>
    </form>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading audit entries" />
    }
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    <table mat-table [dataSource]="result().items" aria-label="Audit entries">
      <ng-container matColumnDef="time">
        <th mat-header-cell *matHeaderCellDef>Time</th>
        <td mat-cell *matCellDef="let e">{{ e.occurredAt | date: 'medium' }}</td>
      </ng-container>
      <ng-container matColumnDef="actor">
        <th mat-header-cell *matHeaderCellDef>Actor</th>
        <td mat-cell *matCellDef="let e">{{ e.actorName ?? (e.actorId ? e.actorId : 'System / anonymous') }}<br /><small>{{ e.actorIp }}</small></td>
      </ng-container>
      <ng-container matColumnDef="action">
        <th mat-header-cell *matHeaderCellDef>Action</th>
        <td mat-cell *matCellDef="let e"><code>{{ e.action }}</code></td>
      </ng-container>
      <ng-container matColumnDef="entity">
        <th mat-header-cell *matHeaderCellDef>Entity</th>
        <td mat-cell *matCellDef="let e">{{ e.entityType }}<br /><small>{{ e.entityId }}</small></td>
      </ng-container>
      <ng-container matColumnDef="changes">
        <th mat-header-cell *matHeaderCellDef>Changes</th>
        <td mat-cell *matCellDef="let e">
          @if (e.changes) {
            <pre>{{ e.changes | json }}</pre>
          }
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
      <tr class="mat-row" *matNoDataRow>
        <td class="mat-cell" [attr.colspan]="columns.length">No entries.</td>
      </tr>
    </table>
    <mat-paginator
      [length]="result().totalCount"
      [pageIndex]="result().page - 1"
      [pageSize]="result().pageSize"
      [pageSizeOptions]="[25, 50, 100]"
      (page)="changePage($event)"
      aria-label="Audit pages"
    />
  `,
  styles: `
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    table {
      width: 100%;
    }
    pre {
      margin: 0;
      font-size: 12px;
      white-space: pre-wrap;
      max-width: 420px;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class AuditLog {
  private readonly api = inject(AdminApi);

  protected readonly columns = ['time', 'actor', 'action', 'entity', 'changes'];
  protected readonly loading = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly result = signal<PagedResult<AuditEntry>>({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  protected action = '';
  protected entityType = '';
  protected entityId = '';

  constructor() {
    void this.load(1);
  }

  protected changePage(event: PageEvent): void {
    void this.load(event.pageIndex + 1, event.pageSize);
  }

  protected async load(page: number, pageSize = this.result().pageSize): Promise<void> {
    this.loading.set(true);
    this.failure.set(null);
    try {
      this.result.set(
        await firstValueFrom(
          this.api.auditLogs({
            action: this.action.trim() || undefined,
            entityType: this.entityType.trim() || undefined,
            entityId: this.entityId.trim() || undefined,
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
