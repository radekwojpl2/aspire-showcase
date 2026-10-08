import { apiFetch, readProblem } from './api.ts';

export type CalendarView = 'day' | 'week';

// Days and times are already in the business's time zone: "2026-10-06", "09:00".
export type CalendarBooking = {
  id: string;
  day: string;
  start: string;
  end: string;
  staffMemberId: string;
  staffName: string;
  serviceName: string;
  clientName: string;
  // Null once the client deleted their account (V1-8).
  clientEmail: string | null;
};

// The part of a time off (user story V1-1) on one day; end is "24:00" when it lasts all day.
export type CalendarTimeOff = {
  id: string;
  day: string;
  start: string;
  end: string;
  staffMemberId: string | null;
  staffName: string | null;
  note: string | null;
};

export type Calendar = {
  view: CalendarView;
  date: string;
  firstDay: string;
  lastDay: string;
  timeZone: string;
  staff: { id: string; name: string }[];
  bookings: CalendarBooking[];
  timeOff: CalendarTimeOff[];
};

// What cancelling needs to know of a booking, wherever it's listed.
export type CancellableBooking = Pick<CalendarBooking, 'id' | 'day' | 'start' | 'serviceName' | 'clientName'>;

// The owner's calendar; without a date, the API uses today in the business's time zone.
// Null when the user has no business (404) or isn't an owner (403).
export async function fetchCalendar(view: CalendarView, date?: string, staffMemberId?: string): Promise<Calendar | null> {
  const query = new URLSearchParams({ view });
  if (date) query.set('date', date);
  if (staffMemberId) query.set('staffMemberId', staffMemberId);
  const response = await apiFetch(`/api/businesses/mine/bookings?${query}`);
  if (response.status === 404 || response.status === 403) return null;
  if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
  return (await response.json()) as Calendar;
}

// User story MVP-14: the owner cancels for sickness or emergencies; the client gets an email.
export async function cancelBooking(booking: CancellableBooking): Promise<void> {
  const response = await apiFetch(`/api/businesses/mine/bookings/${booking.id}/cancel`, { method: 'POST' });
  if (!response.ok) throw new Error((await readProblem(response)).title ?? `HTTP error! status: ${response.status}`);
}

export const confirmCancel = (booking: CancellableBooking) =>
  window.confirm(`Cancel ${booking.clientName}'s ${booking.serviceName} on ${booking.day}, ${booking.start}?`);
