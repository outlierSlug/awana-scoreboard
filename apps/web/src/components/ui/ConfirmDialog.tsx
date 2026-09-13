import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'

/** A yes or no question, in front of everything else. */
export function ConfirmDialog({
  open,
  title,
  children,
  confirmLabel,
  cancelLabel = 'Go back',
  destructive = false,
  busy = false,
  onConfirm,
  onCancel,
}: {
  open: boolean
  title: string
  children?: React.ReactNode
  confirmLabel: string
  cancelLabel?: string
  destructive?: boolean
  busy?: boolean
  onConfirm: () => void
  onCancel: () => void
}) {
  return (
    <Modal open={open} locked={busy} onClose={onCancel}>
      <div className="flex flex-col gap-3 p-5">
        <h2 className="text-lg font-bold tracking-tight">{title}</h2>

        {children && <div className="text-sm text-muted-foreground">{children}</div>}

        <div className="mt-2 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="outline" size="lg" disabled={busy} onClick={onCancel}>
            {cancelLabel}
          </Button>
          <Button
            size="lg"
            variant={destructive ? 'destructive' : 'default'}
            disabled={busy}
            onClick={onConfirm}
            autoFocus
          >
            {busy ? 'Working...' : confirmLabel}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
