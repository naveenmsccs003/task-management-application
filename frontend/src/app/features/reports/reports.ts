import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { formatDuration } from '../../core/time-format';
import { ProjectsApi } from '../projects/projects.api';
import { ProjectListItem } from '../projects/projects.models';
import { ReportsApi } from './reports.api';
import { formatInstant, GROUP_BY_LABELS, ReportCell, ReportColumn, ReportInfo, ReportQuery, ReportTable } from './reports.models';

/** "2026-10-18" for a date input, from a local calendar date. */
function isoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * Reports (FR-RPT-05..07, FR-TIME-10): pick a report, a period and a project, view it as a table and export it as CSV
 * when allowed. Only reports the member may run are offered; the server limits the rows to what they may see.
 */
@Component({
  selector: 'app-reports',
  imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Reports</h1>
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (reports().length) {
      <form class="filters" (ngSubmit)="run()" aria-label="Report filters">
        <mat-form-field>
          <mat-label>Report</mat-label>
          <mat-select name="report" [ngModel]="name()" (ngModelChange)="choose($event)">
            @for (r of reports(); track r.name) {
              <mat-option [value]="r.name">{{ r.title }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        @if (selected()?.usesDateRange) {
          <mat-form-field>
            <mat-label>From</mat-label>
            <input matInput type="date" name="from" [(ngModel)]="from" required />
          </mat-form-field>
          <mat-form-field>
            <mat-label>To</mat-label>
            <input matInput type="date" name="to" [(ngModel)]="to" required />
          </mat-form-field>
        }
        <mat-form-field>
          <mat-label>Project</mat-label>
          <mat-select name="projectId" [(ngModel)]="projectId">
            <mat-option value="">All my projects</mat-option>
            @for (p of projects(); track p.id) {
              <mat-option [value]="p.id">{{ p.key }} · {{ p.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        @if (selected()?.groupBy?.length) {
          <mat-form-field>
            <mat-label>Group by</mat-label>
            <mat-select name="groupBy" [(ngModel)]="groupBy">
              @for (g of selected()!.groupBy; track g) {
                <mat-option [value]="g">{{ groupLabels[g] ?? g }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
        <button mat-flat-button type="submit" [disabled]="busy()">Show report</button>
        @if (selected()?.canExport && table()) {
          <a mat-stroked-button [href]="csvUrl()" download>Export CSV</a>
        }
      </form>
      @if (selected(); as r) {
        <p class="muted">{{ r.description }}</p>
      }
    } @else if (!busy()) {
      <p class="muted">There are no reports you can run.</p>
    }

    @if (table(); as t) {
      <h2>{{ t.title }}</h2>
      <p class="muted">
        @if (t.from) {
          {{ t.from }} to {{ t.to }} ·
        }
        {{ t.rows.length }} {{ t.rows.length === 1 ? 'row' : 'rows' }} · times in {{ t.timeZone }}
      </p>
      @if (t.truncated) {
        <p class="warning" role="status">Only the first {{ t.rows.length }} rows are shown. Choose a shorter period or a project.</p>
      }
      <div class="scroll">
        <table class="report" [attr.aria-label]="t.title">
          <thead>
            <tr>
              @for (c of t.columns; track c.key) {
                <th scope="col" [class.num]="isNumeric(c)">{{ c.label }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of t.rows; track $index) {
              <tr>
                @for (c of t.columns; track c.key; let i = $index) {
                  <td [class.num]="isNumeric(c)">
                    @if (c.kind === 'Task' && taskRef(row[i]); as task) {
                      <a [routerLink]="['/tasks', task.id]">{{ task.key }}</a>
                    } @else {
                      {{ format(c, row[i], t.timeZone) }}
                    }
                  </td>
                }
              </tr>
            } @empty {
              <tr>
                <td [attr.colspan]="t.columns.length" class="muted">Nothing to report for these filters.</td>
              </tr>
            }
          </tbody>
          @if (totals(); as sums) {
            <tfoot>
              <tr>
                @for (c of t.columns; track c.key; let i = $index) {
                  <td [class.num]="isNumeric(c)">{{ i === 0 ? 'Total' : sums[i] === null ? '' : duration(sums[i]!) }}</td>
                }
              </tr>
            </tfoot>
          }
        </table>
      </div>
    }
  `,
  styles: `
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
    }
    .scroll {
      overflow-x: auto;
    }
    .report {
      border-collapse: collapse;
      min-width: 60%;
    }
    .report th,
    .report td {
      text-align: left;
      padding: 6px 12px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      vertical-align: top;
    }
    .report .num {
      text-align: right;
      white-space: nowrap;
    }
    .report tfoot td {
      font-weight: 600;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
    }
    .warning {
      font-weight: 600;
    }
  `,
})
export class Reports implements OnInit {
  private readonly api = inject(ReportsApi);
  private readonly projectsApi = inject(ProjectsApi);

  protected readonly groupLabels = GROUP_BY_LABELS;
  protected readonly reports = signal<ReportInfo[]>([]);
  protected readonly projects = signal<ProjectListItem[]>([]);
  protected readonly name = signal<string>('');
  protected readonly table = signal<ReportTable | null>(null);
  protected readonly busy = signal(true);
  protected readonly failure = signal<string | null>(null);
  protected readonly selected = computed(() => this.reports().find((r) => r.name === this.name()) ?? null);

  protected from = isoDate(new Date(Date.now() - 29 * 24 * 3600 * 1000));
  protected to = isoDate(new Date());
  protected projectId = '';
  protected groupBy = '';

  /** A total row for reports with time columns (Number columns such as revisions are not summed). */
  protected readonly totals = computed(() => {
    const table = this.table();
    if (!table?.rows.length || !table.columns.some((c) => c.kind === 'Duration')) {
      return null;
    }
    return table.columns.map((c, i) => (c.kind === 'Duration' ? table.rows.reduce((sum, row) => sum + Number(row[i] ?? 0), 0) : null));
  });

  async ngOnInit(): Promise<void> {
    try {
      const [reports, projects] = await Promise.all([
        firstValueFrom(this.api.list()),
        firstValueFrom(this.projectsApi.list({ page: 1, pageSize: 100 })),
      ]);
      this.reports.set(reports);
      this.projects.set(projects.items);
      if (reports.length) {
        this.choose(reports[0].name);
      }
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected choose(name: string): void {
    this.name.set(name);
    this.groupBy = this.selected()?.groupBy[0] ?? '';
    this.table.set(null);
  }

  protected async run(): Promise<void> {
    this.busy.set(true);
    this.failure.set(null);
    try {
      this.table.set(await firstValueFrom(this.api.run(this.name(), this.query())));
    } catch (error) {
      this.table.set(null);
      this.failure.set(asApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected csvUrl(): string {
    return this.api.csvUrl(this.name(), this.query());
  }

  protected isNumeric(column: ReportColumn): boolean {
    return column.kind === 'Number' || column.kind === 'Duration';
  }

  protected taskRef(cell: ReportCell): { id: string; key: string } | null {
    return cell && typeof cell === 'object' ? cell : null;
  }

  protected duration(seconds: number): string {
    return formatDuration(seconds);
  }

  protected format(column: ReportColumn, cell: ReportCell, timeZone: string): string {
    if (cell === null || cell === undefined) {
      return '';
    }
    switch (column.kind) {
      case 'Duration':
        return formatDuration(Number(cell));
      case 'DateTime':
        return formatInstant(String(cell), timeZone);
      case 'Number':
        return column.key === 'percent' ? `${cell} %` : String(cell);
      default:
        return typeof cell === 'object' ? cell.key : String(cell);
    }
  }

  private query(): ReportQuery {
    const report = this.selected();
    return {
      from: report?.usesDateRange ? this.from : undefined,
      to: report?.usesDateRange ? this.to : undefined,
      projectId: this.projectId || undefined,
      groupBy: report?.groupBy.length ? this.groupBy : undefined,
    };
  }
}
