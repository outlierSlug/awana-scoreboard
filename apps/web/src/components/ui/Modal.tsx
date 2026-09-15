import { useEffect, useState } from 'react'

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
  // Percentages, not vw: a dialog in the top layer sizes against the content
  // area, while 100vw counts the scrollbar's width as usable and leaves the box
  // wider than the page it is sitting on.
  className = 'w-[min(28rem,calc(100%-2rem))]',
  children,
}: {
  open: boolean
  onClose: () => void
  locked?: boolean
  className?: string
  children: React.ReactNode
}) {
  // Held as state rather than a ref so that anything portalling into it
  // re-renders once the element exists.
  const [dialog, setDialog] = useState<HTMLDialogElement | null>(null)

  useEffect(() => {
    if (!dialog) return

    if (open && !dialog.open) {
      dialog.showModal()

      // Opened at the top, every time. A dialog keeps the scroll position it
      // was left at, so on a phone, where a long one is genuinely scrollable,
      // reopening it lands halfway down the form it was closed from.
      //
      // The rule below sees a value from useState being written to. It is a DOM
      // element and scrollTop is an imperative property on it, not React state.
      // eslint-disable-next-line react/immutability
      dialog.scrollTop = 0
    } else if (!open && dialog.open) {
      dialog.close()
    }
  }, [open, dialog])

  return (
    <dialog
      ref={setDialog}
      onCancel={(event) => {
        event.preventDefault()
        if (!locked) onClose()
      }}
      onClick={(event) => {
        if (locked || !dialog) return

        // The backdrop is decided by where the pointer was, not by what the
        // event landed on. A menu portalled into this dialog is removed on
        // pointerup, so the click that follows reports the dialog itself as its
        // target and a check for that closed the whole modal on every option
        // picked. The dialog's own box is its content, the backdrop is outside
        // it, so the coordinates answer this honestly.
        if (event.nativeEvent.detail === 0) return

        const box = dialog.getBoundingClientRect()
        const inside =
          event.clientX >= box.left &&
          event.clientX <= box.right &&
          event.clientY >= box.top &&
          event.clientY <= box.bottom

        if (!inside) onClose()
      }}
      className={[
        // The dialog scrolls itself once its content outgrows the viewport, so
        // it reserves its own scrollbar for the same reason the page does:
        // choosing a game fills the form out and the gutter must not appear
        // underneath someone already reaching for the next control. Reserved on
        // BOTH edges, so the form inside stays centred and lines up with the
        // same form on the page behind it rather than sitting half a scrollbar
        // to the left of it.
        'm-auto max-h-[calc(100dvh-2rem)] overflow-y-auto [scrollbar-gutter:stable_both-edges] rounded-2xl border bg-card p-0 text-card-foreground shadow-2xl backdrop:bg-black/50 backdrop:backdrop-blur-[2px]',
        className,
      ].join(' ')}
    >
      {/* Unmounted while closed, so a form inside starts fresh every time it
          is opened rather than holding the last round someone looked at. */}
      {open && children}
    </dialog>
  )
}
