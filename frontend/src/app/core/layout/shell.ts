import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../auth/auth.service';

@Component({
  selector: 'app-shell',
  imports: [MatButtonModule, MatMenuModule, MatToolbarModule, RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main-content">Skip to main content</a>
    <mat-toolbar role="banner">
      <a class="brand" routerLink="/">Task Management</a>
      @if (auth.me(); as me) {
        <nav aria-label="Main">
          @if (auth.hasPermission('members.view')) {
            <a mat-button routerLink="/admin/members" routerLinkActive="active">Members</a>
          }
          @if (auth.hasPermission('roles.view')) {
            <a mat-button routerLink="/admin/roles" routerLinkActive="active">Roles</a>
          }
          @if (auth.hasPermission('settings.manage')) {
            <a mat-button routerLink="/admin/settings" routerLinkActive="active">Settings</a>
          }
          @if (auth.hasPermission('audit.view')) {
            <a mat-button routerLink="/admin/audit" routerLinkActive="active">Audit log</a>
          }
        </nav>
        <span class="spacer"></span>
        <button mat-button [matMenuTriggerFor]="userMenu" aria-label="Account menu">{{ me.fullName }}</button>
        <mat-menu #userMenu="matMenu">
          <a mat-menu-item routerLink="/profile">My profile</a>
          <button mat-menu-item type="button" (click)="signOut()">Sign out</button>
        </mat-menu>
      }
    </mat-toolbar>
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
  private readonly router = inject(Router);

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/sign-in']);
  }
}
