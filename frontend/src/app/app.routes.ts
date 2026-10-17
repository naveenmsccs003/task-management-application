import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Task Management',
    canActivate: [authGuard],
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
  },
  {
    path: 'sign-in',
    title: 'Sign in · Task Management',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/sign-in').then((m) => m.SignIn),
  },
  {
    path: 'forgot-password',
    title: 'Reset password · Task Management',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    title: 'Choose a new password · Task Management',
    data: { heading: 'Choose a new password' },
    loadComponent: () => import('./features/auth/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'set-password',
    title: 'Set your password · Task Management',
    data: { heading: 'Welcome! Set your password' },
    loadComponent: () => import('./features/auth/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'profile',
    title: 'My profile · Task Management',
    canActivate: [permissionGuard('profile.manage', 'Own')],
    loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
  },
  {
    path: 'projects',
    title: 'Projects · Task Management',
    canActivate: [permissionGuard('projects.view', 'Own')],
    loadComponent: () => import('./features/projects/project-list').then((m) => m.ProjectList),
  },
  {
    path: 'projects/new',
    title: 'New project · Task Management',
    canActivate: [permissionGuard('projects.create')],
    loadComponent: () => import('./features/projects/project-form').then((m) => m.ProjectForm),
  },
  {
    path: 'projects/:id',
    title: 'Project · Task Management',
    canActivate: [authGuard],
    loadComponent: () => import('./features/projects/project-detail').then((m) => m.ProjectDetail),
  },
  {
    path: 'projects/:id/edit',
    title: 'Edit project · Task Management',
    canActivate: [authGuard],
    loadComponent: () => import('./features/projects/project-form').then((m) => m.ProjectForm),
  },
  {
    path: 'projects/:projectId/tasks/new',
    title: 'New task · Task Management',
    canActivate: [authGuard],
    loadComponent: () => import('./features/tasks/task-form').then((m) => m.TaskForm),
  },
  {
    path: 'tasks',
    title: 'Tasks · Task Management',
    canActivate: [permissionGuard('tasks.view', 'Own')],
    loadComponent: () => import('./features/tasks/task-list').then((m) => m.TaskList),
  },
  {
    path: 'tasks/:id',
    title: 'Task · Task Management',
    canActivate: [authGuard],
    loadComponent: () => import('./features/tasks/task-detail').then((m) => m.TaskDetail),
  },
  {
    path: 'tasks/:id/edit',
    title: 'Edit task · Task Management',
    canActivate: [authGuard],
    loadComponent: () => import('./features/tasks/task-form').then((m) => m.TaskForm),
  },
  {
    path: 'admin',
    children: [
      {
        path: 'members',
        title: 'Members · Task Management',
        canActivate: [permissionGuard('members.view')],
        loadComponent: () => import('./features/admin/members/member-list').then((m) => m.MemberList),
      },
      {
        path: 'members/new',
        title: 'Add member · Task Management',
        canActivate: [permissionGuard('members.create')],
        loadComponent: () => import('./features/admin/members/member-form').then((m) => m.MemberForm),
      },
      {
        path: 'members/:id',
        title: 'Member · Task Management',
        canActivate: [permissionGuard('members.view')],
        loadComponent: () => import('./features/admin/members/member-form').then((m) => m.MemberForm),
      },
      {
        path: 'roles',
        title: 'Roles · Task Management',
        canActivate: [permissionGuard('roles.view')],
        loadComponent: () => import('./features/admin/roles/roles').then((m) => m.Roles),
      },
      {
        path: 'settings',
        title: 'Settings · Task Management',
        canActivate: [permissionGuard('settings.manage')],
        loadComponent: () => import('./features/admin/settings/settings').then((m) => m.Settings),
      },
      {
        path: 'audit',
        title: 'Audit log · Task Management',
        canActivate: [permissionGuard('audit.view')],
        loadComponent: () => import('./features/admin/audit/audit-log').then((m) => m.AuditLog),
      },
    ],
  },
  {
    path: 'forbidden',
    title: 'No access · Task Management',
    loadComponent: () => import('./features/not-found/forbidden').then((m) => m.Forbidden),
  },
  {
    path: '**',
    title: 'Page not found · Task Management',
    loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound),
  },
];
