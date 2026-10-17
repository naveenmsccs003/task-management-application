import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { asApiError } from '../../core/api/api-error';
import { controlError } from '../../core/api/form-errors';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-sign-in',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-page.scss',
  template: `
    <mat-card appearance="outlined">
      <mat-card-header><mat-card-title><h1>Sign in</h1></mat-card-title></mat-card-header>
      <mat-card-content>
        @if (expired()) {
          <p class="message info">Your session has ended. Please sign in again.</p>
        }
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <mat-form-field>
            <mat-label>Email</mat-label>
            <input matInput type="email" formControlName="email" autocomplete="username" required />
            <mat-error>{{ error('email', 'Email') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Password</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="current-password" required />
            <mat-error>{{ error('password', 'Password') }}</mat-error>
          </mat-form-field>
          @if (failure(); as message) {
            <p class="message error" role="alert">{{ message }}</p>
          }
          <div class="actions">
            <a routerLink="/forgot-password">Forgot password?</a>
            <button mat-flat-button type="submit" [disabled]="busy()">Sign in</button>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Query parameters bound by the router. */
  readonly returnUrl = input<string>();
  readonly expired = input<string>();

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected error(name: 'email' | 'password', label: string): string | null {
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
      const { email, password } = this.form.getRawValue();
      await this.auth.signIn(email, password);
      await this.router.navigateByUrl(this.safeReturnUrl());
    } catch (error) {
      this.failure.set(asApiError(error).message);
      this.form.controls.password.reset();
    } finally {
      this.busy.set(false);
    }
  }

  /** Only allow in-app paths, never another site (open-redirect protection). */
  private safeReturnUrl(): string {
    const url = this.returnUrl();
    return url && url.startsWith('/') && !url.startsWith('//') ? url : '/';
  }
}
