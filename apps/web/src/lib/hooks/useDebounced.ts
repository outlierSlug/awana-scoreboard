import { useEffect, useState } from 'react'

/**
 * Trails a fast-changing value.
 *
 * Used to keep the live points preview from firing a request on every single
 * tap. A scorekeeper enters four teams in a couple of seconds, and what matters
 * is the answer once they stop, not four answers on the way there.
 */
export function useDebounced<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return settled
}
