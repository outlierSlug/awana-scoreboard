import { SessionStatus } from '@/lib/types'

const LABEL = {
  [SessionStatus.Setup]: { text: 'Setup', className: 'text-muted-foreground' },
  [SessionStatus.Running]: { text: 'Live', className: 'text-[var(--color-team-green)]' },
  [SessionStatus.Finished]: { text: 'Finished', className: 'text-muted-foreground' },
} as const

export function SessionStatusLabel({ status }: { status: SessionStatus }) {
  const { text, className } = LABEL[status]
  return <span className={`text-sm font-medium ${className}`}>{text}</span>
}
