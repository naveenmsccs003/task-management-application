import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { toParams } from '../../core/api/http-params';
import { PagedResult } from '../../core/api/paged';
import {
  AlgorithmSubmission,
  ApprovalRequest,
  ApprovalRequestStatus,
  DelayCategory,
  DelayReport,
  ReviewSubmission,
  ChecklistItem,
  SaveTask,
  Task,
  TaskComment,
  TaskHistoryEntry,
  TaskListItem,
  TaskQuery,
  TaskTime,
} from './tasks.models';

export type WorkAction = 'start' | 'pause' | 'resume' | 'stop';

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

  /**
   * Start, pause, resume or stop. Each call carries a fresh Idempotency-Key, so a network retry of the same call cannot
   * act twice (FR-WORK-07); the server never takes times from the client.
   */
  work(id: string, action: WorkAction, reason?: string): Observable<Task> {
    const headers = new HttpHeaders({ 'Idempotency-Key': crypto.randomUUID() });
    return this.http.post<Task>(`/api/v1/tasks/${id}/${action}`, reason !== undefined ? { reason } : {}, { headers });
  }

  algorithms(id: string): Observable<AlgorithmSubmission[]> {
    return this.http.get<AlgorithmSubmission[]>(`/api/v1/tasks/${id}/algorithm`);
  }

  submitAlgorithm(id: string, content: string, attachmentIds: string[] = []): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/algorithm`, { content, attachmentIds });
  }

  decideAlgorithm(id: string, submissionId: string, decision: 'approve' | 'reject', comment: string | null): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/algorithm/${submissionId}/${decision}`, { comment });
  }

  checklist(id: string): Observable<ChecklistItem[]> {
    return this.http.get<ChecklistItem[]>(`/api/v1/tasks/${id}/checklist`);
  }

  addChecklistItem(id: string, text: string, isMandatory: boolean): Observable<ChecklistItem[]> {
    return this.http.post<ChecklistItem[]>(`/api/v1/tasks/${id}/checklist`, { text, isMandatory });
  }

  setChecklistItem(id: string, itemId: string, completed: boolean, note?: string): Observable<ChecklistItem[]> {
    return completed
      ? this.http.post<ChecklistItem[]>(`/api/v1/tasks/${id}/checklist/${itemId}/complete`, { note: note || null })
      : this.http.post<ChecklistItem[]>(`/api/v1/tasks/${id}/checklist/${itemId}/uncomplete`, {});
  }

  time(id: string): Observable<TaskTime> {
    return this.http.get<TaskTime>(`/api/v1/tasks/${id}/time`);
  }

  adjustTime(id: string, userId: string, minutes: number, reason: string): Observable<TaskTime> {
    return this.http.post<TaskTime>(`/api/v1/tasks/${id}/time-adjustments`, { userId, minutes, reason });
  }

  submitReview(id: string, summary: string, attachmentIds: string[] = []): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/submit-review`, { summary, attachmentIds });
  }

  approveReview(id: string, comment: string | null): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/approve`, { comment });
  }

  requestRevision(id: string, comment: string, reopenChecklistItemIds: string[], algorithmReapprovalRequired: boolean): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/request-revision`, { comment, reopenChecklistItemIds, algorithmReapprovalRequired });
  }

  close(id: string): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/close`, {});
  }

  reopen(id: string, reason: string): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${id}/reopen`, { reason });
  }

  reviews(id: string): Observable<ReviewSubmission[]> {
    return this.http.get<ReviewSubmission[]>(`/api/v1/tasks/${id}/reviews`);
  }

  requests(id: string): Observable<ApprovalRequest[]> {
    return this.http.get<ApprovalRequest[]>(`/api/v1/tasks/${id}/requests`);
  }

  requestReassignment(id: string, newAssigneeId: string, reason: string, algorithmReapprovalRequired: boolean): Observable<ApprovalRequest[]> {
    return this.http.post<ApprovalRequest[]>(`/api/v1/tasks/${id}/reassignment-requests`, { newAssigneeId, reason, algorithmReapprovalRequired });
  }

  requestExtension(id: string, proposedDueDate: string, reason: string): Observable<ApprovalRequest[]> {
    return this.http.post<ApprovalRequest[]>(`/api/v1/tasks/${id}/extension-requests`, { proposedDueDate, reason });
  }

  decideRequest(requestId: string, decision: 'approve' | 'reject', comment: string | null, approvedDueDate?: string | null): Observable<ApprovalRequest[]> {
    return this.http.post<ApprovalRequest[]>(`/api/v1/requests/${requestId}/${decision}`, { comment, approvedDueDate: approvedDueDate || null });
  }

  withdrawRequest(requestId: string): Observable<ApprovalRequest[]> {
    return this.http.post<ApprovalRequest[]>(`/api/v1/requests/${requestId}/withdraw`, {});
  }

  /** The caller's approval queue. */
  requestQueue(status: ApprovalRequestStatus = 'Pending'): Observable<ApprovalRequest[]> {
    return this.http.get<ApprovalRequest[]>('/api/v1/requests', { params: toParams({ status }) });
  }

  delays(id: string): Observable<DelayReport[]> {
    return this.http.get<DelayReport[]>(`/api/v1/tasks/${id}/delay-reports`);
  }

  reportDelay(id: string, category: DelayCategory, description: string): Observable<DelayReport[]> {
    return this.http.post<DelayReport[]>(`/api/v1/tasks/${id}/delay-reports`, { category, description });
  }

  history(id: string): Observable<TaskHistoryEntry[]> {
    return this.http.get<TaskHistoryEntry[]>(`/api/v1/tasks/${id}/history`);
  }

  comments(id: string): Observable<TaskComment[]> {
    return this.http.get<TaskComment[]>(`/api/v1/tasks/${id}/comments`);
  }

  addComment(id: string, body: string, attachmentIds: string[] = []): Observable<TaskComment> {
    return this.http.post<TaskComment>(`/api/v1/tasks/${id}/comments`, { body, attachmentIds });
  }

  editComment(id: string, commentId: string, body: string): Observable<TaskComment> {
    return this.http.patch<TaskComment>(`/api/v1/tasks/${id}/comments/${commentId}`, { body });
  }

  deleteComment(id: string, commentId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/tasks/${id}/comments/${commentId}`);
  }
}
