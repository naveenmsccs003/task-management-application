import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { applyServerErrors, controlError } from '../../core/api/form-errors';
import { ProjectsApi } from './projects.api';
import { Project } from './projects.models';

/** Create (route /projects/new) or edit (route /projects/:id/edit) a project. */
@Component({
  selector: 'app-project-form',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (project(); as p) {
      <a [routerLink]="['/projects', p.id]">← {{ p.name }}</a>
      <h1>Edit project</h1>
    } @else {
      <a routerLink="/projects">← Projects</a>
      <h1>New project</h1>
    }

    <mat-card appearance="outlined">
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="save()" novalidate>
          <mat-form-field>
            <mat-label>Key</mat-label>
            <input matInput formControlName="key" required maxlength="10" />
            <mat-hint>2–10 letters or digits, e.g. TMA. Task keys start with it (TMA-1) and it cannot change later.</mat-hint>
            <mat-error>{{ error('key', 'Key') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" required />
            <mat-error>{{ error('name', 'Name') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Description</mat-label>
            <textarea matInput formControlName="description" rows="4"></textarea>
            <mat-error>{{ error('description', 'Description') }}</mat-error>
          </mat-form-field>
          <div class="row">
            <mat-form-field>
              <mat-label>Start date</mat-label>
              <input matInput type="date" formControlName="startDate" />
            </mat-form-field>
            <mat-form-field>
              <mat-label>Target end date</mat-label>
              <input matInput type="date" formControlName="targetEndDate" />
              <mat-error>{{ error('targetEndDate', 'Target end date') }}</mat-error>
            </mat-form-field>
          </div>
          <mat-checkbox formControlName="algorithmRequiredDefault">New tasks require an approved algorithm by default</mat-checkbox>
          <mat-checkbox formControlName="closeOnApproval">Close tasks as soon as the Team Leader approves them</mat-checkbox>
          @if (failure(); as message) {
            <p class="error" role="alert">{{ message }}</p>
          }
          <div class="actions">
            <button mat-flat-button type="submit" [disabled]="busy()">{{ project() ? 'Save changes' : 'Create project' }}</button>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card {
      max-width: 720px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .row {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
    }
    .actions {
      margin-top: 8px;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class ProjectForm implements OnInit {
  private readonly api = inject(ProjectsApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** Route parameter; absent for "new". */
  readonly id = input<string>();

  protected readonly project = signal<Project | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    key: ['', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9]{1,9}$/)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    startDate: [''],
    targetEndDate: [''],
    algorithmRequiredDefault: [false],
    closeOnApproval: [false],
  });

  async ngOnInit(): Promise<void> {
    const id = this.id();
    if (!id) return;
    try {
      const project = await firstValueFrom(this.api.get(id));
      this.project.set(project);
      this.form.reset({
        key: project.key,
        name: project.name,
        description: project.description,
        startDate: project.startDate ?? '',
        targetEndDate: project.targetEndDate ?? '',
        algorithmRequiredDefault: project.algorithmRequiredDefault,
        closeOnApproval: project.closeOnApproval,
      });
      this.form.controls.key.disable();
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }

  protected error(name: keyof typeof this.form.controls, label: string): string | null {
    const control = this.form.controls[name];
    if (control.errors?.['pattern']) return 'Use 2–10 letters or digits, starting with a letter.';
    return controlError(control, label);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const body = {
      ...value,
      key: value.key.toUpperCase(),
      startDate: value.startDate || null,
      targetEndDate: value.targetEndDate || null,
    };
    this.busy.set(true);
    this.failure.set(null);
    try {
      const current = this.project();
      const saved = current
        ? await firstValueFrom(this.api.update(current.id, { ...body, version: current.version }))
        : await firstValueFrom(this.api.create(body));
      this.snackBar.open(current ? 'Project saved.' : `Project ${saved.key} created.`, undefined, { duration: 3000 });
      await this.router.navigate(['/projects', saved.id]);
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
