import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { asApiError } from '../../core/api/api-error';
import { GitLabApi } from './gitlab.api';
import {
  describeRun,
  GitLabConnectionOption,
  GitLabIssuePage,
  GitLabProject,
  GitLabProjectLink,
  ImportRun,
  WebhookSecret,
} from './gitlab.models';

type IssueState = 'opened' | 'closed' | 'all';

/**
 * The project's GitLab tab (FR-IMP-03..08): link a GitLab project, pick issues to import as Draft tasks, sync now,
 * set up the webhook, and see past runs. Imported issues are marked and cannot be picked again.
 */
@Component({
  selector: 'app-project-gitlab',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (failure(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (loaded()) {
      @if (link(); as l) {
        <section>
          <p>
            Linked to <a [href]="l.webUrl" target="_blank" rel="noopener noreferrer">{{ l.pathWithNamespace }}</a>
            through {{ l.connectionName }}.
            <span class="muted">
              @if (l.lastSyncedAt) {
                Last synced {{ l.lastSyncedAt | date: 'short' }}.
              } @else {
                Not synced yet.
              }
              {{ l.syncEnabled ? 'Synced automatically every 15 minutes.' : 'Automatic sync is off.' }}
            </span>
          </p>
          @if (!readOnly()) {
            <div class="actions">
              <button mat-stroked-button type="button" (click)="sync()" [disabled]="busy()">Sync now</button>
              <button mat-button type="button" (click)="newSecret()" [disabled]="busy()">
                {{ l.hasWebhookSecret ? 'Replace webhook secret' : 'Set up a webhook' }}
              </button>
              <button mat-button type="button" (click)="unlink()" [disabled]="busy()">Unlink</button>
            </div>
          }
          @if (secret(); as s) {
            <div class="secret" role="status">
              <p>In GitLab, add a webhook for <strong>Issues events</strong> with:</p>
              <p>URL: <code>{{ origin + s.path }}</code></p>
              <p>Secret token: <code data-testid="webhook-secret">{{ s.secret }}</code></p>
              <p class="muted">Copy the secret now — it will not be shown again.</p>
            </div>
          }
        </section>

        <h3>Issues</h3>
        <form class="filters" (ngSubmit)="loadIssues(1)" aria-label="Find GitLab issues">
          <mat-form-field>
            <mat-label>State</mat-label>
            <mat-select name="state" [(ngModel)]="state" (selectionChange)="loadIssues(1)">
              <mat-option value="opened">Open</mat-option>
              <mat-option value="closed">Closed</mat-option>
              <mat-option value="all">All</mat-option>
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Search issues</mat-label>
            <input matInput name="search" [(ngModel)]="search" />
          </mat-form-field>
          <button mat-button type="submit">Search</button>
        </form>
        @if (issues(); as page) {
          <table class="issues" aria-label="GitLab issues">
            <thead>
              <tr><th></th><th>Issue</th><th>Labels</th><th>Milestone due</th><th>Task</th></tr>
            </thead>
            <tbody>
              @for (i of page.items; track i.iid) {
                <tr>
                  <td>
                    <mat-checkbox
                      [checked]="selected().has(i.iid)"
                      [disabled]="!!i.taskId || i.isClosed || readOnly()"
                      (change)="toggle(i.iid, $event.checked)"
                      [aria-label]="'Select #' + i.iid + ' ' + i.title"
                    />
                  </td>
                  <td>
                    <a [href]="i.webUrl" target="_blank" rel="noopener noreferrer">#{{ i.iid }}</a> {{ i.title }}
                    @if (i.isClosed) {
                      <span class="muted">(closed)</span>
                    }
                  </td>
                  <td>{{ i.labels.join(', ') }}</td>
                  <td>{{ i.milestoneDueDate ?? '' }}</td>
                  <td>
                    @if (i.taskId) {
                      <a [routerLink]="['/tasks', i.taskId]">{{ i.taskKey }}</a>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="muted">No issues found.</td></tr>
              }
            </tbody>
          </table>
          <div class="actions">
            @if (page.page > 1) {
              <button mat-button type="button" (click)="loadIssues(page.page - 1)">Previous</button>
            }
            @if (page.hasMore) {
              <button mat-button type="button" (click)="loadIssues(page.page + 1)">Next</button>
            }
            @if (!readOnly()) {
              <button mat-flat-button type="button" (click)="importSelected()" [disabled]="busy() || !selected().size">
                Import {{ selected().size || '' }} as draft tasks
              </button>
            }
          </div>
        }

        <h3>Recent imports and syncs</h3>
        <ul class="runs">
          @for (r of runs(); track r.id) {
            <li>
              <strong>{{ r.kind }}</strong> {{ r.startedAt | date: 'short' }}
              {{ r.startedBy ? 'by ' + r.startedBy.name : '(automatic)' }} — {{ describe(r) }}
              @for (e of r.errors; track $index) {
                <div class="error">{{ e }}</div>
              }
            </li>
          } @empty {
            <li class="muted">Nothing imported yet.</li>
          }
        </ul>
      } @else {
        <p class="muted">This project is not linked to GitLab. Link it to import issues as draft tasks.</p>
        @if (!readOnly()) {
          <form class="link" (ngSubmit)="findProjects()" aria-label="Link to GitLab">
            <mat-form-field>
              <mat-label>GitLab connection</mat-label>
              <mat-select name="connection" [(ngModel)]="connectionId" required>
                @for (c of connectionOptions(); track c.id) {
                  <mat-option [value]="c.id">{{ c.name }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Find GitLab project</mat-label>
              <input matInput name="projectSearch" [(ngModel)]="projectSearch" />
            </mat-form-field>
            <button mat-stroked-button type="submit" [disabled]="busy() || !connectionId">Find</button>
          </form>
          @if (!connectionOptions().length) {
            <p class="muted">No GitLab connections exist yet. Ask an Admin to add one.</p>
          }
          <ul class="projects" aria-label="GitLab projects">
            @for (p of remoteProjects(); track p.id) {
              <li>
                {{ p.pathWithNamespace }}
                <button mat-button type="button" (click)="linkTo(p)" [disabled]="busy()" [attr.aria-label]="'Link ' + p.pathWithNamespace">Link</button>
              </li>
            }
          </ul>
        }
      }
    }
  `,
  styles: `
    .actions,
    .filters,
    .link {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: center;
    }
    .issues {
      width: 100%;
      border-collapse: collapse;
    }
    .issues th,
    .issues td {
      text-align: left;
      padding: 4px 8px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .secret {
      padding: 8px 12px;
      border-radius: 4px;
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    code {
      word-break: break-all;
    }
    .runs {
      padding-left: 16px;
    }
    .muted {
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class ProjectGitLab implements OnInit {
  private readonly api = inject(GitLabApi);

  readonly projectId = input.required<string>();
  readonly readOnly = input(false);

  protected readonly origin = globalThis.location?.origin ?? '';
  protected readonly loaded = signal(false);
  protected readonly link = signal<GitLabProjectLink | null>(null);
  protected readonly connectionOptions = signal<GitLabConnectionOption[]>([]);
  protected readonly remoteProjects = signal<GitLabProject[]>([]);
  protected readonly issues = signal<GitLabIssuePage | null>(null);
  protected readonly selected = signal(new Set<number>());
  protected readonly runs = signal<ImportRun[]>([]);
  protected readonly secret = signal<WebhookSecret | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected connectionId = '';
  protected projectSearch = '';
  protected state: IssueState = 'opened';
  protected search = '';

  async ngOnInit(): Promise<void> {
    await this.run(async () => {
      this.link.set(await firstValueFrom(this.api.link(this.projectId())));
      if (this.link()) {
        await this.loadLinked();
      } else {
        this.connectionOptions.set(await firstValueFrom(this.api.connectionOptions()));
        this.connectionId = this.connectionOptions()[0]?.id ?? '';
      }
    });
    this.loaded.set(true);
  }

  protected describe(run: ImportRun): string {
    return describeRun(run);
  }

  protected toggle(iid: number, checked: boolean): void {
    this.selected.update((set) => {
      const next = new Set(set);
      if (checked) next.add(iid);
      else next.delete(iid);
      return next;
    });
  }

  protected async findProjects(): Promise<void> {
    await this.run(async () => this.remoteProjects.set(await firstValueFrom(this.api.searchProjects(this.connectionId, this.projectSearch))));
  }

  protected async linkTo(project: GitLabProject): Promise<void> {
    await this.run(async () => {
      this.link.set(await firstValueFrom(this.api.setLink(this.projectId(), this.connectionId, project.id, true)));
      this.remoteProjects.set([]);
      await this.loadLinked();
    });
  }

  protected async unlink(): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.api.unlink(this.projectId()));
      this.link.set(null);
      this.issues.set(null);
      this.secret.set(null);
      this.connectionOptions.set(await firstValueFrom(this.api.connectionOptions()));
      this.connectionId = this.connectionOptions()[0]?.id ?? '';
    });
  }

  protected async newSecret(): Promise<void> {
    await this.run(async () => {
      this.secret.set(await firstValueFrom(this.api.newWebhookSecret(this.projectId())));
      this.link.update((l) => (l ? { ...l, hasWebhookSecret: true } : l));
    });
  }

  protected async loadIssues(page: number): Promise<void> {
    await this.run(async () => this.issues.set(await firstValueFrom(this.api.issues(this.projectId(), this.state, this.search, page))));
  }

  protected async importSelected(): Promise<void> {
    await this.run(async () => {
      const run = await firstValueFrom(this.api.import(this.projectId(), [...this.selected()]));
      this.runs.update((list) => [run, ...list]);
      this.selected.set(new Set());
      this.issues.set(await firstValueFrom(this.api.issues(this.projectId(), this.state, this.search, this.issues()?.page ?? 1)));
    });
  }

  protected async sync(): Promise<void> {
    await this.run(async () => {
      const run = await firstValueFrom(this.api.sync(this.projectId()));
      this.runs.update((list) => [run, ...list]);
      this.link.set(await firstValueFrom(this.api.link(this.projectId())));
    });
  }

  private async loadLinked(): Promise<void> {
    const [issues, runs] = await Promise.all([
      firstValueFrom(this.api.issues(this.projectId(), this.state, this.search, 1)),
      firstValueFrom(this.api.runs(this.projectId())),
    ]);
    this.issues.set(issues);
    this.runs.set(runs);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.failure.set(null);
    try {
      await action();
    } catch (error) {
      this.failure.set(asApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
