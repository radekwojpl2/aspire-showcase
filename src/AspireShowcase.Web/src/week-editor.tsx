import { days, type Day, type Period, type Week } from './weekly-hours.ts';

const toMinutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
const toTime = (minutes: number) =>
  `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;

// A new period starts an hour after the day's last one ends (a lunch break), or at 9:00.
function nextPeriod(periods: Period[]): Period {
  const last = periods[periods.length - 1];
  if (!last) return { opens: '09:00', closes: '17:00' };
  const opens = Math.min(toMinutes(last.closes) + 60, 23 * 60);
  return { opens: toTime(opens), closes: toTime(Math.min(opens + 4 * 60, 23 * 60 + 55)) };
}

// One row per weekday, each with any number of periods. Errors come from the API, keyed by day.
// `noun` names the hours in labels: "opens"/"closes" for a business, "starts"/"ends" for staff.
export function WeekEditor({
  week,
  onChange,
  errors,
  idPrefix,
  emptyDay,
  addFirst,
  noun = { start: 'opens', end: 'closes' },
}: {
  week: Week;
  onChange: (week: Week) => void;
  errors: Record<string, string[]>;
  idPrefix: string;
  emptyDay: string;
  addFirst: string;
  noun?: { start: string; end: string };
}) {
  const change = (day: Day, periods: Period[]) => onChange({ ...week, [day]: periods });

  return (
    <>
      {days.map(({ key, label }) => (
        <fieldset
          key={key}
          className="day"
          aria-describedby={errors[key] ? `${idPrefix}-${key}-errors` : undefined}
        >
          <legend className="day-name">{label}</legend>
          <div className="day-periods">
            {week[key].length === 0 && <span className="hint">{emptyDay}</span>}
            {week[key].map((period, index) => (
              <div key={index} className="period">
                <input
                  type="time"
                  step={300}
                  className="input"
                  value={period.opens}
                  aria-label={`${label}, period ${index + 1}, ${noun.start}`}
                  onChange={(event) =>
                    change(key, week[key].map((p, i) => (i === index ? { ...p, opens: event.target.value } : p)))
                  }
                />
                <span aria-hidden="true">–</span>
                <input
                  type="time"
                  step={300}
                  className="input"
                  value={period.closes}
                  aria-label={`${label}, period ${index + 1}, ${noun.end}`}
                  onChange={(event) =>
                    change(key, week[key].map((p, i) => (i === index ? { ...p, closes: event.target.value } : p)))
                  }
                />
                <button
                  type="button"
                  className="button button-secondary"
                  aria-label={`Remove ${label}, period ${index + 1}`}
                  onClick={() => change(key, week[key].filter((_, i) => i !== index))}
                >
                  Remove
                </button>
              </div>
            ))}
            <div>
              <button
                type="button"
                className="button button-secondary"
                onClick={() => change(key, [...week[key], nextPeriod(week[key])])}
              >
                {week[key].length === 0 ? addFirst : 'Add hours'}
              </button>
            </div>
            {errors[key] && (
              <div id={`${idPrefix}-${key}-errors`}>
                {errors[key].map((message) => (
                  <p key={message} className="field-error">{message}</p>
                ))}
              </div>
            )}
          </div>
        </fieldset>
      ))}
    </>
  );
}
