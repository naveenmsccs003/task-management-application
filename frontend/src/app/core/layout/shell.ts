import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-shell',
  imports: [MatToolbarModule, RouterLink, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main-content">Skip to main content</a>
    <mat-toolbar role="banner">
      <a class="brand" routerLink="/">Task Management</a>
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
    .brand {
      color: inherit;
      text-decoration: none;
      font: var(--mat-sys-title-large);
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
export class Shell {}
