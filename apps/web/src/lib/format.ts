/**
 * Formats the API's plain date for reading.
 *
 * Parsed and formatted in UTC on purpose. The API sends a calendar date with no
 * time, and letting the browser interpret it in local time would show a Friday
 * session as Thursday for anyone west of the church.
 */
export function formatDate(iso: string): string {
  const [year, month, day] = iso.split('-').map(Number)

  return new Date(Date.UTC(year, month - 1, day)).toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric',
    timeZone: 'UTC',
  })
}
