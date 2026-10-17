import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AttachmentsApi } from './attachments.api';
import { Attachment, AttachmentOwnerType } from './attachments.models';

/**
 * The task page's files, shared by the task, comment, algorithm and review sections. While any file is still being
 * scanned the list is refreshed every few seconds (for at most two minutes), so "Checking for malware…" turns into a
 * download link without a reload.
 */
@Injectable()
export class TaskAttachmentsStore {
  static readonly REFRESH_MS = 4000;
  static readonly MAX_REFRESHES = 30;

  private readonly api = inject(AttachmentsApi);
  private taskId = '';
  private timer: ReturnType<typeof setTimeout> | undefined;
  private refreshes = 0;

  readonly attachments = signal<Attachment[]>([]);

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  async load(taskId: string): Promise<void> {
    this.taskId = taskId;
    this.refreshes = 0;
    await this.refresh();
  }

  forOwner(ownerType: AttachmentOwnerType, ownerId: string): Attachment[] {
    return this.attachments().filter((a) => a.ownerType === ownerType && a.ownerId === ownerId);
  }

  async refresh(): Promise<void> {
    if (!this.taskId) return;
    clearTimeout(this.timer);
    try {
      this.attachments.set(await firstValueFrom(this.api.list(this.taskId)));
    } catch {
      return; // the sections show their own errors; files simply stay as they were
    }
    if (this.attachments().some((a) => a.scanStatus === 'Pending') && this.refreshes < TaskAttachmentsStore.MAX_REFRESHES) {
      this.refreshes++;
      this.timer = setTimeout(() => void this.refresh(), TaskAttachmentsStore.REFRESH_MS);
    }
  }

  /** After an upload or a record that linked drafts: refresh and allow a new round of polling. */
  async changed(): Promise<void> {
    this.refreshes = 0;
    await this.refresh();
  }
}
