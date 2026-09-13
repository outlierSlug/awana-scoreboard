import { createContext, useContext } from 'react'

export type ConnectionState = 'connecting' | 'live' | 'reconnecting' | 'offline'

export interface HubContextValue {
  state: ConnectionState
  /** When the server last told us anything, for the board's "updated Ns ago". */
  lastMessageAt: Date | null
  /** True once the connection has been down long enough that polling should take over. */
  shouldPoll: boolean
  join: (slugOrId: string) => void
  leave: (slugOrId: string) => void
}

/**
 * Kept apart from the provider component so that editing the provider does not
 * invalidate every consumer during development, and so the hook can be
 * imported without pulling the connection code in.
 */
export const HubContext = createContext<HubContextValue | null>(null)

export function useHub(): HubContextValue {
  const context = useContext(HubContext)
  if (!context) throw new Error('useHub must be used inside a HubProvider.')
  return context
}
