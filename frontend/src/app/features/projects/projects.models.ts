import { PermissionGrant, PermissionScope, RoleRef, scopeAtLeast } from '../../core/auth/auth.models';

export type ProjectStatus = 'Active' | 'OnHold' | 'Completed' | 'Archived';

export const PROJECT_STATUS_LABELS: Record<ProjectStatus, string> = {
  Active: 'Active',
  OnHold: 'On hold',
  Completed: 'Completed',
  Archived: 'Archived',
};

/** GET /api/v1/projects/{id} */
export interface Project {
  id: string;
  key: string;
  name: string;
  description: string;
  status: ProjectStatus;
  startDate: string | null;
  targetEndDate: string | null;
  algorithmRequiredDefault: boolean;
  closeOnApproval: boolean;
  defaultChecklistTemplateId: string | null;
  memberCount: number;
  openTaskCount: number;
  myRole: RoleRef | null;
  /** The caller's effective permissions in this project (display only; the API enforces them). */
  myPermissions: PermissionGrant[];
  createdAt: string;
  version: number;
}

export interface ProjectListItem {
  id: string;
  key: string;
  name: string;
  status: ProjectStatus;
  targetEndDate: string | null;
  memberCount: number;
  openTaskCount: number;
  myRole: RoleRef | null;
}

export interface ProjectQuery {
  search?: string;
  status?: ProjectStatus;
  page: number;
  pageSize: number;
}

export interface SaveProject {
  key?: string;
  name: string;
  description: string;
  startDate: string | null;
  targetEndDate: string | null;
  algorithmRequiredDefault: boolean;
  closeOnApproval: boolean;
  defaultChecklistTemplateId?: string | null;
  version?: number;
}

export interface ProjectMember {
  userId: string;
  fullName: string;
  email: string;
  isActive: boolean;
  projectRole: RoleRef;
  canWorkOnTasks: boolean;
  openTaskCount: number;
  addedAt: string;
}

export interface MemberCandidate {
  id: string;
  fullName: string;
  email: string;
}

export type ProjectStatusAction = 'hold' | 'complete' | 'archive' | 'reactivate';

/** True when the project grants the permission with at least the given scope. */
export function projectAllows(project: Project | null | undefined, code: string, minimumScope: PermissionScope = 'Project'): boolean {
  const grant = project?.myPermissions.find((p) => p.code === code);
  return !!grant && scopeAtLeast(grant.scope, minimumScope);
}

export function isReadOnly(project: Project | null | undefined): boolean {
  return project?.status === 'Completed' || project?.status === 'Archived';
}
