import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { asApiError } from '../../core/api/api-error';
import { SYSTEM_INFO_URL, SystemInfo } from '../../core/system/system-info';

/** Temporary landing page (Phase 3): proves the browser → proxy → API → database path works. */
@Component({
  selector: 'app-home',
  imports: [DatePipe, MatCardModule, MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Task Management</h1>
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>API status</mat-card-title>
      </mat-card-header>
      <mat-card-content aria-live="polite">
        @if (systemInfo.isLoading()) {
          <mat-spinner diameter="32" aria-label="Checking the API" />
        } @else if (systemInfo.hasValue()) {
          <p class="status ok">Connected</p>
          <dl>
            <dt>Version</dt>
            <dd data-testid="api-version">{{ systemInfo.value().version }}</dd>
            <dt>Server time</dt>
            <dd>{{ systemInfo.value().serverTimeUtc | date: 'medium' }}</dd>
          </dl>
        } @else if (error(); as e) {
          <p class="status error" role="alert">{{ e.message }}</p>
          @if (e.traceId) {
            <p class="trace">Reference: <code>{{ e.traceId }}</code></p>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card {
      max-width: 480px;
    }
    .status.ok {
      color: var(--mat-sys-primary);
    }
    .status.error {
      color: var(--mat-sys-error);
    }
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 16px;
    }
    dd {
      margin: 0;
    }
    .trace {
      font: var(--mat-sys-body-small);
    }
  `,
})
export class Home {
  protected readonly systemInfo = httpResource<SystemInfo>(() => SYSTEM_INFO_URL);

  protected readonly error = computed(() => {
    const error = this.systemInfo.error();
    return error ? asApiError(error) : null;
  });
}
