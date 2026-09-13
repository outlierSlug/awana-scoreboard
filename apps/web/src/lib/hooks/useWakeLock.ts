import { useEffect } from 'react'

/**
 * Keeps the screen awake while the board is open.
 *
 * Ten lines that stop a projector going black in hour two of a games night.
 * The lock is dropped whenever the tab is hidden, so it has to be re-requested
 * on every return to visibility rather than taken once at mount.
 *
 * Unsupported browsers and refusals are both fine: the board still works, the
 * screen just sleeps as it normally would.
 */
export function useWakeLock(enabled: boolean) {
  useEffect(() => {
    if (!enabled) return
    if (!('wakeLock' in navigator)) return

    let sentinel: WakeLockSentinel | null = null
    let cancelled = false

    const acquire = async () => {
      if (document.visibilityState !== 'visible') return
      try {
        const lock = await navigator.wakeLock.request('screen')
        if (cancelled) {
          void lock.release()
          return
        }
        sentinel = lock
      } catch {
        // Denied, or the tab lost focus mid-request. Nothing to do.
      }
    }

    void acquire()
    document.addEventListener('visibilitychange', acquire)

    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', acquire)
      void sentinel?.release().catch(() => undefined)
    }
  }, [enabled])
}
