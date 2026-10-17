import { describeHistory, formatAllocation, TaskHistoryEntry } from './tasks.models';

const base: TaskHistoryEntry = {
  occurredAt: '2026-10-17T09:00:00Z',
  kind: 'status',
  actor: { id: '1', name: 'Priya' },
  revision: 0,
  action: 'create',
  fromStatus: null,
  toStatus: 'Draft',
  fromUser: null,
  toUser: null,
  comment: null,
};

describe('task model helpers', () => {
  it('formats allocated minutes as hours, treating empty and zero as not estimated', () => {
    expect(formatAllocation(480)).toBe('8 h');
    expect(formatAllocation(90)).toBe('1.5 h');
    expect(formatAllocation(null)).toBe('Not estimated');
    expect(formatAllocation(0)).toBe('Not estimated');
  });

  it('describes creation, state changes and assignments', () => {
    expect(describeHistory(base)).toBe('Created as Draft');
    expect(describeHistory({ ...base, action: 'assign', fromStatus: 'Draft', toStatus: 'ReadyToStart' })).toBe('Draft → Ready to start');
    expect(describeHistory({ ...base, kind: 'assignment', toUser: { id: '2', name: 'Dev One' } })).toBe('Assigned to Dev One');
    expect(
      describeHistory({ ...base, kind: 'assignment', fromUser: { id: '2', name: 'Dev One' }, toUser: { id: '3', name: 'Dev Two' } }),
    ).toBe('Reassigned from Dev One to Dev Two');
  });
});
