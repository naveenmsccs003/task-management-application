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
import { asApiError } from '../../../core/api/api-error';
import { PagedResult } from '../../../core/api/paged';
import { AuthService } from '../../../core/auth/auth.service';
import { AdminApi } from '../admin.api';
import { Member } from '../admin.models';

@Component({
  selector: 'app-member-list',
  imports: [
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="header">
      <h1>Members</h1>
      @if (auth.hasPermission('members.create')) {
        <a mat-flat-button routerLink="/admin/members/new">Add member</a>
      }
    </div>

    <form class="filters" (ngSubmit)="search()">
      <mat-form-field>
        <mat-label>Search name, email or code</mat-label>
        <input matInput name="search" [(ngModel)]="searchText" />
      </mat-form-field>
      <mat-form-field>
        <mat-label>Status</mat-label>
        <mat-select name="status" [(ngModel)]="status" (selectionChange)="search()">
          <mat-option value="all">All</mat-option>
          <mat-option value="active">Active</mat-option>
          <mat-option value="inactive">Deactivated</mat-option>
        </mat-select>
      </mat-form-field>
      <button mat-stroked-button type="submit">Search</button>
    </form>

    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading members" />
    }
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    <table mat-table [dataSource]="result().items" aria-label="Members">
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>Name</th>
        <td mat-cell *matCellDef="let m"><a [routerLink]="['/admin/members', m.id]">{{ m.fullName }}</a></td>
      </ng-container>
      <ng-container matColumnDef="email">
        <th mat-header-cell *matHeaderCellDef>Email</th>
        <td mat-cell *matCellDef="let m">{{ m.email }}</td>
      </ng-container>
      <ng-container matColumnDef="role">
        <th mat-header-cell *matHeaderCellDef>Role</th>
        <td mat-cell *matCellDef="let m">{{ m.globalRole.name }}</td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let m">
          {{ !m.isActive ? 'Deactivated' : m.hasPassword ? 'Active' : 'Invited' }}
        </td>
      </ng-container>
      <ng-container matColumnDef="lastSignIn">
        <th mat-header-cell *matHeaderCellDef>Last sign-in</th>
        <td mat-cell *matCellDef="let m">{{ m.lastSignInAt ? (m.lastSignInAt | date: 'medium') : '—' }}</td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
      <tr class="mat-row" *matNoDataRow>
        <td class="mat-cell" [attr.colspan]="columns.length">No members found.</td>
      </tr>
    </table>
    <mat-paginator
      [length]="result().totalCount"
      [pageIndex]="result().page - 1"
      [pageSize]="result().pageSize"
      [pageSizeOptions]="[25, 50, 100]"
      (page)="changePage($event)"
      aria-label="Member pages"
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
export class MemberList {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(AdminApi);

  protected readonly columns = ['name', 'email', 'role', 'status', 'lastSignIn'];
  protected readonly loading = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly result = signal<PagedResult<Member>>({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  protected searchText = '';
  protected status: 'all' | 'active' | 'inactive' = 'active';

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
          this.api.members({
            search: this.searchText.trim() || undefined,
            isActive: this.status === 'all' ? undefined : this.status === 'active',
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
