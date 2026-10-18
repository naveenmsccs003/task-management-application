import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { NotificationsService } from '../notifications/notifications.service';
import { ActiveSessionService } from '../work/active-session.service';
import { formatClock } from '../time-format';

@Component({
  selector: 'app-shell',
  imports: [MatBadgeModule, MatButtonModule, MatMenuModule, MatToolbarModule, RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main-content">Skip to main content</a>
    <mat-toolbar role="banner">
      <a class="brand" routerLink="/">Task Management</a>
      @if (auth.me(); as me) {
        <nav aria-label="Main">
          @if (auth.hasPermission('projects.view', 'Own')) {
            <a mat-button routerLink="/projects" routerLinkActive="active">Projects</a>
          }
          @if (auth.hasPermission('tasks.view', 'Own')) {
            <a mat-button routerLink="/tasks" routerLinkActive="active">Tasks</a>
          }
          @if (auth.hasPermission('members.view')) {
            <a mat-button routerLink="/admin/members" routerLinkActive="active">Members</a>
          }
          @if (auth.hasPermission('roles.view')) {
            <a mat-button routerLink="/admin/roles" routerLinkActive="active">Roles</a>
          }
          @if (auth.hasPermission('reassignments.approve', 'Project') || auth.hasPermission('deadlines.approve', 'Project')) {
            <a mat-button routerLink="/approvals" routerLinkActive="active">Approvals</a>
          }
          @if (auth.hasPermission('checklists.manage')) {
            <a mat-button routerLink="/checklist-templates" routerLinkActive="active">Checklists</a>
          }
          @if (auth.hasPermission('settings.manage')) {
            <a mat-button routerLink="/admin/settings" routerLinkActive="active">Settings</a>
          }
          @if (auth.hasPermission('gitlab.connections.manage')) {
            <a mat-button routerLink="/admin/gitlab" routerLinkActive="active">GitLab</a>
          }
          @if (auth.hasPermission('audit.view')) {
            <a mat-button routerLink="/admin/audit" routerLinkActive="active">Audit log</a>
          }
          @if (auth.hasPermission('system.health')) {
            <a mat-button routerLink="/admin/system-status" routerLinkActive="active">System</a>
          }
        </nav>
        <span class="spacer"></span>
        @if (auth.hasPermission('notifications.view', 'Own')) {
          <a
            mat-button
            routerLink="/notifications"
            routerLinkActive="active"
            [matBadge]="notifications.unreadCount()"
            [matBadgeHidden]="!notifications.unreadCount()"
            matBadgeColor="warn"
            [attr.aria-label]="'Notifications, ' + notifications.unreadCount() + ' unread'"
            data-testid="notifications-link"
          >
            Notifications
          </a>
        }
        <button mat-button [matMenuTriggerFor]="userMenu" aria-label="Account menu">{{ me.fullName }}</button>
        <mat-menu #userMenu="matMenu">
          <a mat-menu-item routerLink="/profile">My profile</a>
          <button mat-menu-item type="button" (click)="signOut()">Sign out</button>
        </mat-menu>
      }
    </mat-toolbar>
    @if (activeSession.session(); as s) {
      <div class="timer" role="status" aria-live="off">
        Working on
        <a [routerLink]="['/tasks', s.taskId]">{{ s.taskKey }} · {{ s.taskTitle }}</a>
        <span class="clock" data-testid="work-timer">{{ clock(activeSession.elapsedSeconds()) }}</span>
      </div>
    }
    <main id="main-content" tabindex="-1">
      <router-outlet />
    </main>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-height: 100%;
    }
    mat-toolbar {
      gap: 8px;
      flex-wrap: wrap;
      height: auto;
      min-height: 64px;
    }
    .brand {
      color: inherit;
      text-decoration: none;
      font: var(--mat-sys-title-large);
      margin-right: 16px;
    }
    .spacer {
      flex: 1;
    }
    .timer {
      display: flex;
      gap: 8px;
      align-items: center;
      flex-wrap: wrap;
      padding: 6px 16px;
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }
    .timer a {
      color: inherit;
      font-weight: 500;
    }
    .clock {
      font-variant-numeric: tabular-nums;
    }
    nav a.active {
      text-decoration: underline;
    }
    main {
      flex: 1;
      padding: 24px 16px;
      max-width: 1200px;
      width: 100%;
      box-sizing: border-box;
      margin: 0 auto;
    }
    main:focus {
      outline: none;
    }
    .skip-link {
      position: absolute;
      left: -9999px;
    }
    .skip-link:focus {
      left: 16px;
      top: 8px;
      z-index: 10;
      padding: 8px 12px;
      background: var(--mat-sys-surface);
      color: var(--mat-sys-on-surface);
    }
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly activeSession = inject(ActiveSessionService);
  protected readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);

  constructor() {
    // Show the running timer after sign-in or a page reload; clear it on sign-out.
    effect(() => {
      if (this.auth.me()) {
        void this.activeSession.refresh();
        void this.notifications.connect();
      } else {
        this.activeSession.clear();
        void this.notifications.disconnect();
      }
    });
  }

  protected clock(seconds: number): string {
    return formatClock(seconds);
  }

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/sign-in']);
  }
}
