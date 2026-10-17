import { toParams } from './http-params';

describe('toParams', () => {
  it('skips empty values and repeats array values', () => {
    const params = toParams({ q: 'login', status: ['Draft', 'InReview'], assigneeId: undefined, search: '', page: 1, overdue: false });

    expect(params.toString()).toBe('q=login&status=Draft&status=InReview&page=1&overdue=false');
  });

  it('adds nothing for an empty array', () => {
    expect(toParams({ status: [] }).toString()).toBe('');
  });
});
