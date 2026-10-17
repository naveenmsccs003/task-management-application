import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../../core/api/api-error';
import { PermissionScope } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { AdminApi } from '../admin.api';
import { Permission, Role } from '../admin.models';

type ScopeChoice = PermissionScope | 'None';

/** Roles and their permission grants (FR-ROLE-01..03). Changes apply within a minute; the API re-checks everything. */
@Component({
  selector: 'app-roles',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatListModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Roles and permissions</h1>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    <div class="layout">
      <section aria-label="Roles">
        <mat-selection-list [multiple]="false" (selectionChange)="select($event.options[0].value)">
          @for (role of roles(); track role.id) {
            <mat-list-option [value]="role.id" [selected]="role.id === selectedId()">
              <span matListItemTitle>{{ role.name }}</span>
              <span matListItemLine>{{ role.memberCount }} member(s){{ role.isSystem ? ' · built-in' : '' }}</span>
            </mat-list-option>
          }
        </mat-selection-list>
        @if (canManage()) {
          <form class="new-role" (ngSubmit)="createRole()">
            <mat-form-field>
              <mat-label>New role name</mat-label>
              <input matInput name="newRole" [(ngModel)]="newRoleName" maxlength="100" />
            </mat-form-field>
            <button mat-stroked-button type="submit" [disabled]="!newRoleName.trim() || busy()">Add role</button>
          </form>
        }
      </section>

      @if (selected(); as role) {
        <section aria-label="Permissions" class="permissions">
          <h2>{{ role.name }}</h2>
          <p>{{ role.description }}</p>
          <p class="hint">
            <strong>Global</strong>: everywhere. <strong>Project</strong>: only in projects where the member has this role.
            <strong>Own</strong>: only records assigned to or created by the member.
          </p>
          <table>
            <thead>
              <tr><th scope="col">Permission</th><th scope="col">Scope</th></tr>
            </thead>
            <tbody>
              @for (permission of permissions(); track permission.code) {
                <tr>
                  <td>
                    <span class="code">{{ permission.code }}</span><br />
                    <span class="description">{{ permission.description }}</span>
                  </td>
                  <td>
                    <mat-form-field subscriptSizing="dynamic">
                      <mat-select
                        [attr.aria-label]="'Scope for ' + permission.code"
                        [value]="draft()[permission.code] ?? 'None'"
                        [disabled]="!canManage()"
                        (selectionChange)="setScope(permission.code, $event.value)"
                      >
                        <mat-option value="None">—</mat-option>
                        <mat-option value="Own">Own</mat-option>
                        <mat-option value="Project">Project</mat-option>
                        <mat-option value="Global">Global</mat-option>
                      </mat-select>
                    </mat-form-field>
                  </td>
                </tr>
              }
            </tbody>
          </table>
          @if (canManage()) {
            <div class="actions">
              <button mat-flat-button type="button" (click)="savePermissions()" [disabled]="busy() || !dirty()">Save permissions</button>
              @if (!role.isSystem && role.memberCount === 0) {
                <button mat-stroked-button type="button" (click)="deleteRole()" [disabled]="busy()">Delete role</button>
              }
            </div>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .layout {
      display: grid;
      grid-template-columns: minmax(220px, 280px) 1fr;
      gap: 24px;
      align-items: start;
    }
    @media (max-width: 800px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
    table {
      border-collapse: collapse;
      width: 100%;
    }
    th {
      text-align: left;
    }
    td {
      padding: 4px 8px 4px 0;
      vertical-align: middle;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .code {
      font-family: monospace;
    }
    .description,
    .hint {
      font: var(--mat-sys-body-small);
    }
    .actions {
      display: flex;
      gap: 12px;
      margin-top: 16px;
    }
    .new-role {
      display: flex;
      gap: 8px;
      align-items: baseline;
      margin-top: 12px;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class Roles implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly roles = signal<Role[]>([]);
  protected readonly permissions = signal<Permission[]>([]);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly draft = signal<Record<string, PermissionScope>>({});
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly selected = computed(() => this.roles().find((r) => r.id === this.selectedId()) ?? null);
  protected readonly canManage = computed(() => this.auth.hasPermission('roles.manage'));
  protected readonly dirty = computed(() => {
    const role = this.selected();
    if (!role) return false;
    const original = Object.fromEntries(role.permissions.map((p) => [p.code, p.scope]));
    return JSON.stringify(sortKeys(original)) !== JSON.stringify(sortKeys(this.draft()));
  });
  protected newRoleName = '';

  async ngOnInit(): Promise<void> {
    await this.run(async () => {
      const [roles, permissions] = await Promise.all([firstValueFrom(this.api.roles()), firstValueFrom(this.api.permissions())]);
      this.permissions.set(permissions);
      this.roles.set(roles);
      if (roles.length) this.select(roles[0].id);
    });
  }

  protected select(id: string): void {
    this.selectedId.set(id);
    const role = this.roles().find((r) => r.id === id);
    this.draft.set(Object.fromEntries((role?.permissions ?? []).map((p) => [p.code, p.scope])));
  }

  protected setScope(code: string, scope: ScopeChoice): void {
    this.draft.update((current) => {
      const next = { ...current };
      if (scope === 'None') delete next[code];
      else next[code] = scope;
      return next;
    });
  }

  protected async savePermissions(): Promise<void> {
    const role = this.selected()!;
    await this.run(async () => {
      const grants = Object.entries(this.draft()).map(([code, scope]) => ({ code, scope }));
      this.replace(await firstValueFrom(this.api.setRolePermissions(role.id, grants, role.version)));
      this.snackBar.open('Permissions saved. They apply within a minute.', undefined, { duration: 4000 });
    });
  }

  protected async createRole(): Promise<void> {
    await this.run(async () => {
      const role = await firstValueFrom(this.api.createRole(this.newRoleName.trim(), ''));
      this.roles.update((roles) => [...roles, role].sort((a, b) => a.name.localeCompare(b.name)));
      this.newRoleName = '';
      this.select(role.id);
    });
  }

  protected async deleteRole(): Promise<void> {
    const role = this.selected()!;
    await this.run(async () => {
      await firstValueFrom(this.api.deleteRole(role.id));
      this.roles.update((roles) => roles.filter((r) => r.id !== role.id));
      if (this.roles().length) this.select(this.roles()[0].id);
    });
  }

  private replace(role: Role): void {
    this.roles.update((roles) => roles.map((r) => (r.id === role.id ? role : r)));
    this.select(role.id);
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

function sortKeys(value: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(value).sort(([a], [b]) => a.localeCompare(b)));
}
