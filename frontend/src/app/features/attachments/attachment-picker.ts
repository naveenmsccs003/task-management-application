import { HttpEventType } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, inject, input, model, OnInit, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom, lastValueFrom, tap } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { AttachmentsApi } from './attachments.api';
import { acceptList, Attachment, AttachmentOwnerType, AttachmentRules, checkFile, formatFileSize } from './attachments.models';
import { TaskAttachmentsStore } from './task-attachments.store';

interface Upload {
  name: string;
  percent: number;
}

/**
 * Chooses and uploads files, one request per file with progress. Task files are attached at once. Comment, algorithm
 * and review files are drafts: they are listed here (and can be removed) until the parent form sends their ids with
 * the record, then the parent clears <c>drafts</c>.
 */
@Component({
  selector: 'app-attachment-picker',
  imports: [MatButtonModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <input
      #fileInput
      type="file"
      class="hidden"
      multiple
      [accept]="accept()"
      [attr.aria-label]="label()"
      (change)="chosen($event)"
    />
    <button mat-stroked-button type="button" (click)="fileInput.click()" [disabled]="!rules() || uploading().length > 0">
      {{ buttonText() }}
    </button>
    @for (u of uploading(); track u.name) {
      <div class="upload">
        <span>{{ u.name }}</span>
        <mat-progress-bar mode="determinate" [value]="u.percent" [attr.aria-label]="'Uploading ' + u.name" />
      </div>
    }
    @for (message of errors(); track $index) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (ownerType() !== 'Task' && drafts().length) {
      <ul class="drafts" aria-label="Files to attach">
        @for (d of drafts(); track d.id) {
          <li>
            {{ d.fileName }} <span class="muted">{{ size(d.sizeBytes) }}</span>
            <button mat-button type="button" (click)="removeDraft(d)" [attr.aria-label]="'Remove ' + d.fileName">Remove</button>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
      margin: 4px 0 8px;
    }
    .hidden {
      display: none;
    }
    .upload {
      margin-top: 6px;
      max-width: 360px;
    }
    .drafts {
      list-style: none;
      padding: 0;
      margin: 4px 0;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
      margin: 4px 0;
    }
  `,
})
export class AttachmentPicker implements OnInit {
  private readonly api = inject(AttachmentsApi);
  private readonly store = inject(TaskAttachmentsStore, { optional: true });
  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');

  readonly taskId = input.required<string>();
  readonly ownerType = input<AttachmentOwnerType>('Task');
  readonly label = input('Attach a file');
  readonly buttonText = input('Attach files');
  /** Uploaded drafts waiting for their record (not used for task files). */
  readonly drafts = model<Attachment[]>([]);

  protected readonly rules = signal<AttachmentRules | null>(null);
  protected readonly accept = signal('');
  protected readonly uploading = signal<Upload[]>([]);
  protected readonly errors = signal<string[]>([]);

  async ngOnInit(): Promise<void> {
    try {
      const rules = await firstValueFrom(this.api.rules());
      this.rules.set(rules);
      this.accept.set(acceptList(rules));
    } catch (error) {
      this.errors.set([asApiError(error).message]);
    }
  }

  protected size(bytes: number): string {
    return formatFileSize(bytes);
  }

  protected async chosen(event: Event): Promise<void> {
    const files = Array.from((event.target as HTMLInputElement).files ?? []);
    this.fileInput().nativeElement.value = '';
    await this.uploadAll(files);
  }

  /** Uploads one file after another; problems are listed per file and do not stop the others. */
  async uploadAll(files: File[]): Promise<void> {
    const rules = this.rules();
    if (!rules) return;
    const errors: string[] = [];
    const room = rules.maxFilesPerRecord - this.drafts().length;
    if (this.ownerType() !== 'Task' && files.length > room) {
      errors.push(`Attach at most ${rules.maxFilesPerRecord} files.`);
      files = files.slice(0, Math.max(room, 0));
    }

    for (const file of files) {
      const problem = checkFile(file, rules);
      if (problem) {
        errors.push(problem);
        continue;
      }
      this.uploading.set([{ name: file.name, percent: 0 }]);
      try {
        const event = await lastValueFrom(
          this.api.upload(this.taskId(), this.ownerType(), file).pipe(
            tap((e) => {
              if (e.type === HttpEventType.UploadProgress && e.total) {
                this.uploading.set([{ name: file.name, percent: Math.round((100 * e.loaded) / e.total) }]);
              }
            }),
          ),
        );
        if (event.type === HttpEventType.Response && event.body && this.ownerType() !== 'Task') {
          this.drafts.update((list) => [...list, event.body!]);
        }
      } catch (error) {
        errors.push(`${file.name}: ${asApiError(error).message}`);
      }
    }

    this.uploading.set([]);
    this.errors.set(errors);
    if (this.ownerType() === 'Task') {
      await this.store?.changed();
    }
  }

  protected async removeDraft(draft: Attachment): Promise<void> {
    try {
      await firstValueFrom(this.api.delete(draft.id));
      this.drafts.update((list) => list.filter((d) => d.id !== draft.id));
    } catch (error) {
      this.errors.set([asApiError(error).message]);
    }
  }
}
