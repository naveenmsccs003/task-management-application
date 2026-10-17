import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { TasksApi } from './tasks.api';
import { ChecklistItem, Task } from './tasks.models';

/** The task's checklist: the assignee ticks items (with an optional note); TL/PM add task-specific items (FR-CHK-03/04). */
@Component({
  selector: 'app-task-checklist',
  imports: [DatePipe, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Checklist</h2>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (items().length) {
      <p class="muted">{{ mandatoryDone() }} of {{ mandatoryTotal() }} required items done.</p>
    }
    <ul class="items">
      @for (item of items(); track item.id) {
        <li>
          <mat-checkbox
            [checked]="item.isCompleted"
            [disabled]="!canTick() || busy()"
            (change)="toggle(item, $event.checked)"
          >
            {{ item.text }}
            @if (item.isMandatory) {
              <span class="required">required</span>
            }
          </mat-checkbox>
          @if (item.isCompleted) {
            <div class="muted done">
              Ticked by {{ item.completedBy?.name }} · {{ item.completedAt | date: 'medium' }}
              @if (item.note) {
                — “{{ item.note }}”
              }
            </div>
          }
        </li>
      } @empty {
        <li class="muted">No checklist items.</li>
      }
    </ul>
    @if (canTick()) {
      <mat-form-field class="full" subscriptSizing="dynamic">
        <mat-label>Note for the next item you tick (optional)</mat-label>
        <input matInput name="note" [(ngModel)]="note" maxlength="1000" />
      </mat-form-field>
    }

    @if (canManage()) {
      <form class="add" (ngSubmit)="add()" aria-label="Add a checklist item">
        <mat-form-field>
          <mat-label>New checklist item</mat-label>
          <input matInput name="text" [(ngModel)]="newText" maxlength="500" />
        </mat-form-field>
        <mat-checkbox name="mandatory" [(ngModel)]="newMandatory">Required</mat-checkbox>
        <button mat-stroked-button type="submit" [disabled]="busy() || !newText.trim()">Add item</button>
      </form>
    }
  `,
  styles: `
    .items {
      list-style: none;
      padding: 0;
    }
    .items li {
      margin-bottom: 4px;
    }
    .required {
      margin-left: 6px;
      padding: 0 6px;
      border-radius: 8px;
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
      font: var(--mat-sys-label-small);
    }
    .done {
      margin-left: 40px;
    }
    .add {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    .full {
      width: 100%;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class TaskChecklist implements OnInit {
  private readonly api = inject(TasksApi);

  readonly task = input.required<Task>();
  /** Emitted after a change so the page can refresh the checklist summary. */
  readonly changed = output<void>();

  protected readonly items = signal<ChecklistItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly canTick = computed(() => this.task().allowedActions.includes('completeChecklist'));
  protected readonly canManage = computed(() => this.task().allowedActions.includes('manageChecklist'));
  protected readonly mandatoryTotal = computed(() => this.items().filter((i) => i.isMandatory).length);
  protected readonly mandatoryDone = computed(() => this.items().filter((i) => i.isMandatory && i.isCompleted).length);
  protected note = '';
  protected newText = '';
  protected newMandatory = true;

  async ngOnInit(): Promise<void> {
    await this.run(async () => this.items.set(await firstValueFrom(this.api.checklist(this.task().id))));
  }

  protected async toggle(item: ChecklistItem, completed: boolean): Promise<void> {
    await this.run(async () => {
      this.items.set(await firstValueFrom(this.api.setChecklistItem(this.task().id, item.id, completed, this.note.trim())));
      this.note = '';
      this.changed.emit();
    });
    if (this.failure()) {
      // Show the saved state again after a refused change.
      this.items.set(await firstValueFrom(this.api.checklist(this.task().id)));
    }
  }

  protected async add(): Promise<void> {
    await this.run(async () => {
      this.items.set(await firstValueFrom(this.api.addChecklistItem(this.task().id, this.newText, this.newMandatory)));
      this.newText = '';
      this.changed.emit();
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
