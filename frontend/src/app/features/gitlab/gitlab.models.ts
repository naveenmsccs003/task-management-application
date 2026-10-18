/** GitLab integration (docs/api-design.md §4.11). */
export type GitLabConnectionStatus = 'Unknown' | 'Healthy' | 'Unhealthy';
export type GitLabIssueState = 'Opened' | 'Closed' | 'Missing';
export type ImportRunKind = 'Import' | 'Sync' | 'Webhook';

export interface GitLabConnection {
  id: string;
  name: string;
  baseUrl: string;
  /** Last characters of the token; the token itself is never returned. */
  tokenHint: string;
  status: GitLabConnectionStatus;
  lastCheckedAt: string | null;
  lastError: string | null;
  linkedProjects: number;
  version: number;
}

export interface GitLabConnectionOption {
  id: string;
  name: string;
  status: GitLabConnectionStatus;
}

export interface SaveGitLabConnection {
  name: string;
  baseUrl: string;
  /** Required on create; on update only when replacing the token. */
  token: string | null;
  version?: number;
}

export interface GitLabProject {
  id: number;
  name: string;
  pathWithNamespace: string;
  webUrl: string;
}

export interface GitLabProjectLink {
  id: string;
  connectionId: string;
  connectionName: string;
  gitLabProjectId: number;
  pathWithNamespace: string;
  webUrl: string;
  syncEnabled: boolean;
  lastSyncedAt: string | null;
  hasWebhookSecret: boolean;
  webhookPath: string;
}

export interface GitLabIssuePreview {
  iid: number;
  title: string;
  isClosed: boolean;
  labels: string[];
  milestoneDueDate: string | null;
  webUrl: string;
  updatedAt: string;
  taskId: string | null;
  taskKey: string | null;
}

export interface GitLabIssuePage {
  items: GitLabIssuePreview[];
  page: number;
  hasMore: boolean;
}

export interface ImportRun {
  id: string;
  kind: ImportRunKind;
  startedBy: { id: string; name: string } | null;
  startedAt: string;
  finishedAt: string | null;
  created: number;
  updated: number;
  skipped: number;
  failed: number;
  errors: string[];
}

export interface WebhookSecret {
  secret: string;
  path: string;
}

/** On a task imported from GitLab. */
export interface TaskGitLab {
  webUrl: string;
  issueIid: number;
  state: GitLabIssueState;
  pendingChanges: { title: string | null; description: string | null } | null;
}

export const CONNECTION_STATUS_LABELS: Record<GitLabConnectionStatus, string> = {
  Unknown: 'Not tested',
  Healthy: 'Working',
  Unhealthy: 'Not working',
};

/** "2 created, 1 updated, 1 skipped, 0 failed". */
export function describeRun(run: ImportRun): string {
  return `${run.created} created, ${run.updated} updated, ${run.skipped} skipped, ${run.failed} failed`;
}
