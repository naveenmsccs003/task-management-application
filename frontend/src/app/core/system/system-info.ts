/** Response of GET /api/v1/system/info. */
export interface SystemInfo {
  name: string;
  version: string;
  serverTimeUtc: string;
}

export const SYSTEM_INFO_URL = '/api/v1/system/info';
