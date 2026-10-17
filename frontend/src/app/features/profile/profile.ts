import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { applyServerErrors, controlError } from '../../core/api/form-errors';
import { Me } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { timeZones } from '../../core/time-zones';

@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>My profile</h1>
    <div class="cards">
      <mat-card appearance="outlined">
        <mat-card-header><mat-card-title>Details</mat-card-title></mat-card-header>
        <mat-card-content>
          <p>
            {{ auth.me()?.email }} · {{ auth.me()?.globalRole?.name }}
          </p>
          <form [formGroup]="profile" (ngSubmit)="saveProfile()" novalidate>
            <mat-form-field>
              <mat-label>Full name</mat-label>
              <input matInput formControlName="fullName" autocomplete="name" required />
              <mat-error>{{ error(profile.controls.fullName, 'Full name') }}</mat-error>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Time zone</mat-label>
              <mat-select formControlName="timeZone">
                @for (zone of zones; track zone) {
                  <mat-option [value]="zone">{{ zone }}</mat-option>
                }
              </mat-select>
              <mat-error>{{ error(profile.controls.timeZone, 'Time zone') }}</mat-error>
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="busy()">Save</button>
          </form>
        </mat-card-content>
      </mat-card>

      <mat-card appearance="outlined">
        <mat-card-header><mat-card-title>Change password</mat-card-title></mat-card-header>
        <mat-card-content>
          <p>Changing your password signs you out on all other devices.</p>
          <form [formGroup]="password" (ngSubmit)="changePassword()" novalidate>
            <mat-form-field>
              <mat-label>Current password</mat-label>
              <input matInput type="password" formControlName="currentPassword" autocomplete="current-password" required />
              <mat-error>{{ error(password.controls.currentPassword, 'Current password') }}</mat-error>
            </mat-form-field>
            <mat-form-field>
              <mat-label>New password</mat-label>
              <input matInput type="password" formControlName="newPassword" autocomplete="new-password" required />
              <mat-error>{{ error(password.controls.newPassword, 'New password') }}</mat-error>
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="busy()">Change password</button>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .cards {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
      gap: 16px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 8px;
      align-items: stretch;
    }
    button {
      align-self: flex-start;
    }
  `,
})
export class Profile {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly zones = timeZones();
  protected readonly busy = signal(false);
  protected readonly profile = this.fb.group({
    fullName: [this.auth.me()?.fullName ?? '', [Validators.required, Validators.maxLength(200)]],
    timeZone: [this.auth.me()?.timeZone ?? 'UTC', Validators.required],
  });
  protected readonly password = this.fb.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(12)]],
  });

  protected readonly error = controlError;

  protected async saveProfile(): Promise<void> {
    if (this.profile.invalid) {
      this.profile.markAllAsTouched();
      return;
    }
    await this.run(async () => {
      const me = await firstValueFrom(this.http.patch<Me>('/api/v1/me', this.profile.getRawValue()));
      this.auth.setMe(me);
      this.snackBar.open('Profile saved.', undefined, { duration: 3000 });
    }, this.profile);
  }

  protected async changePassword(): Promise<void> {
    if (this.password.invalid) {
      this.password.markAllAsTouched();
      return;
    }
    await this.run(async () => {
      await firstValueFrom(this.http.post('/api/v1/auth/change-password', this.password.getRawValue()));
      this.password.reset();
      this.snackBar.open('Password changed. Other sessions have been signed out.', undefined, { duration: 4000 });
    }, this.password);
  }

  private async run(action: () => Promise<void>, form: typeof this.profile | typeof this.password): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      const apiError = asApiError(error);
      const unmatched = applyServerErrors(form, apiError);
      if (!Object.keys(apiError.fieldErrors).length || unmatched.length) {
        this.snackBar.open(apiError.message, 'Close');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
