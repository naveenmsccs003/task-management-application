import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { AttachmentList } from '../attachments/attachment-list';
import { AttachmentPicker } from '../attachments/attachment-picker';
import { Attachment } from '../attachments/attachments.models';
import { TaskAttachmentsStore } from '../attachments/task-attachments.store';
import { TasksApi } from './tasks.api';
import { TaskComment } from './tasks.models';

/** Comments on a task (FR-TASK-08). Edit and delete buttons follow the server's canEdit / canDelete flags. */
@Component({
  selector: 'app-task-comments',
  imports: [AttachmentList, AttachmentPicker, DatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Comments</h2>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    <ol class="comments">
      @for (c of comments(); track c.id) {
        <li>
          <div class="meta">
            <strong>{{ c.author?.name ?? 'Unknown' }}</strong>
            <span>{{ c.createdAt | date: 'medium' }}</span>
            @if (c.editedAt) {
              <span>(edited)</span>
            }
          </div>
          @if (c.isDeleted) {
            <p class="deleted">This comment was deleted.</p>
          } @else if (editingId() === c.id) {
            <form (ngSubmit)="saveEdit(c)">
              <mat-form-field class="full">
                <mat-label>Edit comment</mat-label>
                <textarea matInput name="editBody" [(ngModel)]="editBody" rows="3" required></textarea>
              </mat-form-field>
              <button mat-flat-button type="submit" [disabled]="busy() || !editBody.trim()">Save</button>
              <button mat-button type="button" (click)="editingId.set(null)">Cancel</button>
            </form>
          } @else {
            <p class="body">{{ c.body }}</p>
            <app-attachment-list [attachments]="filesOf(c.id)" label="Comment files" />
            <div class="row-actions">
              @if (c.canEdit) {
                <button mat-button type="button" (click)="startEdit(c)">Edit</button>
              }
              @if (c.canDelete) {
                <button mat-button type="button" (click)="remove(c)" [disabled]="busy()">Delete</button>
              }
            </div>
          }
        </li>
      } @empty {
        <li class="empty">No comments yet.</li>
      }
    </ol>

    @if (canComment()) {
      <form (ngSubmit)="add()">
        <mat-form-field class="full">
          <mat-label>Add a comment</mat-label>
          <textarea matInput name="newBody" [(ngModel)]="newBody" rows="3" maxlength="10000"></textarea>
        </mat-form-field>
        <app-attachment-picker [taskId]="taskId()" ownerType="Comment" label="Attach a file to the comment" [(drafts)]="drafts" />
        <button mat-flat-button type="submit" [disabled]="busy() || !newBody.trim()">Post comment</button>
      </form>
    }
  `,
  styles: `
    .comments {
      list-style: none;
      padding: 0;
    }
    .comments li {
      padding: 8px 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .meta {
      display: flex;
      gap: 8px;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
    .body {
      white-space: pre-wrap;
      margin: 4px 0;
    }
    .deleted,
    .empty {
      color: var(--mat-sys-on-surface-variant);
      font-style: italic;
    }
    .full {
      width: 100%;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class TaskComments implements OnInit {
  private readonly api = inject(TasksApi);
  private readonly files = inject(TaskAttachmentsStore, { optional: true });

  readonly taskId = input.required<string>();
  readonly canComment = input(false);

  protected readonly comments = signal<TaskComment[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly drafts = signal<Attachment[]>([]);
  protected newBody = '';
  protected editBody = '';

  async ngOnInit(): Promise<void> {
    await this.run(async () => this.comments.set(await firstValueFrom(this.api.comments(this.taskId()))));
  }

  protected filesOf(commentId: string): Attachment[] {
    return this.files?.forOwner('Comment', commentId) ?? [];
  }

  protected startEdit(comment: TaskComment): void {
    this.editBody = comment.body ?? '';
    this.editingId.set(comment.id);
  }

  protected async add(): Promise<void> {
    await this.run(async () => {
      const ids = this.drafts().map((d) => d.id);
      const created = await firstValueFrom(this.api.addComment(this.taskId(), this.newBody, ids));
      this.comments.update((list) => [...list, created]);
      this.newBody = '';
      this.drafts.set([]);
      if (ids.length) await this.files?.changed();
    });
  }

  protected async saveEdit(comment: TaskComment): Promise<void> {
    await this.run(async () => {
      const saved = await firstValueFrom(this.api.editComment(this.taskId(), comment.id, this.editBody));
      this.comments.update((list) => list.map((c) => (c.id === saved.id ? saved : c)));
      this.editingId.set(null);
    });
  }

  protected async remove(comment: TaskComment): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.deleteComment(this.taskId(), comment.id));
      this.comments.set(await firstValueFrom(this.api.comments(this.taskId())));
      await this.files?.changed();
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
