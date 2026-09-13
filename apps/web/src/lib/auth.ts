import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { atLeast, type Me, type UserRole } from '@/lib/types'

/**
 * Who is signed in.
 *
 * One query rather than a context of its own: the answer comes from the server,
 * it is cached like everything else from the server, and a stale copy after
 * signing out is the one failure worth avoiding.
 */
export function useMe() {
  const me = useQuery<Me>({
    queryKey: queryKeys.me(),
    queryFn: ({ signal }) => api.me(signal),
    // Signing in happens through a full page load, so this is fetched fresh on
    // the way back anyway. Holding it a while keeps every other page from
    // asking again.
    staleTime: 5 * 60_000,
    retry: false,
  })

  return {
    me: me.data,
    isLoading: me.isPending,
    isSignedIn: me.data?.isSignedIn === true,
    can: (minimum: UserRole) => atLeast(me.data?.role, minimum),
  }
}

export function useSignOut() {
  const queryClient = useQueryClient()

  return async () => {
    try {
      await api.logout()
    } finally {
      // Whatever the server said, this browser is done: drop every cached
      // answer rather than leave one page showing data the next person cannot
      // fetch again.
      queryClient.clear()
      window.location.href = '/'
    }
  }
}
