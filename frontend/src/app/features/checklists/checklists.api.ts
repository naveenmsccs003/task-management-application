import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { toParams } from '../../core/api/http-params';
import { ChecklistTemplate, SaveChecklistTemplate } from './checklists.models';

/** Typed calls to the checklist template endpoints (docs/api-design.md §4.6). */
@Injectable({ providedIn: 'root' })
export class ChecklistsApi {
  private readonly http = inject(HttpClient);

  /** Without a project: global templates (managers). With one: the templates usable in that project. */
  list(projectId?: string): Observable<ChecklistTemplate[]> {
    return this.http.get<ChecklistTemplate[]>('/api/v1/checklist-templates', { params: toParams({ projectId }) });
  }

  create(body: SaveChecklistTemplate): Observable<ChecklistTemplate> {
    return this.http.post<ChecklistTemplate>('/api/v1/checklist-templates', body);
  }

  update(id: string, body: SaveChecklistTemplate): Observable<ChecklistTemplate> {
    return this.http.patch<ChecklistTemplate>(`/api/v1/checklist-templates/${id}`, body);
  }
}
