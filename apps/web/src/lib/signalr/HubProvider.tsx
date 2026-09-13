import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type IRetryPolicy,
} from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { config } from '@/config'
import { queryKeys } from '@/lib/queryClient'
import type { Scoreboard } from '@/lib/types'
import { HubContext, type ConnectionState, type HubContextValue } from './hubContext'

/**
 * Retries forever, backing off and then holding at fifteen seconds.
 *
 * The library default gives up after about thirty seconds, which is fine for a
 * dashboard someone is looking at and catastrophic for a TV nobody is standing
 * next to. Returning a number rather than null is what keeps it trying.
 */
const forever: IRetryPolicy = {
  nextRetryDelayInMilliseconds: (context) =>
    [0, 2_000, 5_000, 10_000][context.previousRetryCount] ?? 15_000,
}

/** How long offline before the queries start polling as a fallback. */
const POLL_AFTER_MS = 20_000

export function HubProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()

  const [state, setState] = useState<ConnectionState>('connecting')
  const [lastMessageAt, setLastMessageAt] = useState<Date | null>(null)

  const connectionRef = useRef<HubConnection | null>(null)
  // What we are subscribed to. Group membership does NOT survive a reconnect,
  // so this is replayed every time the connection comes back.
  const joinedRef = useRef<Set<string>>(new Set())

  // When the connection went down, held in a ref so that arming the polling
  // fallback does not require a synchronous setState inside an effect.
  const downSinceRef = useRef<number | null>(null)
  const [pollTick, setPollTick] = useState(0)

  /**
   * Pushes land straight in the query cache rather than in component state, so
   * a component only ever calls useQuery and has no idea SignalR exists.
   */
  const applyScoreboard = useCallback(
    (dto: Scoreboard) => {
      setLastMessageAt(new Date())

      const write = (key: readonly unknown[]) => {
        queryClient.setQueryData<Scoreboard>(key, (previous) =>
          // THE VERSION GUARD. After a reconnect a queued message can arrive
          // behind a fresher refetch, and applying it would visibly roll the
          // board back to an older score.
          previous && previous.version >= dto.version ? previous : dto,
        )
      }

      write(queryKeys.scoreboard(dto.slug))
      write(queryKeys.scoreboard(dto.sessionId))
    },
    [queryClient],
  )

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${config.apiBaseUrl}/hubs/scoreboard`, {
        // Left at the default so the negotiate step runs. Church guest wifi
        // sometimes blocks WebSocket upgrades, and negotiation is what lets the
        // client fall back to long polling instead of simply failing.
        skipNegotiation: false,
      })
      .withAutomaticReconnect(forever)
      .configureLogging(LogLevel.Warning)
      .build()

    const rejoinAll = async () => {
      for (const target of joinedRef.current) {
        // A session that has since been deleted should not stop the others.
        await connection.invoke('JoinSession', target).catch(() => undefined)
      }
    }

    connection.on('ScoreboardUpdated', applyScoreboard)
    connection.on('SessionStatusChanged', applyScoreboard)

    connection.onreconnecting(() => setState('reconnecting'))

    connection.onreconnected(async () => {
      setState('live')
      // Without this the connection is up and completely silent, which is the
      // worst of both worlds.
      await rejoinAll()
    })

    connection.onclose(() => setState('offline'))

    connectionRef.current = connection

    connection
      .start()
      .then(async () => {
        setState('live')
        await rejoinAll()
      })
      .catch(() => setState('offline'))

    return () => {
      connectionRef.current = null
      void connection.stop()
    }
  }, [applyScoreboard])

  // Polling only arms once the connection has been down for a while, so a brief
  // blip does not trigger a burst of requests. The timer nudges a render; the
  // decision itself is derived below.
  useEffect(() => {
    if (state === 'live') {
      downSinceRef.current = null
      return
    }

    downSinceRef.current ??= Date.now()

    const timer = setTimeout(() => setPollTick((tick) => tick + 1), POLL_AFTER_MS)
    return () => clearTimeout(timer)
  }, [state])

  const shouldPoll =
    state !== 'live' &&
    downSinceRef.current !== null &&
    Date.now() - downSinceRef.current >= POLL_AFTER_MS

  const join = useCallback((slugOrId: string) => {
    if (joinedRef.current.has(slugOrId)) return
    joinedRef.current.add(slugOrId)

    const connection = connectionRef.current
    if (connection?.state === HubConnectionState.Connected) {
      void connection.invoke('JoinSession', slugOrId).catch(() => undefined)
    }
  }, [])

  const leave = useCallback((slugOrId: string) => {
    joinedRef.current.delete(slugOrId)
  }, [])

  const value = useMemo<HubContextValue>(
    () => ({ state, lastMessageAt, shouldPoll, join, leave }),
    // pollTick is a deliberate dependency: it is what re-derives shouldPoll
    // after the timer fires.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state, lastMessageAt, shouldPoll, pollTick, join, leave],
  )

  return <HubContext value={value}>{children}</HubContext>
}
