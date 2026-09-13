import { useParams, useSearchParams } from 'react-router'
import { ConnectionDot } from '@/components/ConnectionDot'
import { ThemeToggle } from '@/components/ThemeToggle'
import { useFlip } from '@/lib/hooks/useFlip'
import { useScoreboard } from '@/lib/hooks/useScoreboard'
import { useWakeLock } from '@/lib/hooks/useWakeLock'
import { formatDate } from '@/lib/format'
import { SessionStatus, type LastRound, type Standing } from '@/lib/types'
import './board.css'

/**
 * The public scoreboard.
 *
 * One route for every screen it lands on: a projector driven from a laptop, a
 * TV, a leader's phone. Everything is sized in vh so it fills whatever shape it
 * is given without clipping, and it never scrolls.
 */
export function BoardPage() {
  const { slug } = useParams<{ slug: string }>()
  const [search] = useSearchParams()
  const tvMode = search.get('tv') === '1'

  const { data, isPending, error, connection, lastMessageAt } = useScoreboard(slug)
  const registerRow = useFlip()

  // The screen must not sleep while a board is on the wall.
  useWakeLock(true)

  if (isPending) {
    return <BoardMessage title="Loading" detail="Fetching the scoreboard." />
  }

  if (error || !data) {
    return (
      <BoardMessage
        title="Scoreboard unavailable"
        detail={
          error instanceof Error
            ? error.message
            : 'That session could not be found. Check the address on the screen.'
        }
      />
    )
  }

  const notStarted = data.status === SessionStatus.Setup

  return (
    <div className="board">
      <header className="board-head">
        <div className="board-title">
          <span className="board-division">{data.divisionName}</span>
          <span className="board-subtitle">{formatDate(data.date)}</span>
        </div>

        {/* Hidden in TV mode, where the room does not need controls. The
            connection state still reaches the footer, so staleness is never
            invisible even there. */}
        {!tvMode && (
          <div className="board-tools">
            <ConnectionDot
              className="board-connection"
              state={connection}
              lastMessageAt={lastMessageAt}
            />
            <ThemeToggle />
          </div>
        )}
      </header>

      <div className="board-rows">
        {data.standings.map((team) => (
          <TeamRow
            key={team.teamId}
            ref={registerRow(team.teamId)}
            team={team}
            showPoints={!notStarted}
          />
        ))}
      </div>

      <footer className="board-foot">
        <span className="board-summary">{summarize(data.status, data.lastRound)}</span>

        {tvMode && connection !== 'live' && (
          <span className="board-foot-warning">
            <ConnectionDot state={connection} lastMessageAt={lastMessageAt} />
          </span>
        )}
      </footer>
    </div>
  )
}

function TeamRow({
  ref,
  team,
  showPoints,
}: {
  ref: (element: HTMLElement | null) => void
  team: Standing
  showPoints: boolean
}) {
  return (
    <div
      ref={ref}
      className="board-row"
      style={
        {
          // The team color comes from the API, never from a class. Tenants
          // configure their own, and the design tokens are only seed values.
          '--team': team.colorHex,
        } as React.CSSProperties
      }
    >
      <div className="board-bar" />
      <div className="board-rank">{team.rank}</div>

      {/* The name is always shown. Roughly 8% of boys cannot distinguish red
          from green, and those are two of the four teams. */}
      <div className="board-name">{team.name}</div>

      <div className="board-delta">
        {team.rankChange > 0 && <ArrowUp />}
        {team.rankChange < 0 && <ArrowDown />}
      </div>

      <div className="board-points">{showPoints ? Math.round(team.points) : '—'}</div>
    </div>
  )
}

/**
 * One line describing the last round only. The totals are already enormous on
 * the screen above, so repeating them here would be noise; what the room cannot
 * see is what just changed.
 */
function summarize(status: SessionStatus, lastRound: LastRound | null): string {
  if (status === SessionStatus.Setup) return 'Starting soon.'
  if (!lastRound) return 'No rounds yet.'

  const scores = lastRound.teams
    .map((team) => {
      const points = Math.round(team.points)
      if (team.isDisqualified) return `${team.teamName} DQ`
      return `${team.teamName} ${points > 0 ? '+' : ''}${points}`
    })
    .join(' · ')

  const doubled = lastRound.multiplier !== 1 ? ` (×${lastRound.multiplier})` : ''

  return `Round ${lastRound.roundNumber} · ${lastRound.gameName}${doubled} — ${scores}`
}

function BoardMessage({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="board board-message">
      <div>
        <h1>{title}</h1>
        <p>{detail}</p>
      </div>
    </div>
  )
}

function ArrowUp() {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="var(--color-status-live)"
      strokeWidth="3"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-label="moved up"
    >
      <path d="M12 19V5" />
      <path d="M5 12l7-7 7 7" />
    </svg>
  )
}

function ArrowDown() {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="var(--color-status-down)"
      strokeWidth="3"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-label="moved down"
    >
      <path d="M12 5v14" />
      <path d="M19 12l-7 7-7-7" />
    </svg>
  )
}
