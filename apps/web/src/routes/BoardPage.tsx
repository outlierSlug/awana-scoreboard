import { useParams, useSearchParams } from 'react-router'
import { ConnectionDot } from '@/components/ConnectionDot'
import { useScoreboard } from '@/lib/hooks/useScoreboard'
import { useWakeLock } from '@/lib/hooks/useWakeLock'
import { SessionStatus, type Standing } from '@/lib/types'
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
  const finished = data.status === SessionStatus.Finished

  return (
    <div className="board">
      <header className="board-head">
        <div className="board-title">
          <span className="board-division">{data.divisionName}</span>
          <span className="board-subtitle">
            {notStarted
              ? 'Starting soon'
              : data.lastRound
                ? `Round ${data.lastRound.roundNumber} · ${data.lastRound.gameName}`
                : 'No rounds yet'}
            {finished ? ' · Final' : ''}
          </span>
        </div>

        {/* Hidden in TV mode, where the room does not need diagnostics, but the
            connection state still drives the footer so staleness is never
            invisible. */}
        {!tvMode && (
          <ConnectionDot
            className="board-connection"
            state={connection}
            lastMessageAt={lastMessageAt}
          />
        )}
      </header>

      <div className="board-rows">
        {data.standings.map((team) => (
          <TeamRow key={team.teamId} team={team} showPoints={!notStarted} />
        ))}
      </div>

      <footer className="board-foot">
        {notStarted ? (
          <span>Waiting for the first round.</span>
        ) : data.lastRound ? (
          <span>{summarize(data.lastRound.roundNumber, data.lastRound.gameName, data.standings)}</span>
        ) : (
          <span>No rounds recorded yet.</span>
        )}

        {tvMode && connection !== 'live' && (
          <span className="board-foot-warning">
            <ConnectionDot state={connection} lastMessageAt={lastMessageAt} />
          </span>
        )}
      </footer>
    </div>
  )
}

function TeamRow({ team, showPoints }: { team: Standing; showPoints: boolean }) {
  return (
    <div
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

function summarize(roundNumber: number, gameName: string, standings: Standing[]): string {
  const leader = standings[0]
  const tiedAtTop = standings.filter((s) => s.rank === 1)

  if (tiedAtTop.length > 1) {
    const names = tiedAtTop.map((s) => s.name).join(' and ')
    return `Round ${roundNumber}, ${gameName}. ${names} lead on ${Math.round(leader.points)}.`
  }

  return `Round ${roundNumber}, ${gameName}. ${leader.name} leads on ${Math.round(leader.points)}.`
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
    <svg viewBox="0 0 24 24" fill="none" stroke="var(--color-status-live)" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" aria-label="moved up">
      <path d="M12 19V5" />
      <path d="M5 12l7-7 7 7" />
    </svg>
  )
}

function ArrowDown() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="var(--color-status-down)" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" aria-label="moved down">
      <path d="M12 5v14" />
      <path d="M19 12l-7 7-7-7" />
    </svg>
  )
}
