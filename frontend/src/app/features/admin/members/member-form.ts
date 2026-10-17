import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../../core/api/api-error';
import { applyServerErrors, controlError } from '../../../core/api/form-errors';
import { AuthService } from '../../../core/auth/auth.service';
import { browserTimeZone, timeZones } from '../../../core/time-zones';
import { AdminApi } from '../admin.api';
import { Member, Role } from '../admin.models';

/** Create (route /admin/members/new) or edit (route /admin/members/:id) a member. */
@Component({
  selector: 'app-member-form',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/admin/members">← Members</a>
    <h1>{{ member() ? member()!.fullName : 'Add member' }}</h1>

    @if (!member()) {
      <p>The new member receives an email with a link to set their own password. You never see or choose it.</p>
    }

    <mat-card appearance="outlined">
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="save()" novalidate>
          <mat-form-field>
            <mat-label>Email</mat-label>
            <input matInput type="email" formControlName="email" required />
            <mat-error>{{ error('email', 'Email') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Full name</mat-label>
            <input matInput formControlName="fullName" required />
            <mat-error>{{ error('fullName', 'Full name') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Global role</mat-label>
            <mat-select formControlName="globalRoleId" required>
              @for (role of roles(); track role.id) {
                <mat-option [value]="role.id">{{ role.name }}</mat-option>
              }
            </mat-select>
            <mat-error>{{ error('globalRoleId', 'Role') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Time zone</mat-label>
            <mat-select formControlName="timeZone" required>
              @for (zone of zones; track zone) {
                <mat-option [value]="zone">{{ zone }}</mat-option>
              }
            </mat-select>
            <mat-error>{{ error('timeZone', 'Time zone') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Employee code (optional)</mat-label>
            <input matInput formControlName="employeeCode" />
            <mat-error>{{ error('employeeCode', 'Employee code') }}</mat-error>
          </mat-form-field>
          @if (failure(); as message) {
            <p class="error" role="alert">{{ message }}</p>
          }
          <div class="actions">
            <button mat-flat-button type="submit" [disabled]="busy() || !loaded()">{{ member() ? 'Save changes' : 'Create and send invite' }}</button>
          </div>
        </form>
      </mat-card-content>
    </mat-card>

    @if (member(); as m) {
      <mat-card appearance="outlined" class="danger">
        <mat-card-header><mat-card-title>Access</mat-card-title></mat-card-header>
        <mat-card-content>
          <p>Status: {{ !m.isActive ? 'Deactivated' : m.hasPassword ? 'Active' : 'Invited (password not set yet)' }}</p>
          <div class="actions">
            @if (m.isActive && auth.hasPermission('members.reset-password')) {
              <button mat-stroked-button type="button" (click)="sendReset()" [disabled]="busy()">
                {{ m.hasPassword ? 'Send password reset email' : 'Resend invite email' }}
              </button>
            }
            @if (m.isActive) {
              <button mat-stroked-button type="button" (click)="setActive(false)" [disabled]="busy()">Deactivate</button>
            } @else {
              <button mat-stroked-button type="button" (click)="setActive(true)" [disabled]="busy()">Reactivate</button>
            }
          </div>
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    mat-card {
      max-width: 640px;
      margin-bottom: 16px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .actions {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class MemberForm implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** Route parameter; absent for "new". */
  readonly id = input<string>();

  protected readonly zones = timeZones();
  protected readonly roles = signal<Role[]>([]);
  protected readonly member = signal<Member | null>(null);
  protected readonly busy = signal(false);
  /** Submit waits for the role list, so a fast click cannot hit a form that is still filling in (known issue L19). */
  protected readonly loaded = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    globalRoleId: ['', Validators.required],
    timeZone: [browserTimeZone(), Validators.required],
    employeeCode: ['', Validators.maxLength(50)],
  });

  async ngOnInit(): Promise<void> {
    try {
      this.roles.set(await firstValueFrom(this.api.roles()));
      const id = this.id();
      if (id) {
        this.show(await firstValueFrom(this.api.member(id)));
      } else {
        this.form.controls.globalRoleId.setValue(this.roles().find((r) => r.name === 'Developer')?.id ?? '');
      }
      this.loaded.set(true);
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }

  protected error(name: keyof typeof this.form.controls, label: string): string | null {
    return controlError(this.form.controls[name], label);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const body = { ...value, employeeCode: value.employeeCode.trim() || null };
    await this.run(async () => {
      const current = this.member();
      if (current) {
        this.show(await firstValueFrom(this.api.updateMember(current.id, { ...body, version: current.version })));
        this.snackBar.open('Member saved.', undefined, { duration: 3000 });
      } else {
        const created = await firstValueFrom(this.api.createMember(body));
        this.snackBar.open(`Invite sent to ${created.email}.`, undefined, { duration: 4000 });
        await this.router.navigate(['/admin/members', created.id]);
      }
    });
  }

  protected async sendReset(): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.sendPasswordReset(this.member()!.id));
      this.snackBar.open('Email sent. The member has been signed out everywhere.', undefined, { duration: 4000 });
    });
  }

  protected async setActive(active: boolean): Promise<void> {
    const id = this.member()!.id;
    await this.run(async () => {
      this.show(await firstValueFrom(active ? this.api.reactivateMember(id) : this.api.deactivateMember(id)));
    });
  }

  private show(member: Member): void {
    this.member.set(member);
    this.form.reset({
      email: member.email,
      fullName: member.fullName,
      globalRoleId: member.globalRole.id,
      timeZone: member.timeZone,
      employeeCode: member.employeeCode ?? '',
    });
    // Email is the sign-in identity; it is not editable after creation.
    this.form.controls.email.disable();
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.failure.set(null);
    try {
      await action();
    } catch (error) {
      const apiError = asApiError(error);
      const unmatched = applyServerErrors(this.form, apiError);
      if (!Object.keys(apiError.fieldErrors).length || unmatched.length) {
        this.failure.set(apiError.message);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
