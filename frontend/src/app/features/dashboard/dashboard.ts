import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { asApiError } from '../../core/api/api-error';
import { AuthService } from '../../core/auth/auth.service';
import { formatDuration } from '../../core/time-format';
import { DASHBOARD_URL } from '../reports/reports.api';
import { Dashboard, DashboardList, ProjectProgress, StatusCount, WeekCount } from '../reports/reports.models';
import { TASK_STATUS_LABELS } from '../tasks/tasks.models';

/** Trend chart geometry: one thin bar per week, 2px gaps, anchored to the baseline. */
const BAR_WIDTH = 14;
const BAR_GAP = 4;
const CHART_HEIGHT = 40;

/**
 * The role dashboard (FR-RPT-01..04). Sections come from the API by what the member may do: My work, Team, Projects
 * and Administration. Members without dashboard.view just get the greeting.
 */
@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, NgTemplateOutlet, MatCardModule, MatProgressBarModule, MatProgressSpinnerModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Welcome{{ auth.me() ? ', ' + auth.me()!.fullName : '' }}</h1>
    @if (dashboard.isLoading() && !dashboard.hasValue()) {
      <mat-spinner diameter="32" aria-label="Loading your dashboard" />
    } @else if (error(); as e) {
      <p class="error" role="alert">{{ e.message }}</p>
    } @else if (data(); as d) {
      @if (d.myWork; as w) {
        <section aria-labelledby="my-work">
          <h2 id="my-work">My work</h2>
          <div class="tiles">
            <div class="tile"><span class="value" data-testid="hours-this-week">{{ duration(w.secondsThisWeek) }}</span><span class="label">this week</span></div>
            <div class="tile"><span class="value">{{ w.overdue.total }}</span><span class="label">overdue</span></div>
            <div class="tile"><span class="value">{{ w.dueSoon.total }}</span><span class="label">due in the next 3 days</span></div>
            <div class="tile"><span class="value">{{ w.waitingForFeedback.total }}</span><span class="label">waiting for feedback</span></div>
            <div class="tile"><span class="value">{{ w.revisionRequested.total }}</span><span class="label">revision requested</span></div>
          </div>
          @if (w.activeSession; as s) {
            <p>Working on <a [routerLink]="['/tasks', s.taskId]">{{ s.taskKey }} · {{ s.taskTitle }}</a> since {{ s.startedAt | date: 'shortTime' }}.</p>
          }
          @if (w.byStatus.length) {
            <p class="muted">My open tasks: {{ statuses(w.byStatus) }}</p>
          }
          <div class="lists">
            <ng-container *ngTemplateOutlet="taskList; context: { title: 'Overdue', list: w.overdue, assignee: false }" />
            <ng-container *ngTemplateOutlet="taskList; context: { title: 'Due soon', list: w.dueSoon, assignee: false }" />
            <ng-container *ngTemplateOutlet="taskList; context: { title: 'Waiting for feedback', list: w.waitingForFeedback, assignee: false }" />
            <ng-container *ngTemplateOutlet="taskList; context: { title: 'Revision requested', list: w.revisionRequested, assignee: false }" />
          </div>
        </section>
      }

      @if (d.team; as t) {
        <section aria-labelledby="team">
          <h2 id="team">Team</h2>
          <div class="tiles">
            <div class="tile"><span class="value">{{ t.algorithmApprovals.total }}</span><span class="label">algorithms to approve</span></div>
            <div class="tile"><span class="value">{{ t.reviews.total }}</span><span class="label">reviews</span></div>
            <div class="tile"><span class="value">{{ t.overAllocatedTotal }}</span><span class="label">over allocated hours</span></div>
            <div class="tile"><span class="value">{{ t.overdue.total }}</span><span class="label">overdue</span></div>
            <a class="tile link" routerLink="/approvals"><span class="value">{{ t.pendingRequests }}</span><span class="label">pending requests</span></a>
          </div>
          <div class="lists">
            <ng-container *ngTemplateOutlet="taskList; context: { title: 'Algorithms to approve', list: t.algorithmApprovals, assignee: true }" />
            <ng-container *ngTemplateOutlet="taskList; context: { title: 'Reviews', list: t.reviews, assignee: true }" />
            <ng-container *ngTemplateOutlet="taskList; context: { title: 'Overdue', list: t.overdue, assignee: true }" />
            @if (t.overAllocated.length) {
              <mat-card appearance="outlined">
                <mat-card-header><mat-card-title>Over allocated hours</mat-card-title></mat-card-header>
                <mat-card-content>
                  <ul>
                    @for (o of t.overAllocated; track o.id) {
                      <li>
                        <a [routerLink]="['/tasks', o.id]">{{ o.key }} · {{ o.title }}</a>
                        <span class="muted"> — {{ duration(o.spentSeconds) }} of {{ duration(o.allocatedSeconds) }}{{ o.assignee ? ', ' + o.assignee.name : '' }}</span>
                      </li>
                    }
                  </ul>
                </mat-card-content>
              </mat-card>
            }
          </div>
          @if (t.workload.length) {
            <table class="grid" aria-label="Team workload">
              <thead>
                <tr><th scope="col">Member</th><th scope="col" class="num">Open tasks</th><th scope="col" class="num">In progress</th><th scope="col" class="num">This week</th></tr>
              </thead>
              <tbody>
                @for (m of t.workload; track m.member.id) {
                  <tr>
                    <td>{{ m.member.name }}</td>
                    <td class="num">{{ m.openTasks }}</td>
                    <td class="num">{{ m.inProgress }}</td>
                    <td class="num">{{ duration(m.secondsThisWeek) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </section>
      }

      @if (d.projects; as p) {
        <section aria-labelledby="projects">
          <h2 id="projects">Projects</h2>
          <div class="projects">
            @for (project of p.items; track project.id) {
              <mat-card appearance="outlined" [attr.data-testid]="'project-' + project.key">
                <mat-card-header>
                  <mat-card-title><a [routerLink]="['/projects', project.id]">{{ project.key }} · {{ project.name }}</a></mat-card-title>
                  @if (project.status !== 'Active') {
                    <mat-card-subtitle>{{ project.status === 'OnHold' ? 'On hold' : project.status }}</mat-card-subtitle>
                  }
                </mat-card-header>
                <mat-card-content>
                  <p class="muted">{{ project.byStatus.length ? statuses(project.byStatus) : 'No tasks yet' }}</p>
                  <p>{{ timeUsed(project) }}</p>
                  @if (project.allocatedSeconds) {
                    <mat-progress-bar
                      mode="determinate"
                      [value]="percent(project.spentSeconds, project.allocatedSeconds)"
                      [attr.aria-label]="'Time used: ' + percent(project.spentSeconds, project.allocatedSeconds) + '% of allocated hours'"
                    />
                  }
                  <dl>
                    <dt>Overdue now</dt>
                    <dd>{{ project.overdue }}</dd>
                    <dt>Extensions approved</dt>
                    <dd>{{ project.extensionsApproved }}</dd>
                    <dt>Revisions requested</dt>
                    <dd>{{ project.revisionsRequested }}</dd>
                  </dl>
                  <figure class="trend">
                    <figcaption class="muted">Overdue tasks, last {{ project.overdueTrend.length }} weeks</figcaption>
                    <svg [attr.width]="trendWidth(project.overdueTrend)" [attr.height]="chartHeight + 1" role="img" [attr.aria-label]="trendSummary(project.overdueTrend)">
                      @for (bar of bars(project.overdueTrend); track bar.week.weekStart) {
                        <g class="bar">
                          <title>Week of {{ bar.week.weekStart | date: 'mediumDate' }}: {{ bar.week.count }} overdue</title>
                          <rect class="hit" [attr.x]="bar.x - 2" y="0" [attr.width]="barWidth + 4" [attr.height]="chartHeight" />
                          <rect class="mark" [attr.x]="bar.x" [attr.y]="chartHeight - bar.height" [attr.width]="barWidth" [attr.height]="bar.height" rx="2" />
                        </g>
                      }
                      <line class="baseline" x1="0" [attr.x2]="trendWidth(project.overdueTrend)" [attr.y1]="chartHeight + 0.5" [attr.y2]="chartHeight + 0.5" />
                    </svg>
                  </figure>
                </mat-card-content>
              </mat-card>
            }
          </div>
          @if (p.total > p.items.length) {
            <p class="muted">{{ p.total - p.items.length }} more — see <a routerLink="/projects">all projects</a>.</p>
          }
        </section>
      }

      @if (d.admin; as a) {
        <section aria-labelledby="administration">
          <h2 id="administration">Administration</h2>
          <div class="tiles">
            <div class="tile"><span class="value">{{ a.activeMembers }}</span><span class="label">active members</span></div>
            <div class="tile"><span class="value">{{ a.invitedMembers }}</span><span class="label">invited, not signed up</span></div>
            <div class="tile"><span class="value">{{ a.lockedMembers }}</span><span class="label">locked out now</span></div>
            <div class="tile"><span class="value">{{ a.failedSignIns24Hours }}</span><span class="label">failed sign-ins (24 h; {{ a.failedSignIns7Days }} in 7 days)</span></div>
            <a class="tile link" routerLink="/admin/system-status"><span class="value">{{ a.emailsFailed }}</span><span class="label">failed emails ({{ a.emailsPending }} waiting)</span></a>
          </div>
          <div class="lists">
            <mat-card appearance="outlined">
              <mat-card-header><mat-card-title>Background jobs</mat-card-title></mat-card-header>
              <mat-card-content>
                <ul>
                  @for (job of a.jobs; track job.jobName) {
                    <li>
                      <strong [class.bad]="!job.lastSucceeded">{{ job.lastSucceeded ? 'OK' : 'Failed' }}</strong>
                      {{ job.jobName }} <span class="muted">— last run {{ job.lastFinishedAt | date: 'short' }}</span>
                    </li>
                  } @empty {
                    <li class="muted">No job has run yet.</li>
                  }
                </ul>
              </mat-card-content>
            </mat-card>
            <mat-card appearance="outlined">
              <mat-card-header><mat-card-title>GitLab connections</mat-card-title></mat-card-header>
              <mat-card-content>
                <ul>
                  @for (c of a.gitLab; track c.id) {
                    <li>
                      <strong [class.bad]="c.status === 'Unhealthy'">{{ c.status === 'Healthy' ? 'Working' : c.status === 'Unhealthy' ? 'Failing' : 'Not tested' }}</strong>
                      {{ c.name }}
                      @if (c.lastError) {
                        <span class="muted">— {{ c.lastError }}</span>
                      }
                    </li>
                  } @empty {
                    <li class="muted">No GitLab connections.</li>
                  }
                </ul>
              </mat-card-content>
            </mat-card>
          </div>
        </section>
      }
    }

    <ng-template #taskList let-title="title" let-list="list" let-assignee="assignee">
      @if (asList(list).total) {
        <mat-card appearance="outlined">
          <mat-card-header><mat-card-title>{{ title }}</mat-card-title></mat-card-header>
          <mat-card-content>
            <ul [attr.aria-label]="title">
              @for (task of asList(list).items; track task.id) {
                <li>
                  <a [routerLink]="['/tasks', task.id]">{{ task.key }} · {{ task.title }}</a>
                  <span class="muted">
                    — {{ statusLabels[task.status] }}{{ task.dueDate ? ', due ' + task.dueDate : '' }}{{ assignee && task.assignee ? ', ' + task.assignee.name : '' }}
                  </span>
                </li>
              }
            </ul>
            @if (asList(list).total > asList(list).items.length) {
              <p class="muted">and {{ asList(list).total - asList(list).items.length }} more</p>
            }
          </mat-card-content>
        </mat-card>
      }
    </ng-template>
  `,
  styles: `
    section {
      margin-bottom: 32px;
    }
    .tiles {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
      gap: 12px;
      margin-bottom: 16px;
    }
    .tile {
      display: flex;
      flex-direction: column;
      padding: 12px 16px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 12px;
      color: inherit;
      text-decoration: none;
    }
    .tile.link:hover,
    .tile.link:focus-visible {
      border-color: var(--mat-sys-primary);
    }
    .value {
      font: var(--mat-sys-headline-small);
    }
    .label {
      color: var(--mat-sys-on-surface-variant);
    }
    .lists,
    .projects {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 16px;
      margin-bottom: 16px;
    }
    ul {
      padding-left: 20px;
      margin: 0;
    }
    li {
      margin: 4px 0;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
    .error,
    .bad {
      color: var(--mat-sys-error);
    }
    dl {
      display: grid;
      grid-template-columns: 1fr max-content;
      gap: 2px 16px;
    }
    dd {
      margin: 0;
      text-align: right;
    }
    .grid {
      border-collapse: collapse;
      min-width: 50%;
    }
    .grid th,
    .grid td {
      text-align: left;
      padding: 6px 12px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .grid .num {
      text-align: right;
    }
    figure {
      margin: 8px 0 0;
    }
    .trend .mark {
      fill: var(--mat-sys-primary);
    }
    .trend .hit {
      fill: transparent;
    }
    .trend .bar:hover .mark {
      fill: var(--mat-sys-on-primary-container);
    }
    .trend .baseline {
      stroke: var(--mat-sys-outline-variant);
    }
  `,
})
export class DashboardPage {
  protected readonly auth = inject(AuthService);
  protected readonly statusLabels = TASK_STATUS_LABELS;
  protected readonly barWidth = BAR_WIDTH;
  protected readonly chartHeight = CHART_HEIGHT;

  protected readonly dashboard = httpResource<Dashboard>(() => (this.auth.hasPermission('dashboard.view', 'Own') ? DASHBOARD_URL : undefined));
  protected readonly data = computed(() => (this.dashboard.hasValue() ? this.dashboard.value() : null));
  protected readonly error = computed(() => {
    const error = this.dashboard.error();
    return error ? asApiError(error) : null;
  });

  protected duration(seconds: number): string {
    return formatDuration(seconds);
  }

  protected timeUsed(project: ProjectProgress): string {
    const spent = `${formatDuration(project.spentSeconds)} spent`;
    return project.allocatedSeconds ? `${spent} of ${formatDuration(project.allocatedSeconds)} allocated` : spent;
  }

  protected percent(spent: number, allocated: number): number {
    return allocated ? Math.round((spent * 100) / allocated) : 0;
  }

  protected statuses(counts: StatusCount[]): string {
    return counts.map((c) => `${TASK_STATUS_LABELS[c.status]} ${c.count}`).join(' · ');
  }

  protected asList(list: DashboardList): DashboardList {
    return list;
  }

  protected trendWidth(weeks: WeekCount[]): number {
    return weeks.length * (BAR_WIDTH + BAR_GAP);
  }

  /** Bars scaled to the busiest week; an empty week keeps a 1px stub so the week is still visible. */
  protected bars(weeks: WeekCount[]): { week: WeekCount; x: number; height: number }[] {
    const max = Math.max(1, ...weeks.map((w) => w.count));
    return weeks.map((week, i) => ({
      week,
      x: i * (BAR_WIDTH + BAR_GAP) + BAR_GAP / 2,
      height: week.count ? Math.max(4, Math.round((week.count / max) * CHART_HEIGHT)) : 1,
    }));
  }

  protected trendSummary(weeks: WeekCount[]): string {
    return `Overdue tasks at the end of each of the last ${weeks.length} weeks, oldest first: ${weeks.map((w) => w.count).join(', ')}`;
  }
}
