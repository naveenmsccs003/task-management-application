/**
 * Browsers list time zones with ICU's legacy names (e.g. Asia/Calcutta), but the server's tz database may only know
 * the current IANA names (Ubuntu ships the old aliases in the optional tzdata-legacy package). Map them before use.
 */
const legacyNames: Readonly<Record<string, string>> = {
  'Africa/Asmera': 'Africa/Asmara',
  'America/Buenos_Aires': 'America/Argentina/Buenos_Aires',
  'America/Catamarca': 'America/Argentina/Catamarca',
  'America/Coral_Harbour': 'America/Atikokan',
  'America/Cordoba': 'America/Argentina/Cordoba',
  'America/Godthab': 'America/Nuuk',
  'America/Indianapolis': 'America/Indiana/Indianapolis',
  'America/Jujuy': 'America/Argentina/Jujuy',
  'America/Louisville': 'America/Kentucky/Louisville',
  'America/Mendoza': 'America/Argentina/Mendoza',
  'Asia/Calcutta': 'Asia/Kolkata',
  'Asia/Katmandu': 'Asia/Kathmandu',
  'Asia/Rangoon': 'Asia/Yangon',
  'Asia/Saigon': 'Asia/Ho_Chi_Minh',
  'Atlantic/Faeroe': 'Atlantic/Faroe',
  'Europe/Kiev': 'Europe/Kyiv',
  'Pacific/Enderbury': 'Pacific/Kanton',
  'Pacific/Ponape': 'Pacific/Pohnpei',
  'Pacific/Truk': 'Pacific/Chuuk',
};

export function currentTimeZoneName(zone: string): string {
  return legacyNames[zone] ?? zone;
}

/** IANA time zones known to the browser, for time-zone pickers. */
export function timeZones(): string[] {
  const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf;
  const zones = new Set((supported ? supported('timeZone') : []).map(currentTimeZoneName));
  zones.add('UTC');
  return [...zones].sort();
}

export function browserTimeZone(): string {
  return currentTimeZoneName(Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC');
}
