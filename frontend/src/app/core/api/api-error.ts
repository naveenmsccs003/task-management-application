import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 Problem Details as returned by the API (docs/api-design.md §3). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

/** The single error shape that components see, whatever went wrong. */
export interface ApiError {
  status: number;
  code: string;
  message: string;
  traceId?: string;
  fieldErrors: Record<string, string[]>;
}

const FALLBACK_MESSAGES: Record<number, string> = {
  0: 'The server could not be reached. Check your connection and try again.',
  401: 'Please sign in to continue.',
  403: 'You do not have permission to do this.',
  404: 'The item was not found, or you do not have access to it.',
  409: 'This action conflicts with the current state. Reload and try again.',
  429: 'Too many requests. Please wait a moment and try again.',
};

export function isApiError(value: unknown): value is ApiError {
  return (
    typeof value === 'object' &&
    value !== null &&
    'status' in value &&
    'code' in value &&
    'message' in value &&
    'fieldErrors' in value
  );
}

export function toApiError(response: HttpErrorResponse): ApiError {
  const problem = isProblemDetails(response.error) ? response.error : undefined;
  const status = response.status;

  return {
    status,
    code: problem?.code ?? (status === 0 ? 'network_error' : `http_${status}`),
    message:
      problem?.title ??
      FALLBACK_MESSAGES[status] ??
      (status >= 500 ? 'An unexpected error occurred.' : 'The request could not be completed.'),
    traceId: problem?.traceId,
    fieldErrors: problem?.errors ?? {},
  };
}

function isProblemDetails(body: unknown): body is ProblemDetails {
  return typeof body === 'object' && body !== null && ('title' in body || 'status' in body);
}

/** Normalises anything an HTTP call or resource can fail with into an {@link ApiError}. */
export function asApiError(error: unknown): ApiError {
  if (isApiError(error)) {
    return error;
  }
  if (error instanceof HttpErrorResponse) {
    return toApiError(error);
  }
  return { status: 0, code: 'unknown_error', message: 'An unexpected error occurred.', fieldErrors: {} };
}
