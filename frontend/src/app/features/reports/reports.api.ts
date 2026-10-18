import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { toParams } from '../../core/api/http-params';
import { ReportInfo, ReportQuery, ReportTable } from './reports.models';

export const DASHBOARD_URL = '/api/v1/dashboard';

/** Typed calls to the report endpoints (docs/api-design.md §4.12). */
@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);

  list(): Observable<ReportInfo[]> {
    return this.http.get<ReportInfo[]>('/api/v1/reports');
  }

  run(name: string, query: ReportQuery): Observable<ReportTable> {
    return this.http.get<ReportTable>(`/api/v1/reports/${encodeURIComponent(name)}`, { params: toParams(query) });
  }

  /** A plain link (the session cookie goes with it), so the browser downloads the file itself. */
  csvUrl(name: string, query: ReportQuery): string {
    return `/api/v1/reports/${encodeURIComponent(name)}?${toParams({ ...query, format: 'csv' }).toString()}`;
  }
}
