const moneyFormat = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
const numberFormat = new Intl.NumberFormat('en-US');
const dateFormat = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
const shortDate = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' });
const dateTimeFormat = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });

export const money = (value: number | null | undefined) => value === null || value === undefined ? '—' : moneyFormat.format(value);
export const num = (value: number | null | undefined) => value === null || value === undefined ? '—' : numberFormat.format(value);
export const date = (value: string | null | undefined) => value ? dateFormat.format(parse(value)) : '—';
export const day = (value: string | null | undefined) => value ? shortDate.format(parse(value)) : '—';
export const dateTime = (value: string | null | undefined) => value ? dateTimeFormat.format(new Date(value)) : '—';

/** Relative wording for a date-only value (yyyy-MM-dd) against today. */
export function due(value: string, today = todayIso()): string {
  const days = Math.round((parse(value).getTime() - parse(today).getTime()) / 86_400_000);
  if (days === 0) return 'Due today';
  if (days === -1) return 'Due yesterday';
  if (days < 0) return `${-days} days overdue`;
  if (days === 1) return 'Due tomorrow';
  return `Due in ${days} days`;
}

export function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

// Date-only strings are calendar days, not instants: read them as local dates.
function parse(value: string): Date {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) ? new Date(`${value}T00:00:00`) : new Date(value);
}

/** Maps a status or stage to a badge tone; the text is always shown alongside the colour. */
export function tone(value: string): string {
  switch (value) {
    case 'Committed': case 'Dispatched': case 'Ready for loading': return 'good';
    case 'Cancelled': case 'Discarded': case 'Departed': return 'bad';
    case 'Planned': case 'Draft': case 'On yard': return 'warn';
    default: return 'neutral';
  }
}
