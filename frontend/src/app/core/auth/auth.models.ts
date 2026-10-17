export type PermissionScope = 'Own' | 'Project' | 'Global';

export interface RoleRef {
  id: string;
  name: string;
}

export interface PermissionGrant {
  code: string;
  scope: PermissionScope;
}

/** GET /api/v1/me */
export interface Me {
  id: string;
  email: string;
  fullName: string;
  timeZone: string;
  globalRole: RoleRef;
  permissions: PermissionGrant[];
}

const SCOPE_RANK: Record<PermissionScope, number> = { Own: 0, Project: 1, Global: 2 };

export function scopeAtLeast(actual: PermissionScope, minimum: PermissionScope): boolean {
  return SCOPE_RANK[actual] >= SCOPE_RANK[minimum];
}
