import { PermissionGrant, RoleRef } from '../../core/auth/auth.models';

export interface Member {
  id: string;
  email: string;
  fullName: string;
  timeZone: string;
  employeeCode: string | null;
  globalRole: RoleRef;
  isActive: boolean;
  hasPassword: boolean;
  createdAt: string;
  lastSignInAt: string | null;
  deactivatedAt: string | null;
  version: number;
}

export interface MemberQuery {
  search?: string;
  isActive?: boolean;
  roleId?: string;
  page: number;
  pageSize: number;
}

export interface SaveMember {
  email?: string;
  fullName: string;
  globalRoleId: string;
  timeZone: string;
  employeeCode: string | null;
  version?: number;
}

export interface Role {
  id: string;
  name: string;
  description: string;
  isSystem: boolean;
  memberCount: number;
  permissions: PermissionGrant[];
  version: number;
}

export interface Permission {
  code: string;
  module: string;
  description: string;
}

export interface AuthSettings {
  passwordMinLength: number;
  maxFailedSignInAttempts: number;
  lockoutMinutes: number;
  resetTokenMinutes: number;
  inviteTokenHours: number;
}

export interface WorkSettings {
  maxSessionHours: number;
  pauseReasonRequired: boolean;
  allocationWarningPercent: number;
}

export interface Settings {
  auth: AuthSettings;
  work: WorkSettings;
}

export interface AuditEntry {
  id: number;
  occurredAt: string;
  actorId: string | null;
  actorName: string | null;
  actorIp: string | null;
  action: string;
  entityType: string;
  entityId: string;
  projectId: string | null;
  changes: Record<string, unknown> | null;
  correlationId: string | null;
}

export interface AuditQuery {
  from?: string;
  to?: string;
  action?: string;
  entityType?: string;
  entityId?: string;
  page: number;
  pageSize: number;
}
