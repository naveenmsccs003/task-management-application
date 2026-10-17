import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { applyServerErrors, controlError } from '../../core/api/form-errors';
import { ProjectsApi } from '../projects/projects.api';
import { Project } from '../projects/projects.models';
import { TasksApi } from './tasks.api';
import { SaveTask, Task, TASK_PRIORITIES, TASK_TYPES } from './tasks.models';

/** Create (route /projects/:projectId/tasks/new) or edit (route /tasks/:id/edit) a task. */
@Component({
  selector: 'app-task-form',
  imports: [
    ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule,
    MatInputModule, MatSelectModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (task(); as t) {
      <a [routerLink]="['/tasks', t.id]">← {{ t.key }}</a>
      <h1>Edit {{ t.key }}</h1>
    } @else if (project(); as p) {
      <a [routerLink]="['/projects', p.id]">← {{ p.name }}</a>
      <h1>New task in {{ p.key }}</h1>
    }

    <mat-card appearance="outlined">
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="save()" novalidate>
          <mat-form-field>
            <mat-label>Title</mat-label>
            <input matInput formControlName="title" required />
            <mat-error>{{ error('title', 'Title') }}</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Description</mat-label>
            <textarea matInput formControlName="description" rows="6"></textarea>
            <mat-hint>Markdown is allowed.</mat-hint>
            <mat-error>{{ error('description', 'Description') }}</mat-error>
          </mat-form-field>
          <div class="row">
            <mat-form-field>
              <mat-label>Type</mat-label>
              <mat-select formControlName="type">
                @for (t of types; track t) {
                  <mat-option [value]="t">{{ t }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Priority</mat-label>
              <mat-select formControlName="priority">
                @for (p of priorities; track p) {
                  <mat-option [value]="p">{{ p }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          </div>
          <div class="row">
            <mat-form-field>
              <mat-label>Start date</mat-label>
              <input matInput type="date" formControlName="startDate" />
            </mat-form-field>
            <mat-form-field>
              <mat-label>Due date</mat-label>
              <input matInput type="date" formControlName="dueDate" />
              @if (form.controls.dueDate.disabled) {
                <mat-hint>Fixed after assignment; use a deadline extension.</mat-hint>
              } @else {
                <mat-hint>Required before the task can be assigned.</mat-hint>
              }
              <mat-error>{{ error('dueDate', 'Due date') }}</mat-error>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Allocated hours</mat-label>
              <input matInput type="number" min="0" step="0.25" formControlName="allocatedHours" />
              <mat-hint>Leave empty if not estimated.</mat-hint>
              <mat-error>{{ error('allocatedHours', 'Allocated hours') }}</mat-error>
            </mat-form-field>
          </div>
          <mat-form-field>
            <mat-label>Labels</mat-label>
            <input matInput formControlName="labels" />
            <mat-hint>Separate with commas, e.g. backend, login</mat-hint>
            <mat-error>{{ error('labels', 'Labels') }}</mat-error>
          </mat-form-field>
          <mat-checkbox formControlName="algorithmRequired">An approved algorithm is required before work starts</mat-checkbox>
          @if (failure(); as message) {
            <p class="error" role="alert">{{ message }}</p>
          }
          <div class="actions">
            <button mat-flat-button type="submit" [disabled]="busy()">{{ task() ? 'Save changes' : 'Create task' }}</button>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card {
      max-width: 820px;
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
export class TaskForm implements OnInit {
  private readonly tasksApi = inject(TasksApi);
  private readonly projectsApi = inject(ProjectsApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** Route parameters: projectId when creating, id when editing. */
  readonly projectId = input<string>();
  readonly id = input<string>();

  protected readonly types = TASK_TYPES;
  protected readonly priorities = TASK_PRIORITIES;
  protected readonly project = signal<Project | null>(null);
  protected readonly task = signal<Task | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    title: ['', [Validators.required, Validators.maxLength(300)]],
    description: [''],
    type: ['Feature' as SaveTask['type']],
    priority: ['Medium' as SaveTask['priority']],
    startDate: [''],
    dueDate: [''],
    allocatedHours: [null as number | null, [Validators.min(0), Validators.max(10000)]],
    labels: [''],
    algorithmRequired: [false],
  });

  async ngOnInit(): Promise<void> {
    try {
      const id = this.id();
      if (id) {
        this.show(await firstValueFrom(this.tasksApi.get(id)));
      } else {
        const project = await firstValueFrom(this.projectsApi.get(this.projectId()!));
        this.project.set(project);
        this.form.controls.algorithmRequired.setValue(project.algorithmRequiredDefault);
      }
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
    const body: SaveTask = {
      title: value.title,
      description: value.description,
      type: value.type,
      priority: value.priority,
      startDate: value.startDate || null,
      dueDate: value.dueDate || null,
      allocatedMinutes: value.allocatedHours ? Math.round(value.allocatedHours * 60) : null,
      algorithmRequired: value.algorithmRequired,
      labels: value.labels.split(',').map((l) => l.trim()).filter((l) => l),
    };
    this.busy.set(true);
    this.failure.set(null);
    try {
      const current = this.task();
      const saved = current
        ? await firstValueFrom(this.tasksApi.update(current.id, { ...body, version: current.version }))
        : await firstValueFrom(this.tasksApi.create(this.project()!.id, body));
      this.snackBar.open(current ? 'Task saved.' : `Task ${saved.key} created.`, undefined, { duration: 3000 });
      await this.router.navigate(['/tasks', saved.id]);
    } catch (error) {
      const apiError = asApiError(error);
      // The server reports allocated time in minutes; show it on the hours field.
      const mapped = { ...apiError, fieldErrors: renameKey(apiError.fieldErrors, 'allocatedMinutes', 'allocatedHours') };
      const unmatched = applyServerErrors(this.form, mapped);
      if (!Object.keys(apiError.fieldErrors).length || unmatched.length) {
        this.failure.set(apiError.message);
      }
    } finally {
      this.busy.set(false);
    }
  }

  private show(task: Task): void {
    this.task.set(task);
    this.form.reset({
      title: task.title,
      description: task.description,
      type: task.type,
      priority: task.priority,
      startDate: task.startDate ?? '',
      dueDate: task.dueDate ?? '',
      allocatedHours: task.allocatedMinutes ? task.allocatedMinutes / 60 : null,
      labels: task.labels.join(', '),
      algorithmRequired: task.algorithmRequired,
    });
    if (task.status !== 'Draft') {
      this.form.controls.dueDate.disable();
    }
    if (!['Draft', 'AwaitingAlgorithm', 'ReadyToStart'].includes(task.status)) {
      this.form.controls.algorithmRequired.disable();
    }
  }
}

function renameKey(errors: Record<string, string[]>, from: string, to: string): Record<string, string[]> {
  if (!(from in errors)) return errors;
  const { [from]: messages, ...rest } = errors;
  return { ...rest, [to]: messages };
}
