import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { applyServerErrors, controlError } from '../../core/api/form-errors';

function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const password = group.get('newPassword')?.value as string;
  const confirm = group.get('confirmPassword');
  if (confirm && confirm.value && confirm.value !== password) {
    confirm.setErrors({ ...(confirm.errors ?? {}), mismatch: true });
  }
  return null;
}

/** Used for both "reset password" and "set your password" (invite) links. */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-page.scss',
  template: `
    <mat-card appearance="outlined">
      <mat-card-header><mat-card-title><h1>{{ heading }}</h1></mat-card-title></mat-card-header>
      <mat-card-content>
        @if (done()) {
          <p class="message info" role="status">Your password has been set. You can now sign in.</p>
          <a mat-flat-button routerLink="/sign-in">Sign in</a>
        } @else if (!token()) {
          <p class="message error" role="alert">This link is incomplete. Open the link from your email again.</p>
        } @else {
          <p>Use at least 12 characters. A few unrelated words make a strong, memorable password.</p>
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <mat-form-field>
              <mat-label>New password</mat-label>
              <input matInput type="password" formControlName="newPassword" autocomplete="new-password" required />
              <mat-error>{{ error('newPassword', 'Password') }}</mat-error>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Confirm new password</mat-label>
              <input matInput type="password" formControlName="confirmPassword" autocomplete="new-password" required />
              <mat-error>{{ error('confirmPassword', 'Confirmation') }}</mat-error>
            </mat-form-field>
            @if (failure(); as message) {
              <p class="message error" role="alert">{{ message }}</p>
            }
            <div class="actions">
              <a routerLink="/forgot-password">Request a new link</a>
              <button mat-flat-button type="submit" [disabled]="busy()">Set password</button>
            </div>
          </form>
        }
      </mat-card-content>
    </mat-card>
  `,
})
export class ResetPassword {
  private readonly http = inject(HttpClient);

  readonly token = input<string>();

  protected readonly heading = inject(ActivatedRoute).snapshot.data['heading'] ?? 'Choose a new password';
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );

  protected error(name: 'newPassword' | 'confirmPassword', label: string): string | null {
    return controlError(this.form.controls[name], label);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.failure.set(null);
    try {
      await firstValueFrom(
        this.http.post('/api/v1/auth/reset-password', { token: this.token(), newPassword: this.form.controls.newPassword.value }),
      );
      this.done.set(true);
    } catch (error) {
      const apiError = asApiError(error);
      const unmatched = applyServerErrors(this.form, apiError);
      this.failure.set(Object.keys(apiError.fieldErrors).length && !unmatched.length ? null : apiError.message);
    } finally {
      this.busy.set(false);
    }
  }
}
