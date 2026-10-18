import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { applyServerErrors, controlError } from '../../core/api/form-errors';
import { GitLabApi } from './gitlab.api';
import { CONNECTION_STATUS_LABELS, GitLabConnection } from './gitlab.models';

/**
 * GitLab connections for Admins (FR-IMP-01/02). The token field is write-only: it is never filled in from the server,
 * and leaving it empty when editing keeps the stored token.
 */
@Component({
  selector: 'app-gitlab-connections',
  imports: [DatePipe, ReactiveFormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>GitLab connections</h1>
    <p class="muted">Use an access token with only the <code>read_api</code> scope. Tokens are encrypted and cannot be shown again.</p>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    @for (c of connections(); track c.id) {
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-card-title>{{ c.name }}</mat-card-title>
          <mat-card-subtitle>{{ c.baseUrl }} · token {{ c.tokenHint }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <p>
            <span class="status" [class.bad]="c.status === 'Unhealthy'" [attr.data-testid]="'status-' + c.name">{{ statusLabels[c.status] }}</span>
            @if (c.lastCheckedAt) {
              <span class="muted">checked {{ c.lastCheckedAt | date: 'short' }}</span>
            }
            · {{ c.linkedProjects }} linked {{ c.linkedProjects === 1 ? 'project' : 'projects' }}
          </p>
          @if (c.lastError) {
            <p class="error">{{ c.lastError }}</p>
          }
        </mat-card-content>
        <mat-card-actions>
          <button mat-button type="button" (click)="test(c)" [disabled]="busy()">Test</button>
          <button mat-button type="button" (click)="edit(c)" [disabled]="busy()">Edit</button>
          <button mat-button type="button" (click)="remove(c)" [disabled]="busy() || c.linkedProjects > 0">Delete</button>
        </mat-card-actions>
      </mat-card>
    } @empty {
      <p class="muted">No connections yet.</p>
    }

    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ editing() ? 'Edit ' + editing()!.name : 'Add a connection' }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="save()" novalidate aria-label="GitLab connection">
          <mat-form-field>
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" maxlength="100" required />
            <mat-error>{{ error('name', 'Name') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>GitLab address</mat-label>
            <input matInput formControlName="baseUrl" placeholder="https://gitlab.example.com" required />
            <mat-error>{{ error('baseUrl', 'Address') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ editing() ? 'New access token (leave empty to keep)' : 'Access token' }}</mat-label>
            <input matInput type="password" formControlName="token" autocomplete="off" />
            <mat-error>{{ error('token', 'Access token') }}</mat-error>
          </mat-form-field>
          <div class="actions">
            <button mat-flat-button type="submit" [disabled]="busy()">{{ editing() ? 'Save' : 'Add connection' }}</button>
            @if (editing()) {
              <button mat-button type="button" (click)="cancel()">Cancel</button>
            }
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card {
      max-width: 720px;
      margin-bottom: 16px;
    }
    form {
      display: flex;
      flex-direction: column;
    }
    .actions {
      display: flex;
      gap: 8px;
    }
    .status {
      padding: 2px 10px;
      border-radius: 12px;
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }
    .status.bad {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class GitLabConnections implements OnInit {
  private readonly api = inject(GitLabApi);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly statusLabels = CONNECTION_STATUS_LABELS;
  protected readonly connections = signal<GitLabConnection[]>([]);
  protected readonly editing = signal<GitLabConnection | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    baseUrl: ['', [Validators.required]],
    token: [''],
  });

  async ngOnInit(): Promise<void> {
    await this.run(async () => this.connections.set(await firstValueFrom(this.api.connections())));
  }

  protected error(name: keyof typeof this.form.controls, label: string): string | null {
    return controlError(this.form.controls[name], label);
  }

  protected edit(connection: GitLabConnection): void {
    this.editing.set(connection);
    this.form.reset({ name: connection.name, baseUrl: connection.baseUrl, token: '' });
  }

  protected cancel(): void {
    this.editing.set(null);
    this.form.reset();
  }

  protected async save(): Promise<void> {
    const editing = this.editing();
    if (!editing && !this.form.controls.token.value.trim()) {
      this.form.controls.token.setErrors({ required: true });
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const body = { name: value.name, baseUrl: value.baseUrl, token: value.token.trim() || null, version: editing?.version };
    this.busy.set(true);
    this.failure.set(null);
    try {
      const saved = await firstValueFrom(editing ? this.api.updateConnection(editing.id, body) : this.api.createConnection(body));
      this.connections.update((list) => [...list.filter((c) => c.id !== saved.id), saved].sort((a, b) => a.name.localeCompare(b.name)));
      this.cancel();
      this.snackBar.open('Connection saved. Use Test to check it.', undefined, { duration: 3000 });
    } catch (error) {
      const apiError = asApiError(error);
      if (applyServerErrors(this.form, apiError).length || !Object.keys(apiError.fieldErrors).length) {
        this.failure.set(apiError.message);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async test(connection: GitLabConnection): Promise<void> {
    await this.run(async () => {
      const tested = await firstValueFrom(this.api.testConnection(connection.id));
      this.connections.update((list) => list.map((c) => (c.id === tested.id ? tested : c)));
    });
  }

  protected async remove(connection: GitLabConnection): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.deleteConnection(connection.id));
      this.connections.update((list) => list.filter((c) => c.id !== connection.id));
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
