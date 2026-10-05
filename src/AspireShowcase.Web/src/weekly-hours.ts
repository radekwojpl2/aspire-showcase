// Weekly hours as the API sends and takes them: a list of periods, each on a weekday. Both a
// business's opening hours and a staff member's working hours have this shape.

export const days = [
  { key: 'monday', label: 'Monday' },
  { key: 'tuesday', label: 'Tuesday' },
  { key: 'wednesday', label: 'Wednesday' },
  { key: 'thursday', label: 'Thursday' },
  { key: 'friday', label: 'Friday' },
  { key: 'saturday', label: 'Saturday' },
  { key: 'sunday', label: 'Sunday' },
] as const;

export type Day = (typeof days)[number]['key'];

// Times as HH:mm, in the business's time zone: what <input type="time"> and the API both use.
export type Period = { opens: string; closes: string };

export type DayPeriod = Period & { day: Day };

// The same hours, by day, as the editor shows them.
export type Week = Record<Day, Period[]>;

export const emptyWeek = (): Week => ({
  monday: [], tuesday: [], wednesday: [], thursday: [], friday: [], saturday: [], sunday: [],
});

export function toWeek(periods: DayPeriod[]): Week {
  const week = emptyWeek();
  for (const { day, opens, closes } of periods) week[day].push({ opens, closes });
  return week;
}

export const toPeriods = (week: Week): DayPeriod[] =>
  days.flatMap(({ key }) => week[key].map((period) => ({ day: key, ...period })));

// "Mon 09:00–12:00, 13:00–17:00 · Sat 10:00–14:00", for lists.
export function describe(periods: DayPeriod[]): string {
  const week = toWeek(periods);
  return days
    .filter(({ key }) => week[key].length > 0)
    .map(({ key, label }) => `${label.slice(0, 3)} ${week[key].map((p) => `${p.opens}–${p.closes}`).join(', ')}`)
    .join(' · ');
}
