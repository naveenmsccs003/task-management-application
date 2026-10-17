import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { toParams } from '../../core/api/http-params';
import { PagedResult } from '../../core/api/paged';
import { RoleRef } from '../../core/auth/auth.models';
import {
  MemberCandidate,
  Project,
  ProjectListItem,
  ProjectMember,
  ProjectQuery,
  ProjectStatusAction,
  SaveProject,
} from './projects.models';

/** Typed calls to the project endpoints (docs/api-design.md §4.3). */
@Injectable({ providedIn: 'root' })
export class ProjectsApi {
  private readonly http = inject(HttpClient);

  list(query: ProjectQuery): Observable<PagedResult<ProjectListItem>> {
    return this.http.get<PagedResult<ProjectListItem>>('/api/v1/projects', { params: toParams(query) });
  }

  /** Roles that can be given inside a project (every role except Admin). */
  projectRoles(): Observable<RoleRef[]> {
    return this.http.get<RoleRef[]>('/api/v1/projects/roles');
  }

  get(id: string): Observable<Project> {
    return this.http.get<Project>(`/api/v1/projects/${id}`);
  }

  create(body: SaveProject): Observable<Project> {
    return this.http.post<Project>('/api/v1/projects', body);
  }

  update(id: string, body: SaveProject): Observable<Project> {
    return this.http.patch<Project>(`/api/v1/projects/${id}`, body);
  }

  changeStatus(id: string, action: ProjectStatusAction, confirmOpenTasks = false): Observable<Project> {
    return this.http.post<Project>(`/api/v1/projects/${id}/${action}`, { confirmOpenTasks });
  }

  members(id: string): Observable<ProjectMember[]> {
    return this.http.get<ProjectMember[]>(`/api/v1/projects/${id}/members`);
  }

  candidates(id: string, search: string): Observable<MemberCandidate[]> {
    return this.http.get<MemberCandidate[]>(`/api/v1/projects/${id}/member-candidates`, { params: toParams({ search }) });
  }

  addMember(id: string, userId: string, projectRoleId: string): Observable<ProjectMember[]> {
    return this.http.post<ProjectMember[]>(`/api/v1/projects/${id}/members`, { userId, projectRoleId });
  }

  changeMemberRole(id: string, userId: string, projectRoleId: string): Observable<ProjectMember[]> {
    return this.http.patch<ProjectMember[]>(`/api/v1/projects/${id}/members/${userId}`, { projectRoleId });
  }

  removeMember(id: string, userId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/projects/${id}/members/${userId}`);
  }
}
