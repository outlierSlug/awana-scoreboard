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

/**
 * A score, shown with whatever precision it actually has.
 *
 * The server has already rounded to the session's configured decimal places, so
 * there is nothing left to decide here: 62.5 prints as 62.5 and 40 prints as
 * 40, with no trailing zeros either way.
 *
 * Every screen used to call Math.round on this, which was harmless while the
 * only table in use was the official one, where every possible tie already
 * divides into whole numbers. On a table that does not, a four-way tie worth
 * 62.5 was displayed as 63 on both the console and the board, and the decimal
 * place somebody had deliberately configured did nothing.
 *
 * Grouped, because a season total reaches four digits and 1,240 reads faster
 * than 1240 across a gym.
 */
export function formatPoints(points: number): string {
  // Three, matching the numeric(9,3) the points are stored in.
  return points.toLocaleString(undefined, { maximumFractionDigits: 3 })
}
