import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';
import { toParams } from '../../core/api/http-params';
import { Task } from '../tasks/tasks.models';
import {
  GitLabConnection,
  GitLabConnectionOption,
  GitLabIssuePage,
  GitLabProject,
  GitLabProjectLink,
  ImportRun,
  SaveGitLabConnection,
  WebhookSecret,
} from './gitlab.models';

/** Typed calls to the GitLab endpoints (docs/api-design.md §4.11). */
@Injectable({ providedIn: 'root' })
export class GitLabApi {
  private readonly http = inject(HttpClient);

  connections(): Observable<GitLabConnection[]> {
    return this.http.get<GitLabConnection[]>('/api/v1/gitlab/connections');
  }

  createConnection(body: SaveGitLabConnection): Observable<GitLabConnection> {
    return this.http.post<GitLabConnection>('/api/v1/gitlab/connections', body);
  }

  updateConnection(id: string, body: SaveGitLabConnection): Observable<GitLabConnection> {
    return this.http.patch<GitLabConnection>(`/api/v1/gitlab/connections/${id}`, body);
  }

  deleteConnection(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/gitlab/connections/${id}`);
  }

  testConnection(id: string): Observable<GitLabConnection> {
    return this.http.post<GitLabConnection>(`/api/v1/gitlab/connections/${id}/test`, {});
  }

  connectionOptions(): Observable<GitLabConnectionOption[]> {
    return this.http.get<GitLabConnectionOption[]>('/api/v1/gitlab/connection-options');
  }

  searchProjects(connectionId: string, search: string): Observable<GitLabProject[]> {
    return this.http.get<GitLabProject[]>(`/api/v1/gitlab/connections/${connectionId}/projects`, { params: toParams({ search }) });
  }

  /** null when the project is not linked (204). */
  link(projectId: string): Observable<GitLabProjectLink | null> {
    return this.http
      .get<GitLabProjectLink>(`/api/v1/projects/${projectId}/gitlab-link`, { observe: 'response' })
      .pipe(map((response: HttpResponse<GitLabProjectLink>) => (response.status === 204 ? null : response.body)));
  }

  setLink(projectId: string, connectionId: string, gitLabProjectId: number, syncEnabled: boolean): Observable<GitLabProjectLink> {
    return this.http.put<GitLabProjectLink>(`/api/v1/projects/${projectId}/gitlab-link`, { connectionId, gitLabProjectId, syncEnabled });
  }

  unlink(projectId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/projects/${projectId}/gitlab-link`);
  }

  newWebhookSecret(projectId: string): Observable<WebhookSecret> {
    return this.http.post<WebhookSecret>(`/api/v1/projects/${projectId}/gitlab-link/webhook-secret`, {});
  }

  issues(projectId: string, state: 'opened' | 'closed' | 'all', search: string, page: number): Observable<GitLabIssuePage> {
    return this.http.get<GitLabIssuePage>(`/api/v1/projects/${projectId}/gitlab/issues`, { params: toParams({ state, search, page }) });
  }

  import(projectId: string, issueIids: number[]): Observable<ImportRun> {
    return this.http.post<ImportRun>(`/api/v1/projects/${projectId}/gitlab/import`, { issueIids });
  }

  sync(projectId: string): Observable<ImportRun> {
    return this.http.post<ImportRun>(`/api/v1/projects/${projectId}/gitlab/sync`, {});
  }

  runs(projectId: string): Observable<ImportRun[]> {
    return this.http.get<ImportRun[]>(`/api/v1/projects/${projectId}/gitlab/import-runs`);
  }

  acceptChanges(taskId: string): Observable<Task> {
    return this.http.post<Task>(`/api/v1/tasks/${taskId}/gitlab/accept-changes`, {});
  }
}
