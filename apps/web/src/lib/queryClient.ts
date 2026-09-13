import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './apiClient'

/**
 * Query keys in one place, so a cache write from the real-time layer and a
 * cache read from a component cannot drift apart on a typo.
 */
export const queryKeys = {
  scoreboard: (slug: string) => ['scoreboard', slug] as const,
  liveSessions: (church: string) => ['live-sessions', church] as const,
  sessions: () => ['sessions'] as const,
  session: (id: string) => ['session', id] as const,
  divisions: () => ['divisions'] as const,
  games: (divisionId?: string) => ['games', divisionId ?? 'all'] as const,
}

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // The board holds a live connection, so polling is a fallback rather than
      // the normal path. What matters far more is recovering the moment the
      // network comes back, which is the common failure in a gym.
      refetchOnReconnect: true,
      refetchOnWindowFocus: false,
      staleTime: 30_000,

      retry: (failureCount, error) => {
        // A 404 or a 409 will not become true by asking again.
        if (error instanceof ApiError && !error.isRetryable) return false
        return failureCount < 3
      },

      retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 8000),
    },
    mutations: {
      // Never retry a mutation automatically. Recording a round is idempotent
      // ONLY because the client sends the same clientRequestId, and that is the
      // caller's job to arrange, not something to paper over here.
      retry: false,
    },
  },
})
