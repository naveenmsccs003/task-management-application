import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../../core/api/api-error';
import { applyServerErrors, controlError } from '../../../core/api/form-errors';
import { FILE_TYPE_CATALOG } from '../../attachments/attachments.models';
import { AdminApi } from '../admin.api';

@Component({
  selector: 'app-settings',
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Settings</h1>
    <mat-card appearance="outlined">
      <mat-card-header><mat-card-title>Sign-in and passwords</mat-card-title></mat-card-header>
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="save()" novalidate>
          @for (field of fields; track field.name) {
            <mat-form-field>
              <mat-label>{{ field.label }}</mat-label>
              <input matInput type="number" [formControlName]="field.name" [min]="field.min" [max]="field.max" required />
              <mat-hint>{{ field.min }}–{{ field.max }}</mat-hint>
              <mat-error>{{ error(field.name, field.label) }}</mat-error>
            </mat-form-field>
          }
          @if (failure(); as message) {
            <p class="error" role="alert">{{ message }}</p>
          }
          <button mat-flat-button type="submit" [disabled]="busy()">Save</button>
        </form>
      </mat-card-content>
    </mat-card>

    <mat-card appearance="outlined">
      <mat-card-header><mat-card-title>Work sessions</mat-card-title></mat-card-header>
      <mat-card-content>
        <form [formGroup]="workForm" (ngSubmit)="saveWork()" novalidate>
          <mat-form-field>
            <mat-label>Maximum session length (hours)</mat-label>
            <input matInput type="number" formControlName="maxSessionHours" min="1" max="24" required />
            <mat-hint>Longer sessions are closed automatically. 1–24</mat-hint>
            <mat-error>{{ workError('maxSessionHours', 'Maximum session length') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Allocated-hours warning (%)</mat-label>
            <input matInput type="number" formControlName="allocationWarningPercent" min="50" max="99" required />
            <mat-hint>50–99</mat-hint>
            <mat-error>{{ workError('allocationWarningPercent', 'Warning threshold') }}</mat-error>
          </mat-form-field>
          <mat-checkbox formControlName="pauseReasonRequired">Require a reason when pausing work</mat-checkbox>
          @if (workFailure(); as message) {
            <p class="error" role="alert">{{ message }}</p>
          }
          <button mat-flat-button type="submit" [disabled]="busy()">Save</button>
        </form>
      </mat-card-content>
    </mat-card>

    <mat-card appearance="outlined">
      <mat-card-header><mat-card-title>Attachments</mat-card-title></mat-card-header>
      <mat-card-content>
        <form [formGroup]="fileForm" (ngSubmit)="saveFiles()" novalidate>
          @for (field of sizeFields; track field.name) {
            <mat-form-field>
              <mat-label>{{ field.label }}</mat-label>
              <input matInput type="number" [formControlName]="field.name" min="1" [max]="field.max" required />
              <mat-hint>1–{{ field.max }}</mat-hint>
              <mat-error>{{ fileError(field.name, field.label) }}</mat-error>
            </mat-form-field>
          }
          <fieldset>
            <legend>Allowed file types</legend>
            @for (type of fileTypes; track type.code) {
              <mat-checkbox [checked]="allowed().has(type.code)" (change)="toggleType(type.code, $event.checked)">{{ type.label }}</mat-checkbox>
            }
            <p class="hint">SVG and HTML files are never allowed. Files are checked by their content, not their name.</p>
          </fieldset>
          @if (fileFailure(); as message) {
            <p class="error" role="alert">{{ message }}</p>
          }
          <button mat-flat-button type="submit" [disabled]="busy()">Save</button>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card {
      max-width: 560px;
      margin-bottom: 16px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    button {
      align-self: flex-start;
    }
    fieldset {
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 4px;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
    }
    .hint {
      grid-column: 1 / -1;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class Settings implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly fields = [
    { name: 'passwordMinLength', label: 'Minimum password length', min: 8, max: 128 },
    { name: 'maxFailedSignInAttempts', label: 'Failed sign-ins before lockout', min: 3, max: 20 },
    { name: 'lockoutMinutes', label: 'Lockout duration (minutes)', min: 1, max: 1440 },
    { name: 'resetTokenMinutes', label: 'Password reset link lifetime (minutes)', min: 5, max: 1440 },
    { name: 'inviteTokenHours', label: 'Invite link lifetime (hours)', min: 1, max: 720 },
  ] as const;

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    passwordMinLength: [12, [Validators.required, Validators.min(8), Validators.max(128)]],
    maxFailedSignInAttempts: [5, [Validators.required, Validators.min(3), Validators.max(20)]],
    lockoutMinutes: [15, [Validators.required, Validators.min(1), Validators.max(1440)]],
    resetTokenMinutes: [30, [Validators.required, Validators.min(5), Validators.max(1440)]],
    inviteTokenHours: [72, [Validators.required, Validators.min(1), Validators.max(720)]],
  });

  protected readonly workFailure = signal<string | null>(null);
  protected readonly workForm = inject(NonNullableFormBuilder).group({
    maxSessionHours: [10, [Validators.required, Validators.min(1), Validators.max(24)]],
    allocationWarningPercent: [80, [Validators.required, Validators.min(50), Validators.max(99)]],
    pauseReasonRequired: [false],
  });

  protected readonly fileTypes = FILE_TYPE_CATALOG;
  protected readonly sizeFields = [
    { name: 'imageMaxMb', label: 'Largest image (MB)', max: 100 },
    { name: 'documentMaxMb', label: 'Largest document (MB)', max: 500 },
    { name: 'videoMaxMb', label: 'Largest video (MB)', max: 1024 },
  ] as const;
  protected readonly fileFailure = signal<string | null>(null);
  protected readonly allowed = signal(new Set<string>());
  protected readonly fileForm = inject(NonNullableFormBuilder).group({
    imageMaxMb: [10, [Validators.required, Validators.min(1), Validators.max(100)]],
    documentMaxMb: [25, [Validators.required, Validators.min(1), Validators.max(500)]],
    videoMaxMb: [200, [Validators.required, Validators.min(1), Validators.max(1024)]],
  });

  async ngOnInit(): Promise<void> {
    try {
      const settings = await firstValueFrom(this.api.settings());
      this.form.reset(settings.auth);
      this.workForm.reset(settings.work);
      this.fileForm.reset(settings.attachments);
      this.allowed.set(new Set(settings.attachments.allowedTypes));
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }

  protected error(name: keyof typeof this.form.controls, label: string): string | null {
    return controlError(this.form.controls[name], label);
  }

  protected workError(name: keyof typeof this.workForm.controls, label: string): string | null {
    return controlError(this.workForm.controls[name], label);
  }

  protected fileError(name: keyof typeof this.fileForm.controls, label: string): string | null {
    return controlError(this.fileForm.controls[name], label);
  }

  protected toggleType(code: string, checked: boolean): void {
    this.allowed.update((set) => {
      const next = new Set(set);
      if (checked) next.add(code);
      else next.delete(code);
      return next;
    });
  }

  protected async saveFiles(): Promise<void> {
    if (this.fileForm.invalid) {
      this.fileForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.fileFailure.set(null);
    try {
      const saved = await firstValueFrom(
        this.api.updateAttachmentSettings({ ...this.fileForm.getRawValue(), allowedTypes: this.fileTypes.map((t) => t.code).filter((c) => this.allowed().has(c)) }),
      );
      this.fileForm.reset(saved);
      this.allowed.set(new Set(saved.allowedTypes));
      this.snackBar.open('Settings saved.', undefined, { duration: 3000 });
    } catch (error) {
      const apiError = asApiError(error);
      if (applyServerErrors(this.fileForm, apiError).length || !Object.keys(apiError.fieldErrors).length) {
        this.fileFailure.set(apiError.message);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveWork(): Promise<void> {
    if (this.workForm.invalid) {
      this.workForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.workFailure.set(null);
    try {
      this.workForm.reset(await firstValueFrom(this.api.updateWorkSettings(this.workForm.getRawValue())));
      this.snackBar.open('Settings saved.', undefined, { duration: 3000 });
    } catch (error) {
      const apiError = asApiError(error);
      if (applyServerErrors(this.workForm, apiError).length || !Object.keys(apiError.fieldErrors).length) {
        this.workFailure.set(apiError.message);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.failure.set(null);
    try {
      this.form.reset(await firstValueFrom(this.api.updateAuthSettings(this.form.getRawValue())));
      this.snackBar.open('Settings saved.', undefined, { duration: 3000 });
    } catch (error) {
      const apiError = asApiError(error);
      if (applyServerErrors(this.form, apiError).length || !Object.keys(apiError.fieldErrors).length) {
        this.failure.set(apiError.message);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
