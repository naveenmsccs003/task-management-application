import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { RoleRef } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { ProjectsApi } from './projects.api';
import { isReadOnly, MemberCandidate, Project, projectAllows, ProjectMember } from './projects.models';

/** Members of one project, with add / change role / remove for members holding projects.members.manage. */
@Component({
  selector: 'app-project-members',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    @if (canManage()) {
      <form class="add" (ngSubmit)="add()" aria-label="Add a project member">
        <mat-form-field>
          <mat-label>Find a person</mat-label>
          <input matInput name="candidateSearch" [(ngModel)]="candidateSearch" (ngModelChange)="findCandidates()" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>Person</mat-label>
          <mat-select name="candidate" [(ngModel)]="candidateId" required>
            @for (c of candidates(); track c.id) {
              <mat-option [value]="c.id">{{ c.fullName }} ({{ c.email }})</mat-option>
            } @empty {
              <mat-option disabled>No matching people</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>Project role</mat-label>
          <mat-select name="role" [(ngModel)]="roleId" required>
            @for (r of roles(); track r.id) {
              <mat-option [value]="r.id">{{ r.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy() || !candidateId || !roleId">Add member</button>
      </form>
    }

    <table mat-table [dataSource]="members()" aria-label="Project members">
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>Name</th>
        <td mat-cell *matCellDef="let m">
          {{ m.fullName }}
          @if (!m.isActive) {
            <span class="muted">(deactivated)</span>
          }
          <div class="muted">{{ m.email }}</div>
        </td>
      </ng-container>
      <ng-container matColumnDef="role">
        <th mat-header-cell *matHeaderCellDef>Project role</th>
        <td mat-cell *matCellDef="let m">
          @if (canManage() && m.userId !== auth.me()?.id) {
            <mat-form-field class="inline" subscriptSizing="dynamic">
              <mat-select
                [value]="m.projectRole.id"
                (selectionChange)="changeRole(m, $event.value)"
                [attr.aria-label]="'Project role of ' + m.fullName"
              >
                @for (r of roles(); track r.id) {
                  <mat-option [value]="r.id">{{ r.name }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          } @else {
            {{ m.projectRole.name }}
          }
        </td>
      </ng-container>
      <ng-container matColumnDef="openTasks">
        <th mat-header-cell *matHeaderCellDef>Open tasks</th>
        <td mat-cell *matCellDef="let m">{{ m.openTaskCount }}</td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef><span class="visually-hidden">Actions</span></th>
        <td mat-cell *matCellDef="let m">
          @if (canManage() && m.userId !== auth.me()?.id) {
            <button
              mat-button
              type="button"
              (click)="remove(m)"
              [disabled]="busy() || m.openTaskCount > 0"
              [attr.aria-label]="'Remove ' + m.fullName"
              [title]="m.openTaskCount > 0 ? 'Reassign their open tasks first' : ''"
            >
              Remove
            </button>
          }
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
      <tr class="mat-row" *matNoDataRow>
        <td class="mat-cell" [attr.colspan]="columns.length">No members yet.</td>
      </tr>
    </table>
  `,
  styles: `
    .add {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .inline {
      width: 180px;
    }
    .error {
      color: var(--mat-sys-error);
    }
    table {
      width: 100%;
    }
    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
    }
  `,
})
export class ProjectMembers implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ProjectsApi);

  readonly project = input.required<Project>();
  /** Emitted after a change, so the parent can refresh counts. */
  readonly changed = output<void>();

  protected readonly columns = ['name', 'role', 'openTasks', 'actions'];
  protected readonly members = signal<ProjectMember[]>([]);
  protected readonly roles = signal<RoleRef[]>([]);
  protected readonly candidates = signal<MemberCandidate[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly canManage = computed(
    () => projectAllows(this.project(), 'projects.members.manage') && !isReadOnly(this.project()),
  );
  protected candidateSearch = '';
  protected candidateId = '';
  protected roleId = '';
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  async ngOnInit(): Promise<void> {
    await this.run(async () => {
      this.members.set(await firstValueFrom(this.api.members(this.project().id)));
      if (this.canManage()) {
        const roles = await firstValueFrom(this.api.projectRoles());
        this.roles.set(roles);
        this.roleId = roles.find((r) => r.name === 'Developer')?.id ?? '';
        await this.loadCandidates();
      }
    });
  }

  protected findCandidates(): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.loadCandidates(), 250);
  }

  protected async add(): Promise<void> {
    await this.run(async () => {
      this.members.set(await firstValueFrom(this.api.addMember(this.project().id, this.candidateId, this.roleId)));
      this.candidateId = '';
      await this.loadCandidates();
      this.changed.emit();
    });
  }

  protected async changeRole(member: ProjectMember, roleId: string): Promise<void> {
    await this.run(async () => {
      this.members.set(await firstValueFrom(this.api.changeMemberRole(this.project().id, member.userId, roleId)));
    });
    // On failure the select already shows the new value; reload so it shows the saved role again.
    if (this.failure()) {
      this.members.set(await firstValueFrom(this.api.members(this.project().id)));
    }
  }

  protected async remove(member: ProjectMember): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.removeMember(this.project().id, member.userId));
      this.members.set(await firstValueFrom(this.api.members(this.project().id)));
      await this.loadCandidates();
      this.changed.emit();
    });
  }

  private async loadCandidates(): Promise<void> {
    this.candidates.set(await firstValueFrom(this.api.candidates(this.project().id, this.candidateSearch.trim())));
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
