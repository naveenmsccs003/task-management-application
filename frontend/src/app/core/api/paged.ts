/** Server-side page of results (docs/api-design.md §1). */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}
