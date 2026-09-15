import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'

/**
 * The shell every "how does this page work" dialog uses.
 *
 * Extracted so the three of them are nothing but their words. They had the same
 * modal, the same heading and the same close button copied into each, which is
 * three places to change when the shape of a dialog changes and one place to
 * forget.
 */
export function HelpModal({
  open,
  onClose,
  title,
  children,
}: {
  open: boolean
  onClose: () => void
  title: string
  children: React.ReactNode
}) {
  return (
    // Wider than the editing dialogs, because this one is prose and the point
    // is to read it without scrolling. 42rem leaves roughly 78 characters to a
    // line, which is about as wide as text stays comfortable; past that the eye
    // loses its place returning to the next line, and the scrolling saved is
    // not worth it.
    <Modal open={open} onClose={onClose} className="w-[min(42rem,calc(100%-2rem))]">
      <div className="flex flex-col gap-5 p-5">
        <h2 className="text-lg font-bold tracking-tight">{title}</h2>

        {children}

        <Button variant="outline" size="lg" onClick={onClose}>
          Close
        </Button>
      </div>
    </Modal>
  )
}

/**
 * One heading and its explanation.
 *
 * The body is a stack rather than a single paragraph, so several paragraphs sit
 * apart on their own. Spacing paragraphs with a pair of line breaks works until
 * somebody changes the type scale, at which point the gaps no longer match
 * anything else on the page.
 */
export function Topic({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-1.5 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
        {title}
      </h3>

      <div className="flex flex-col gap-2 text-sm leading-relaxed text-muted-foreground">
        {children}
      </div>
    </section>
  )
}

/**
 * A named thing and what it does, for the "Editing / Retiring / Deleting" shape
 * that every one of these dialogs ends up needing.
 */
export function Term({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <p>
      <strong className="font-semibold text-foreground">{label}</strong> {children}
    </p>
  )
}

/** Numbered steps, for describing something done in an order. */
export function Steps({ children }: { children: React.ReactNode }) {
  return <ol className="flex list-decimal flex-col gap-1.5 pl-5 marker:text-foreground">{children}</ol>
}
