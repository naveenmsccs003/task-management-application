export type NotificationCategory = 'Account' | 'Assignment' | 'Algorithm' | 'Time' | 'Review' | 'Requests' | 'Deadlines' | 'GitLab';

export const NOTIFICATION_CATEGORY_LABELS: Record<NotificationCategory, string> = {
  Account: 'Account and security',
  Assignment: 'Task assignments',
  Algorithm: 'Algorithm submissions and decisions',
  Time: 'Allocated hours and work sessions',
  Review: 'Reviews, closure and reopening',
  Requests: 'Reassignment and deadline requests',
  Deadlines: 'Due soon, overdue and delay reports',
  GitLab: 'GitLab issues closed or removed',
};

/** GET /api/v1/notifications (FR-NOT-01) and the SignalR "notification" message. */
export interface AppNotification {
  id: string;
  type: string;
  category: NotificationCategory;
  title: string;
  body: string;
  link: string | null;
  createdAt: string;
  isRead: boolean;
}

export interface NotificationPreference {
  category: NotificationCategory;
  emailEnabled: boolean;
  mandatory: boolean;
}
