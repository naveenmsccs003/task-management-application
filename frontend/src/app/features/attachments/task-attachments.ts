import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { AttachmentList } from './attachment-list';
import { AttachmentPicker } from './attachment-picker';
import { TaskAttachmentsStore } from './task-attachments.store';

/** The files attached to the task itself (FR-ATT-01). Comment, algorithm and review files are shown with those records. */
@Component({
  selector: 'app-task-attachments',
  imports: [AttachmentList, AttachmentPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Files</h2>
    <app-attachment-list [attachments]="files()" label="Task files" />
    @if (!files().length) {
      <p class="muted">No files attached to the task.</p>
    }
    @if (canAttach()) {
      <app-attachment-picker [taskId]="taskId()" ownerType="Task" label="Attach a file to the task" />
    }
  `,
  styles: `
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class TaskAttachments {
  private readonly store = inject(TaskAttachmentsStore);

  readonly taskId = input.required<string>();
  readonly canAttach = input(false);

  protected readonly files = computed(() => this.store.attachments().filter((a) => a.ownerType === 'Task'));
}
