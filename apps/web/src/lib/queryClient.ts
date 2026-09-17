import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query'
import { ApiError } from './apiClient'

/**
 * Query keys in one place, so a cache write from the real-time layer and a
 * cache read from a component cannot drift apart on a typo.
 */
export const queryKeys = {
  scoreboard: (slug: string) => ['scoreboard', slug] as const,
  liveSessions: (church: string) => ['live-sessions', church] as const,
  finishedSessions: (church: string) => ['finished-sessions', church] as const,

  /**
   * The same two lists without a church, for invalidating them from the
   * real-time layer, which knows a session changed but not whose church it is.
   * A partial key matches every church's copy, and in v1 there is one.
   */
  liveSessionsAll: ['live-sessions'] as const,
  finishedSessionsAll: ['finished-sessions'] as const,
  me: () => ['me'] as const,
  sessions: () => ['sessions'] as const,
  session: (id: string) => ['session', id] as const,
  divisions: () => ['divisions'] as const,
  games: () => ['games'] as const,
  gameCatalog: () => ['game-catalog'] as const,
  scoringProfiles: () => ['scoring-profiles'] as const,
  people: () => ['people'] as const,
  activity: () => ['activity'] as const,
}

/**
 * A session that has run out, handled once.
 *
 * The cookie lasts fourteen days and slides, so this is rare, but when it does
 * happen it happens mid round: every panel starts answering 401 and the console
 * fills with errors that all mean the same thing. Sending the scorekeeper to
 * sign in again is the only useful response, and it has to be the whole app's
 * response rather than each caller's.
 *
 * Only from inside the console. The board and the landing page call public
 * endpoints that never answer 401, and a redirect to sign-in on a screen
 * showing scores to a room would be the wrong answer to any error at all.
 */
function onExpiredSession(error: unknown) {
  if (!(error instanceof ApiError) || error.status !== 401) return
  if (!window.location.pathname.startsWith('/app')) return

  queryClient.clear()
  window.location.href = '/login'
}

export const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError: onExpiredSession }),
  mutationCache: new MutationCache({ onError: onExpiredSession }),
  defaultOptions: {
    queries: {
      // The board holds a live connection, so polling is a fallback rather than
      // the normal path. What matters far more is recovering the moment the
      // network comes back, which is the common failure in a gym.
      refetchOnReconnect: true,

      // On, and it is the single thing that stops "why is this screen wrong".
      //
      // Only the board subscribes to the hub. Every other screen is a plain
      // query, so a list left open in another window showed whatever was true
      // when it was last looked at. Worse, refetchInterval is itself gated on
      // focus (refetchIntervalInBackground defaults to false), so an unfocused
      // window stops polling AND had nothing to catch it up on the way back.
      //
      // Nothing here is expensive enough to care, and no screen holds unsaved
      // work in a query: the round being tapped out lives in component state,
      // so a refetch cannot take it away.
      refetchOnWindowFocus: true,

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
