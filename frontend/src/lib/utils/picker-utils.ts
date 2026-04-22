/** Shared constants and helpers for date/time picker components */

export const HOURS = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, "0"));
export const MINUTES_5 = Array.from({ length: 12 }, (_, i) => String(i * 5).padStart(2, "0"));

/** Combine separate date/time form fields into a DateTimePicker value ("YYYY-MM-DDTHH:mm") */
export function combineDateTimeFields(date: string, time: string): string {
  if (!date) return "";
  return `${date}T${time || "08:00"}`;
}

/** Split a DateTimePicker value back into separate date/time form fields */
export function splitDateTimeFields(
  value: string,
  setDate: (d: string) => void,
  setTime: (t: string) => void
) {
  if (!value) {
    setDate("");
    setTime("");
    return;
  }
  const [date, time] = value.split("T");
  setDate(date);
  setTime(time);
}
