import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { toParams } from '../../core/api/http-params';
import { PagedResult } from '../../core/api/paged';
import { PermissionGrant } from '../../core/auth/auth.models';
import { AuditEntry, AuditQuery, AuthSettings, Member, MemberQuery, Permission, Role, SaveMember, Settings, WorkSettings } from './admin.models';

/** Typed calls to the administration endpoints (docs/api-design.md §4.2, §4.12). */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  members(query: MemberQuery): Observable<PagedResult<Member>> {
    return this.http.get<PagedResult<Member>>('/api/v1/members', { params: toParams(query) });
  }

  member(id: string): Observable<Member> {
    return this.http.get<Member>(`/api/v1/members/${id}`);
  }

  createMember(body: SaveMember): Observable<Member> {
    return this.http.post<Member>('/api/v1/members', body);
  }

  updateMember(id: string, body: SaveMember): Observable<Member> {
    return this.http.patch<Member>(`/api/v1/members/${id}`, body);
  }

  deactivateMember(id: string): Observable<Member> {
    return this.http.post<Member>(`/api/v1/members/${id}/deactivate`, {});
  }

  reactivateMember(id: string): Observable<Member> {
    return this.http.post<Member>(`/api/v1/members/${id}/reactivate`, {});
  }

  sendPasswordReset(id: string): Observable<void> {
    return this.http.post<void>(`/api/v1/members/${id}/send-password-reset`, {});
  }

  roles(): Observable<Role[]> {
    return this.http.get<Role[]>('/api/v1/roles');
  }

  permissions(): Observable<Permission[]> {
    return this.http.get<Permission[]>('/api/v1/permissions');
  }

  createRole(name: string, description: string): Observable<Role> {
    return this.http.post<Role>('/api/v1/roles', { name, description });
  }

  updateRole(id: string, name: string, description: string): Observable<Role> {
    return this.http.patch<Role>(`/api/v1/roles/${id}`, { name, description });
  }

  deleteRole(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/roles/${id}`);
  }

  setRolePermissions(id: string, permissions: PermissionGrant[], version: number): Observable<Role> {
    return this.http.put<Role>(`/api/v1/roles/${id}/permissions`, { permissions, version });
  }

  settings(): Observable<Settings> {
    return this.http.get<Settings>('/api/v1/settings');
  }

  updateAuthSettings(settings: AuthSettings): Observable<AuthSettings> {
    return this.http.put<AuthSettings>('/api/v1/settings/auth', settings);
  }

  updateWorkSettings(settings: WorkSettings): Observable<WorkSettings> {
    return this.http.put<WorkSettings>('/api/v1/settings/work', settings);
  }

  auditLogs(query: AuditQuery): Observable<PagedResult<AuditEntry>> {
    return this.http.get<PagedResult<AuditEntry>>('/api/v1/audit-logs', { params: toParams(query) });
  }
}
