import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import { Attachment, AttachmentOwnerType, AttachmentRules } from './attachments.models';

/** Typed calls to the attachment endpoints (docs/api-design.md §4.9). */
@Injectable({ providedIn: 'root' })
export class AttachmentsApi {
  private readonly http = inject(HttpClient);
  private rulesCache?: Observable<AttachmentRules>;

  /** Allowed types and limits; fetched once per page load (Admins rarely change them). */
  rules(): Observable<AttachmentRules> {
    this.rulesCache ??= this.http.get<AttachmentRules>('/api/v1/attachment-rules').pipe(shareReplay({ bufferSize: 1, refCount: false }));
    return this.rulesCache;
  }

  list(taskId: string): Observable<Attachment[]> {
    return this.http.get<Attachment[]>(`/api/v1/tasks/${taskId}/attachments`);
  }

  /** One file per request, with upload progress events. */
  upload(taskId: string, ownerType: AttachmentOwnerType, file: File): Observable<HttpEvent<Attachment>> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<Attachment>(`/api/v1/tasks/${taskId}/attachments`, body, {
      params: new HttpParams().set('ownerType', ownerType),
      reportProgress: true,
      observe: 'events',
    });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/attachments/${id}`);
  }

  downloadUrl(id: string): string {
    return `/api/v1/attachments/${id}/download`;
  }

  previewUrl(id: string): string {
    return `/api/v1/attachments/${id}/preview`;
  }
}
