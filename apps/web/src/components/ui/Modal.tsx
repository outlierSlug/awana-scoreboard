import { useEffect, useRef } from 'react'

/**
 * Content in front of everything else.
 *
 * Built on the native dialog element rather than a portal and a stack of
 * effects. showModal already gives the top layer, the backdrop, focus
 * containment and Escape, all of which would otherwise be hand rolled and
 * subtly wrong on a touchscreen.
 */
export function Modal({
  open,
  onClose,
  /** Ignores Escape and the backdrop, for a request already in flight. */
  locked = false,
  className = 'w-[min(28rem,calc(100vw-2rem))]',
  children,
}: {
  open: boolean
  onClose: () => void
  locked?: boolean
  className?: string
  children: React.ReactNode
}) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return

    if (open && !dialog.open) dialog.showModal()
    else if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog
      ref={ref}
      onCancel={(event) => {
        event.preventDefault()
        if (!locked) onClose()
      }}
      onClick={(event) => {
        // Only a click on the dialog box itself is the backdrop. Anything
        // inside has its own element as the target.
        if (event.target === ref.current && !locked) onClose()
      }}
      className={[
        'm-auto max-h-[calc(100dvh-2rem)] overflow-y-auto rounded-2xl border bg-card p-0 text-card-foreground shadow-2xl backdrop:bg-black/50 backdrop:backdrop-blur-[2px]',
        className,
      ].join(' ')}
    >
      {/* Unmounted while closed, so a form inside starts fresh every time it
          is opened rather than holding the last round someone looked at. */}
      {open && children}
    </dialog>
  )
}
