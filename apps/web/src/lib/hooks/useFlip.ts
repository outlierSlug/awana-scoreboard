import { useCallback, useLayoutEffect, useRef } from 'react'

/**
 * Animates rows sliding to their new position when the order changes.
 *
 * The standard FLIP technique: remember where each row was, let React reorder
 * them, then measure again and play the difference backwards. Doing it this way
 * rather than animating heights or opacity means the movement is real and the
 * layout is never in an intermediate state, so a row is always where the
 * browser thinks it is.
 *
 * This is the payoff moment. Kids stop pestering the scorekeeper because they
 * can see their team overtake another one, which is the whole reason the board
 * exists.
 *
 * Honours prefers-reduced-motion, and skips the animation entirely on first
 * paint so the board does not slide in from nowhere when it loads.
 */
export function useFlip(duration = 600) {
  const nodes = useRef(new Map<string, HTMLElement>())
  const positions = useRef(new Map<string, number>())

  useLayoutEffect(() => {
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

    for (const [key, element] of nodes.current) {
      const top = element.getBoundingClientRect().top
      const previous = positions.current.get(key)

      // A row seen for the first time has no previous position, so it simply
      // appears rather than sliding from the top of the screen.
      const moved = previous !== undefined && Math.abs(previous - top) > 1

      if (moved && !reduced) {
        element.animate(
          [{ transform: `translateY(${previous - top}px)` }, { transform: 'translateY(0)' }],
          { duration, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' },
        )
      }

      positions.current.set(key, top)
    }
  })

  /** Ref callback for a row, keyed by something stable such as a team id. */
  const register = useCallback(
    (key: string) => (element: HTMLElement | null) => {
      if (element) nodes.current.set(key, element)
      else {
        nodes.current.delete(key)
        positions.current.delete(key)
      }
    },
    [],
  )

  return register
}
