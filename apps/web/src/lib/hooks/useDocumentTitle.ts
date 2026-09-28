import { useEffect } from 'react'

const APP = 'Awana Scoreboard'

/**
 * Names the browser tab after the page, with the app's name last.
 *
 * Every page used to share one title, so three open tabs, a bookmark, and the
 * board left on the gym laptop all read "Awana Scoreboard". Null means the
 * page IS the app, which is the landing page.
 *
 * Not restored on unmount: the next page sets its own, and restoring would
 * flash the old name between the two.
 */
export function useDocumentTitle(title: string | null | undefined) {
  useEffect(() => {
    document.title = title ? `${title} · ${APP}` : APP
  }, [title])
}
