import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { ChecklistsApi } from './checklists.api';
import { ChecklistTemplate } from './checklists.models';

type ItemForm = FormGroup<{ text: FormControl<string>; isMandatory: FormControl<boolean> }>;

/**
 * Checklist templates (FR-CHK-01). Route /checklist-templates manages global templates; the project page embeds this
 * with a project id to manage that project's templates (global ones are listed there read-only).
 */
@Component({
  selector: 'app-checklist-templates',
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!projectId()) {
      <h1>Checklist templates</h1>
      <p>Global templates can be used in every project. Changing a template never changes checklists already copied to tasks.</p>
    }
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    <div class="layout">
      <ul class="list" aria-label="Templates">
        @for (t of templates(); track t.id) {
          <li [class.selected]="editing()?.id === t.id">
            <button mat-button type="button" (click)="edit(t)">
              {{ t.name }}
              @if (!t.projectId && projectId()) {
                <span class="tag">global</span>
              }
              @if (!t.isActive) {
                <span class="tag">inactive</span>
              }
            </button>
            <span class="muted">{{ t.items.length }} items</span>
          </li>
        } @empty {
          <li class="muted">No templates yet.</li>
        }
        <li><button mat-stroked-button type="button" (click)="startNew()">New template</button></li>
      </ul>

      @if (showForm()) {
        <mat-card appearance="outlined">
          <mat-card-content>
            <form [formGroup]="form" (ngSubmit)="save()" novalidate>
              <mat-form-field>
                <mat-label>Template name</mat-label>
                <input matInput formControlName="name" required maxlength="200" />
              </mat-form-field>
              <mat-checkbox formControlName="isActive">Active (can be applied to new tasks)</mat-checkbox>
              <h3>Items</h3>
              <ol formArrayName="items" class="items">
                @for (item of items.controls; track item; let i = $index) {
                  <li [formGroupName]="i">
                    <mat-form-field subscriptSizing="dynamic">
                      <mat-label>Item {{ i + 1 }}</mat-label>
                      <input matInput formControlName="text" required maxlength="500" />
                    </mat-form-field>
                    <mat-checkbox formControlName="isMandatory">Required</mat-checkbox>
                    <button mat-button type="button" (click)="removeItem(i)" [attr.aria-label]="'Remove item ' + (i + 1)">Remove</button>
                  </li>
                }
              </ol>
              <button mat-stroked-button type="button" (click)="addItem()">Add item</button>
              @if (readOnly()) {
                <p class="muted">You can view this template but not change it.</p>
              }
              <div class="actions">
                <button mat-flat-button type="submit" [disabled]="busy() || readOnly()">{{ editing() ? 'Save template' : 'Create template' }}</button>
                <button mat-button type="button" (click)="close()">Close</button>
              </div>
            </form>
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: `
    .layout {
      display: grid;
      grid-template-columns: minmax(220px, 1fr) 2fr;
      gap: 24px;
      align-items: start;
    }
    @media (max-width: 720px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
    .list {
      list-style: none;
      padding: 0;
    }
    .list li.selected {
      background: var(--mat-sys-secondary-container);
      border-radius: 8px;
    }
    .tag {
      margin-left: 6px;
      padding: 0 6px;
      border-radius: 8px;
      background: var(--mat-sys-surface-variant);
      font: var(--mat-sys-label-small);
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .items li {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: center;
      margin-bottom: 8px;
    }
    .items mat-form-field {
      flex: 1;
      min-width: 220px;
    }
    .actions {
      display: flex;
      gap: 12px;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class ChecklistTemplates implements OnInit {
  private readonly api = inject(ChecklistsApi);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(NonNullableFormBuilder);

  /** Set when embedded in a project page. */
  readonly projectId = input<string>();

  protected readonly templates = signal<ChecklistTemplate[]>([]);
  protected readonly editing = signal<ChecklistTemplate | null>(null);
  protected readonly showForm = signal(false);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly readOnly = computed(() => !!this.editing() && !this.editing()!.canEdit);
  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    isActive: [true],
    items: this.fb.array<ItemForm>([]),
  });

  protected get items(): FormArray<ItemForm> {
    return this.form.controls.items;
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected startNew(): void {
    this.editing.set(null);
    this.form.reset({ name: '', isActive: true });
    this.items.clear();
    this.addItem();
    this.form.enable();
    this.showForm.set(true);
  }

  protected edit(template: ChecklistTemplate): void {
    this.editing.set(template);
    this.form.reset({ name: template.name, isActive: template.isActive });
    this.items.clear();
    for (const item of template.items) {
      this.items.push(this.newItem(item.text, item.isMandatory));
    }
    if (template.canEdit) this.form.enable();
    else this.form.disable();
    this.showForm.set(true);
  }

  protected addItem(): void {
    this.items.push(this.newItem('', true));
  }

  protected removeItem(index: number): void {
    this.items.removeAt(index);
  }

  protected close(): void {
    this.showForm.set(false);
    this.editing.set(null);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid || !this.items.length) {
      this.form.markAllAsTouched();
      this.failure.set(this.items.length ? null : 'Add at least one item.');
      return;
    }
    const value = this.form.getRawValue();
    const current = this.editing();
    const body = {
      projectId: current ? current.projectId : (this.projectId() ?? null),
      name: value.name,
      isActive: value.isActive,
      items: value.items,
      version: current?.version ?? 0,
    };
    this.busy.set(true);
    this.failure.set(null);
    try {
      const saved = current ? await firstValueFrom(this.api.update(current.id, body)) : await firstValueFrom(this.api.create(body));
      this.snackBar.open(`Template “${saved.name}” saved.`, undefined, { duration: 3000 });
      await this.load();
      this.edit(this.templates().find((t) => t.id === saved.id) ?? saved);
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  private newItem(text: string, isMandatory: boolean): ItemForm {
    return this.fb.group({ text: [text, [Validators.required, Validators.maxLength(500)]], isMandatory: [isMandatory] });
  }

  private async load(): Promise<void> {
    try {
      this.templates.set(await firstValueFrom(this.api.list(this.projectId())));
    } catch (error) {
      this.failure.set(asApiError(error).message);
    }
  }
}
