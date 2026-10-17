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
export type TaskActionName =
  | 'update'
  | 'assign'
  | 'cancel'
  | 'delete'
  | 'comment'
  | 'attach'
  | 'start'
  | 'pause'
  | 'resume'
  | 'stop'
  | 'submitAlgorithm'
  | 'reviewAlgorithm'
  | 'completeChecklist'
  | 'manageChecklist'
  | 'adjustTime'
  | 'submitReview'
  | 'review'
  | 'close'
  | 'reopen'
  | 'requestReassignment'
  | 'requestExtension'
  | 'reportDelay';

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
  spentSeconds: number;
  algorithmRequired: boolean;
  algorithmApproved: boolean;
  labels: string[];
  currentRevision: number;
  approvedRevision: number | null;
  flags: { overdue: boolean; overAllocated: boolean; extensionPending: boolean; reassignmentPending: boolean };
  checklist: { total: number; done: number; mandatoryTotal: number; mandatoryDone: number };
  cancelReason: string | null;
  createdBy: UserRef;
  createdAt: string;
  updatedAt: string;
  allowedActions: TaskActionName[];
  canViewAlgorithm: boolean;
  canViewTime: boolean;
  canViewDelays: boolean;
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
  checklistTemplateId?: string | null;
  version?: number;
}

export interface ReviewSubmission {
  id: string;
  revision: number;
  summary: string;
  submittedBy: UserRef | null;
  submittedAt: string;
  decision: 'Pending' | 'Approved' | 'RevisionRequested';
  reviewedBy: UserRef | null;
  reviewedAt: string | null;
  comment: string | null;
  algorithmReapprovalRequired: boolean;
}

export type ApprovalRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Withdrawn' | 'AutoCancelled';

/** A reassignment or deadline-extension request (docs/api-design.md §4.8). */
export interface ApprovalRequest {
  id: string;
  taskId: string;
  taskKey: string;
  taskTitle: string;
  projectId: string;
  type: 'Reassignment' | 'DeadlineExtension';
  status: ApprovalRequestStatus;
  reason: string;
  requestedBy: UserRef | null;
  requestedAt: string;
  proposedAssignee: UserRef | null;
  algorithmReapprovalRequired: boolean;
  currentDueDate: string | null;
  proposedDueDate: string | null;
  approvedDueDate: string | null;
  decidedBy: UserRef | null;
  decidedAt: string | null;
  decisionComment: string | null;
  canDecide: boolean;
  canWithdraw: boolean;
}

export type DelayCategory = 'BlockedByDependency' | 'UnclearRequirement' | 'TechnicalIssue' | 'Leave' | 'Other';

export const DELAY_CATEGORY_LABELS: Record<DelayCategory, string> = {
  BlockedByDependency: 'Blocked by a dependency',
  UnclearRequirement: 'Unclear requirement',
  TechnicalIssue: 'Technical issue',
  Leave: 'Leave',
  Other: 'Other',
};

export interface DelayReport {
  id: string;
  revision: number;
  category: DelayCategory;
  description: string;
  reportedBy: UserRef | null;
  reportedAt: string;
}

export interface AlgorithmSubmission {
  id: string;
  version: number;
  revision: number;
  content: string;
  submittedBy: UserRef | null;
  submittedAt: string;
  decision: 'Pending' | 'Approved' | 'Rejected' | 'Withdrawn';
  decidedBy: UserRef | null;
  decidedAt: string | null;
  decisionComment: string | null;
}

export interface ChecklistItem {
  id: string;
  sortOrder: number;
  text: string;
  isMandatory: boolean;
  fromTemplate: boolean;
  isCompleted: boolean;
  completedBy: UserRef | null;
  completedAt: string | null;
  note: string | null;
  version: number;
}

export type SessionEndReason = 'Paused' | 'Stopped' | 'Submitted' | 'AutoClosed' | 'Reassigned' | 'Cancelled' | 'MemberDeactivated';

export interface WorkSession {
  id: string;
  user: UserRef | null;
  revision: number;
  startedAt: string;
  endedAt: string | null;
  endReason: SessionEndReason | null;
  seconds: number;
}

export interface TimeAdjustment {
  id: string;
  user: UserRef | null;
  revision: number;
  minutes: number;
  reason: string;
  createdBy: UserRef | null;
  createdAt: string;
}

/** GET /api/v1/tasks/{id}/time */
export interface TaskTime {
  allocatedMinutes: number | null;
  spentSeconds: number;
  overrunSeconds: number;
  percentOfAllocation: number | null;
  activeSession: WorkSession | null;
  byRevision: { revision: number; seconds: number }[];
  byMember: { user: UserRef | null; seconds: number }[];
  sessions: WorkSession[];
  adjustments: TimeAdjustment[];
  serverTime: string;
}

export { formatClock, formatDuration } from '../../core/time-format';

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
