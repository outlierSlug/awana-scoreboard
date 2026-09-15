import type { Standing } from '@/lib/types'
import { formatPoints } from '@/lib/format'

/**
 * Where the night ended.
 *
 * The numbers come from the scoreboard endpoint rather than from summing the
 * rounds here. There is one implementation of standings and it is the server's,
 * so a total on this page cannot drift from the one on the wall.
 */
export function FinalStandings({ standings }: { standings: Standing[] }) {
  const leader = standings[0]?.points ?? 0

  return (
    <ul className="flex flex-col gap-2">
      {standings.map((team) => (
        <li key={team.teamId} className="flex items-center gap-3 rounded-xl bg-card p-3 ring-1 ring-foreground/10">
          <span className="w-6 text-center text-sm font-bold text-muted-foreground tabular-nums">
            {team.rank}
          </span>

          <span
            className="inline-flex h-8 min-w-20 items-center justify-center rounded-lg px-3 text-sm font-bold"
            style={{ background: team.colorHex, color: team.textOnColorHex }}
          >
            {team.name}
          </span>

          {/* A bar rather than only a number, so the shape of the night reads
              before any of it is actually read. */}
          <span className="hidden h-2 flex-1 overflow-hidden rounded-full bg-muted sm:block">
            <span
              className="block h-full rounded-full"
              style={{
                background: team.colorHex,
                width: leader > 0 ? `${Math.max(2, (team.points / leader) * 100)}%` : '0%',
              }}
            />
          </span>

          <span className="ml-auto text-lg font-bold tabular-nums sm:ml-0">
            {formatPoints(team.points)}
          </span>
        </li>
      ))}
    </ul>
  )
}
