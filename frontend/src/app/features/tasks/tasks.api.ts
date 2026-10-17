import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { toParams } from '../../core/api/http-params';
import { PagedResult } from '../../core/api/paged';
import { SaveTask, Task, TaskComment, TaskHistoryEntry, TaskListItem, TaskQuery } from './tasks.models';

/** Typed calls to the task endpoints (docs/api-design.md §4.4–4.5). */
@Injectable({ providedIn: 'root' })
export class TasksApi {
  private readonly http = inject(HttpClient);

  list(query: TaskQuery): Observable<PagedResult<TaskListItem>> {
    return this.http.get<PagedResult<TaskListItem>>('/api/v1/tasks', { params: toParams(query) });
  }

  get(id: string): Observable<Task> {
    return this.http.get<Task>(`/api/v1/tasks/${id}`);
  }

  create(projectId: string, body: SaveTask): Observable<Task> {
    return this.http.post<Task>(`/api/v1/projects/${projectId}/tasks`, body);
  }

  update(id: string, body: SaveTask): Observable<Task> {
    return this.http.patch<Task>(`/api/v1/tasks/${id}`, body);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/tasks/${id}`);
  }

  assign(id: string, assigneeId: string, dueDate: string | null): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/assign`, { assigneeId, dueDate });
  }

  cancel(id: string, reason: string): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/cancel`, { reason });
  }

  history(id: string): Observable<TaskHistoryEntry[]> {
    return this.http.get<TaskHistoryEntry[]>(`/api/v1/tasks/${id}/history`);
  }

  comments(id: string): Observable<TaskComment[]> {
    return this.http.get<TaskComment[]>(`/api/v1/tasks/${id}/comments`);
  }

  addComment(id: string, body: string): Observable<TaskComment> {
    return this.http.post<TaskComment>(`/api/v1/tasks/${id}/comments`, { body });
  }

  editComment(id: string, commentId: string, body: string): Observable<TaskComment> {
    return this.http.patch<TaskComment>(`/api/v1/tasks/${id}/comments/${commentId}`, { body });
  }

  deleteComment(id: string, commentId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/tasks/${id}/comments/${commentId}`);
  }
}
