import { formatClock, formatDuration } from './time-format';

describe('time formatting', () => {
  it('formats durations as hours and minutes', () => {
    expect(formatDuration(0)).toBe('0 m');
    expect(formatDuration(59)).toBe('0 m');
    expect(formatDuration(12 * 60)).toBe('12 m');
    expect(formatDuration(3900)).toBe('1 h 05 m');
    expect(formatDuration(-30)).toBe('0 m');
  });

  it('formats the running timer as a clock', () => {
    expect(formatClock(0)).toBe('00:00:00');
    expect(formatClock(3723)).toBe('01:02:03');
    expect(formatClock(36_000)).toBe('10:00:00');
  });
});
