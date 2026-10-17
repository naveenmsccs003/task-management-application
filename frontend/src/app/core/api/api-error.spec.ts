import { HttpErrorResponse } from '@angular/common/http';
import { asApiError, toApiError } from './api-error';

describe('toApiError', () => {
  it('uses the Problem Details body from the API', () => {
    const response = new HttpErrorResponse({
      status: 409,
      error: {
        status: 409,
        title: 'This action is not allowed in the current state.',
        code: 'task.invalid_transition',
        traceId: '00-abc-01',
      },
    });

    expect(toApiError(response)).toEqual({
      status: 409,
      code: 'task.invalid_transition',
      message: 'This action is not allowed in the current state.',
      traceId: '00-abc-01',
      fieldErrors: {},
    });
  });

  it('keeps validation field errors', () => {
    const response = new HttpErrorResponse({
      status: 400,
      error: { status: 400, title: 'Invalid', code: 'validation_failed', errors: { title: ['Required.'] } },
    });

    expect(toApiError(response).fieldErrors).toEqual({ title: ['Required.'] });
  });

  it('explains network failures', () => {
    const error = toApiError(new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }));

    expect(error.code).toBe('network_error');
    expect(error.message).toContain('could not be reached');
  });

  it('falls back to a generic message for server errors without a body', () => {
    const error = toApiError(new HttpErrorResponse({ status: 502, error: null }));

    expect(error.code).toBe('http_502');
    expect(error.message).toBe('An unexpected error occurred.');
  });
});

describe('asApiError', () => {
  it('returns an ApiError unchanged', () => {
    const apiError = { status: 404, code: 'x', message: 'm', fieldErrors: {} };

    expect(asApiError(apiError)).toBe(apiError);
  });

  it('wraps unknown values in a generic error', () => {
    expect(asApiError(new Error('boom')).code).toBe('unknown_error');
  });
});
