import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { controlError } from '../../core/api/form-errors';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-page.scss',
  template: `
    <mat-card appearance="outlined">
      <mat-card-header><mat-card-title><h1>Reset your password</h1></mat-card-title></mat-card-header>
      <mat-card-content>
        @if (sent()) {
          <p class="message info" role="status">
            If an account exists for that email, we have sent a link to reset the password. The link expires soon and
            can be used once.
          </p>
          <a routerLink="/sign-in">Back to sign in</a>
        } @else {
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <mat-form-field>
              <mat-label>Email</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="username" required />
              <mat-error>{{ emailError() }}</mat-error>
            </mat-form-field>
            @if (failure(); as message) {
              <p class="message error" role="alert">{{ message }}</p>
            }
            <div class="actions">
              <a routerLink="/sign-in">Back to sign in</a>
              <button mat-flat-button type="submit" [disabled]="busy()">Send link</button>
            </div>
          </form>
        }
      </mat-card-content>
    </mat-card>
  `,
})
export class ForgotPassword {
  private readonly http = inject(HttpClient);

  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({ email: ['', [Validators.required, Validators.email]] });

  protected emailError(): string | null {
    return controlError(this.form.controls.email, 'Email');
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.failure.set(null);
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/forgot-password', this.form.getRawValue()));
      this.sent.set(true);
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
