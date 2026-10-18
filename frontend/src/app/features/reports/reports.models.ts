import { ActiveSession } from '../../core/work/active-session.service';
import { ProjectStatus } from '../projects/projects.models';
import { TaskStatus, UserRef } from '../tasks/tasks.models';

// Dashboard (GET /api/v1/dashboard, docs/api-design.md §4.12). Sections the member may not see are null.

export interface DashboardTask {
  id: string;
  key: string;
  title: string;
  status: TaskStatus;
  dueDate: string | null;
  assignee: UserRef | null;
}

export interface DashboardList {
  total: number;
  items: DashboardTask[];
}

export interface StatusCount {
  status: TaskStatus;
  count: number;
}

export interface MyWork {
  byStatus: StatusCount[];
  activeSession: ActiveSession | null;
  secondsThisWeek: number;
  dueSoon: DashboardList;
  overdue: DashboardList;
  waitingForFeedback: DashboardList;
  revisionRequested: DashboardList;
}

export interface OverAllocatedTask {
  id: string;
  key: string;
  title: string;
  assignee: UserRef | null;
  allocatedSeconds: number;
  spentSeconds: number;
}

export interface Workload {
  member: UserRef;
  openTasks: number;
  inProgress: number;
  secondsThisWeek: number;
}

export interface Team {
  algorithmApprovals: DashboardList;
  reviews: DashboardList;
  overAllocatedTotal: number;
  overAllocated: OverAllocatedTask[];
  overdue: DashboardList;
  pendingRequests: number;
  workload: Workload[];
}

export interface WeekCount {
  weekStart: string;
  count: number;
}

export interface ProjectProgress {
  id: string;
  key: string;
  name: string;
  status: ProjectStatus;
  byStatus: StatusCount[];
  allocatedSeconds: number;
  spentSeconds: number;
  overdue: number;
  overdueTrend: WeekCount[];
  extensionsApproved: number;
  revisionsRequested: number;
}

export type GitLabConnectionStatus = 'Unknown' | 'Healthy' | 'Unhealthy';

export interface AdminOverview {
  activeMembers: number;
  invitedMembers: number;
  inactiveMembers: number;
  lockedMembers: number;
  failedSignIns24Hours: number;
  failedSignIns7Days: number;
  gitLab: { id: string; name: string; status: GitLabConnectionStatus; lastCheckedAt: string | null; lastError: string | null }[];
  jobs: { jobName: string; lastFinishedAt: string; lastSucceeded: boolean; lastSucceededAt: string | null; lastMessage: string | null }[];
  emailsPending: number;
  emailsFailed: number;
  oldestPendingEmailAt: string | null;
}

export interface Dashboard {
  serverTime: string;
  timeZone: string;
  myWork: MyWork | null;
  team: Team | null;
  projects: { total: number; items: ProjectProgress[] } | null;
  admin: AdminOverview | null;
}

// Reports (GET /api/v1/reports and /api/v1/reports/{name}).

export type ReportColumnKind = 'Text' | 'Number' | 'Duration' | 'Date' | 'DateTime' | 'Task';

export interface ReportInfo {
  name: string;
  title: string;
  description: string;
  usesDateRange: boolean;
  groupBy: string[];
  canExport: boolean;
}

export interface ReportColumn {
  key: string;
  label: string;
  kind: ReportColumnKind;
}

export type ReportCell = string | number | { id: string; key: string } | null;

export interface ReportTable {
  report: string;
  title: string;
  from: string | null;
  to: string | null;
  timeZone: string;
  columns: ReportColumn[];
  rows: ReportCell[][];
  truncated: boolean;
  generatedAt: string;
}

export interface ReportQuery {
  from?: string;
  to?: string;
  projectId?: string;
  groupBy?: string;
}

export const GROUP_BY_LABELS: Record<string, string> = {
  task: 'Task',
  member: 'Member',
  project: 'Project',
  revision: 'Task revision',
  day: 'Day (timesheet)',
};

/** An instant as "18 Oct 2026, 14:05" in the report's time zone (the viewer's, FR-TIME-10). */
export function formatInstant(iso: string, timeZone: string): string {
  try {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(new Date(iso));
  } catch {
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso));
  }
}
