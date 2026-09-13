import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { api } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { useHub } from '@/lib/signalr/hubContext'
import type { Scoreboard } from '@/lib/types'

/**
 * The scoreboard for one session.
 *
 * Initial load, live pushes and post-reconnect resync all funnel through one
 * cache entry with one shape, so a component reads a scoreboard and never
 * thinks about where it came from.
 */
export function useScoreboard(slug: string | undefined) {
  const { join, leave, shouldPoll, state, lastMessageAt } = useHub()

  useEffect(() => {
    if (!slug) return
    join(slug)
    return () => leave(slug)
  }, [slug, join, leave])

  const query = useQuery<Scoreboard>({
    queryKey: queryKeys.scoreboard(slug ?? ''),
    queryFn: ({ signal }) => api.publicScoreboard(slug!, signal),
    enabled: Boolean(slug),
    // Only while the live connection is down. Under normal conditions the
    // server pushes and this stays at zero requests.
    refetchInterval: shouldPoll ? 10_000 : false,
  })

  return {
    ...query,
    connection: state,
    lastMessageAt,
    /** True when the data on screen may be behind what the server has. */
    isStale: state !== 'live',
  }
}
