import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { AttachmentsApi } from './attachments.api';
import { Attachment, formatFileSize, SCAN_STATUS_LABELS } from './attachments.models';
import { TaskAttachmentsStore } from './task-attachments.store';

/**
 * A list of files: download links (only once the malware scan has passed, FR-ATT-07), image thumbnails and video
 * players for previewable files, and a delete button when the server allows it.
 */
@Component({
  selector: 'app-attachment-list',
  imports: [DatePipe, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (attachments().length) {
      <ul class="files" [attr.aria-label]="label()">
        @for (a of attachments(); track a.id) {
          <li>
            @if (a.canPreview && a.category === 'Image') {
              <a [href]="api.previewUrl(a.id)" target="_blank" rel="noopener">
                <img [src]="api.previewUrl(a.id)" [alt]="a.fileName" loading="lazy" />
              </a>
            }
            @if (a.canPreview && a.category === 'Video' && playing() === a.id) {
              <video [src]="api.previewUrl(a.id)" controls preload="metadata" [attr.aria-label]="a.fileName"></video>
            }
            <div class="line">
              @if (a.scanStatus === 'Clean') {
                <a [href]="api.downloadUrl(a.id)" download>{{ a.fileName }}</a>
              } @else {
                <span>{{ a.fileName }}</span>
                <span class="scan" [class.blocked]="a.scanStatus !== 'Pending'">{{ scanLabels[a.scanStatus] }}</span>
              }
              <span class="muted">{{ size(a.sizeBytes) }} · {{ a.uploadedBy?.name ?? 'Unknown' }} · {{ a.uploadedAt | date: 'short' }}</span>
              @if (a.canPreview && a.category === 'Video' && playing() !== a.id) {
                <button mat-button type="button" (click)="playing.set(a.id)">Play</button>
              }
              @if (a.canDelete) {
                <button mat-button type="button" (click)="remove(a)" [disabled]="busy()" [attr.aria-label]="'Delete ' + a.fileName">Delete</button>
              }
            </div>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .files {
      list-style: none;
      padding: 0;
      margin: 4px 0;
    }
    .files li {
      margin-bottom: 6px;
    }
    .line {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px;
    }
    img {
      display: block;
      max-width: 240px;
      max-height: 160px;
      border-radius: 4px;
      border: 1px solid var(--mat-sys-outline-variant);
    }
    video {
      display: block;
      max-width: 100%;
      max-height: 320px;
    }
    .scan {
      padding: 0 8px;
      border-radius: 8px;
      background: var(--mat-sys-surface-variant);
    }
    .scan.blocked {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
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
export class AttachmentList {
  protected readonly api = inject(AttachmentsApi);
  private readonly store = inject(TaskAttachmentsStore, { optional: true });

  readonly attachments = input.required<Attachment[]>();
  readonly label = input('Attached files');

  protected readonly scanLabels = SCAN_STATUS_LABELS;
  protected readonly playing = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected size(bytes: number): string {
    return formatFileSize(bytes);
  }

  protected async remove(attachment: Attachment): Promise<void> {
    this.busy.set(true);
    this.failure.set(null);
    try {
      await firstValueFrom(this.api.delete(attachment.id));
      await this.store?.changed();
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
