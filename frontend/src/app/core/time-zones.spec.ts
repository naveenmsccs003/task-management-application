import { currentTimeZoneName, timeZones } from './time-zones';

describe('time zones', () => {
  it('maps ICU legacy names to current IANA names', () => {
    expect(currentTimeZoneName('Asia/Calcutta')).toBe('Asia/Kolkata');
    expect(currentTimeZoneName('Europe/Kiev')).toBe('Europe/Kyiv');
    expect(currentTimeZoneName('Asia/Kolkata')).toBe('Asia/Kolkata');
  });

  it('lists current names only, once each, including UTC', () => {
    const zones = timeZones();

    expect(zones).toContain('UTC');
    expect(zones).not.toContain('Asia/Calcutta');
    expect(new Set(zones).size).toBe(zones.length);
  });
});
