import { useEffect, useState } from 'react'
import type { ConnectionState } from '@/lib/signalr/hubContext'

const LABEL: Record<ConnectionState, string> = {
  connecting: 'Connecting',
  live: 'Live',
  reconnecting: 'Reconnecting',
  offline: 'Offline',
}

const COLOR: Record<ConnectionState, string> = {
  connecting: 'var(--color-status-retry)',
  live: 'var(--color-status-live)',
  reconnecting: 'var(--color-status-retry)',
  offline: 'var(--color-status-down)',
}

function ago(from: Date | null, now: number): string | null {
  if (!from) return null
  const seconds = Math.max(0, Math.round((now - from.getTime()) / 1000))
  if (seconds < 60) return `${seconds}s ago`
  const minutes = Math.round(seconds / 60)
  return `${minutes}m ago`
}

/**
 * Connection state, always visible.
 *
 * Silent staleness is worse than a visible error: a board showing a wrong score
 * confidently is the failure that loses trust, and nobody in the room can tell
 * the difference between "nothing has happened yet" and "this stopped updating
 * twenty minutes ago" unless the board says so.
 */
export function ConnectionDot({
  state,
  lastMessageAt,
  className,
  labelClassName,
}: {
  state: ConnectionState
  lastMessageAt: Date | null
  className?: string
  /** Lets a cramped header hide the words and keep the dot. */
  labelClassName?: string
}) {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])

  const elapsed = ago(lastMessageAt, now)

  return (
    <span className={className} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.6em' }}>
      <span
        aria-hidden
        style={{
          width: '0.55em',
          height: '0.55em',
          borderRadius: '999px',
          background: COLOR[state],
          flexShrink: 0,
        }}
      />
      <span className={labelClassName}>
        {LABEL[state]}
        {state === 'live' && elapsed ? ` · updated ${elapsed}` : null}
        {state !== 'live' && elapsed ? ` · last update ${elapsed}` : null}
      </span>
    </span>
  )
}
