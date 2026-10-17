export type TaskStatus =
  | 'Draft'
  | 'AwaitingAlgorithm'
  | 'AlgorithmSubmitted'
  | 'ReadyToStart'
  | 'InProgress'
  | 'Paused'
  | 'Stopped'
  | 'InReview'
  | 'RevisionRequested'
  | 'Approved'
  | 'Closed'
  | 'Cancelled';

export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Critical';

export type TaskType = 'Feature' | 'Bug' | 'Improvement' | 'Research' | 'Other';

/** Actions the server says the caller may take now (docs/api-design.md §5). */
export type TaskActionName = 'update' | 'assign' | 'cancel' | 'delete' | 'comment';

export const TASK_STATUS_LABELS: Record<TaskStatus, string> = {
  Draft: 'Draft',
  AwaitingAlgorithm: 'Awaiting algorithm',
  AlgorithmSubmitted: 'Algorithm submitted',
  ReadyToStart: 'Ready to start',
  InProgress: 'In progress',
  Paused: 'Paused',
  Stopped: 'Stopped',
  InReview: 'In review',
  RevisionRequested: 'Revision requested',
  Approved: 'Approved',
  Closed: 'Closed',
  Cancelled: 'Cancelled',
};

export const TASK_STATUSES = Object.keys(TASK_STATUS_LABELS) as TaskStatus[];
export const TASK_PRIORITIES: TaskPriority[] = ['Low', 'Medium', 'High', 'Critical'];
export const TASK_TYPES: TaskType[] = ['Feature', 'Bug', 'Improvement', 'Research', 'Other'];

export interface UserRef {
  id: string;
  name: string;
}

/** GET /api/v1/tasks/{id} */
export interface Task {
  id: string;
  key: string;
  projectId: string;
  projectKey: string;
  projectName: string;
  projectReadOnly: boolean;
  title: string;
  description: string;
  type: TaskType;
  priority: TaskPriority;
  status: TaskStatus;
  assignee: UserRef | null;
  startDate: string | null;
  dueDate: string | null;
  originalDueDate: string | null;
  allocatedMinutes: number | null;
  algorithmRequired: boolean;
  labels: string[];
  currentRevision: number;
  flags: { overdue: boolean };
  cancelReason: string | null;
  createdBy: UserRef;
  createdAt: string;
  updatedAt: string;
  allowedActions: TaskActionName[];
  version: number;
}

export interface TaskListItem {
  id: string;
  key: string;
  projectId: string;
  projectKey: string;
  title: string;
  type: TaskType;
  priority: TaskPriority;
  status: TaskStatus;
  assignee: UserRef | null;
  dueDate: string | null;
  overdue: boolean;
  labels: string[];
  updatedAt: string;
}

export interface TaskQuery {
  projectId?: string;
  status?: TaskStatus[];
  assigneeId?: string;
  priority?: TaskPriority[];
  overdue?: boolean;
  q?: string;
  sort?: string;
  page: number;
  pageSize: number;
}

export interface SaveTask {
  title: string;
  description: string;
  type: TaskType;
  priority: TaskPriority;
  startDate: string | null;
  dueDate: string | null;
  allocatedMinutes: number | null;
  algorithmRequired: boolean;
  labels: string[];
  version?: number;
}

export interface TaskHistoryEntry {
  occurredAt: string;
  kind: 'status' | 'assignment';
  actor: UserRef | null;
  revision: number | null;
  action: string | null;
  fromStatus: TaskStatus | null;
  toStatus: TaskStatus | null;
  fromUser: UserRef | null;
  toUser: UserRef | null;
  comment: string | null;
}

export interface TaskComment {
  id: string;
  author: UserRef | null;
  body: string | null;
  createdAt: string;
  editedAt: string | null;
  isDeleted: boolean;
  canEdit: boolean;
  canDelete: boolean;
}

/** "8 h", "1.5 h" or "Not estimated" (edge case 16). */
export function formatAllocation(minutes: number | null): string {
  if (!minutes) return 'Not estimated';
  const hours = Math.round((minutes / 60) * 100) / 100;
  return `${hours} h`;
}

/** One line describing a history entry, e.g. "Assigned to Priya" or "Draft → Ready to start". */
export function describeHistory(entry: TaskHistoryEntry): string {
  if (entry.kind === 'assignment') {
    return entry.fromUser ? `Reassigned from ${entry.fromUser.name} to ${entry.toUser?.name}` : `Assigned to ${entry.toUser?.name}`;
  }
  if (!entry.fromStatus) {
    return `Created as ${TASK_STATUS_LABELS[entry.toStatus!]}`;
  }
  return `${TASK_STATUS_LABELS[entry.fromStatus]} → ${TASK_STATUS_LABELS[entry.toStatus!]}`;
}
