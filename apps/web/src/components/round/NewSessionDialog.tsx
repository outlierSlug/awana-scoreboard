import { CalendarIcon, Play, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Calendar } from '@/components/ui/calendar'
import { Modal } from '@/components/ui/Modal'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { ApiError } from '@/lib/apiClient'
import { formatDate } from '@/lib/format'
import type { Division, ScoringProfile, SessionSummary } from '@/lib/types'

/**
 * A session, set up and started in one go.
 *
 * Starting is what freezes tonight's scoring rules onto the session, so it is
 * a real step rather than ceremony. It is just not a step anyone wants on a
 * Friday night, when the session is created and started in the same breath.
 *
 * Both ways out are offered, because setup is genuinely useful twice: making
 * next week's session in advance, and counting heads before the games begin.
 */
export function NewSessionDialog({
  open,
  onClose,
  divisions,
  existing,
  scoringProfiles,
  busy,
  error,
  onCreate,
}: {
  open: boolean
  onClose: () => void
  divisions: Division[]
  /** Already created, so the same night cannot be made twice by accident. */
  existing: SessionSummary[]
  /** Active sets only. Offered when there is more than one to choose between. */
  scoringProfiles: ScoringProfile[]
  busy: boolean
  error: unknown
  onCreate: (
    divisionId: string,
    date: string,
    start: boolean,
    scoringProfileId: string | null,
  ) => void
}) {
  return (
    <Modal open={open} onClose={onClose} locked={busy}>
      {/* Remounted with the dialog, so it opens on today rather than on
          whatever was picked the last time it was used. */}
      <Form
        divisions={divisions}
        existing={existing}
        scoringProfiles={scoringProfiles}
        busy={busy}
        error={error}
        onClose={onClose}
        onCreate={onCreate}
      />
    </Modal>
  )
}

function Form({
  divisions,
  existing,
  scoringProfiles,
  busy,
  error,
  onClose,
  onCreate,
}: {
  divisions: Division[]
  existing: SessionSummary[]
  scoringProfiles: ScoringProfile[]
  busy: boolean
  error: unknown
  onClose: () => void
  onCreate: (
    divisionId: string,
    date: string,
    start: boolean,
    scoringProfileId: string | null,
  ) => void
}) {
  const [divisionId, setDivisionId] = useState(divisions[0]?.id ?? '')
  const [date, setDate] = useState(() => toDayString(new Date()))
  const [calendarOpen, setCalendarOpen] = useState(false)

  // Null means "whatever the default is", which is what almost every session
  // wants and what the API does with a null.
  const [profileId, setProfileId] = useState<string | null>(null)

  // Asked of the DOM rather than threaded down: whatever the trigger sits in
  // is the right place to portal to, and nothing can disagree with it.
  const [trigger, setTrigger] = useState<HTMLButtonElement | null>(null)

  // The API allows two sessions for one division on one night, since it is
  // unusual rather than wrong. Worth saying out loud all the same: it is
  // almost always somebody making tonight twice.
  const division = divisions.find((d) => d.id === divisionId)
  const clash = existing.find((s) => s.divisionName === division?.name && s.date === date)

  const ready = divisionId !== '' && !busy

  return (
    <div className="flex flex-col gap-5 p-5">
      <h2 className="text-lg font-bold tracking-tight">New session</h2>

      <div>
        <Label>Division</Label>
        <div className="flex gap-2">
          {divisions.map((division) => (
            <button
              key={division.id}
              type="button"
              onClick={() => setDivisionId(division.id)}
              aria-pressed={division.id === divisionId}
              className={[
                'h-11 flex-1 rounded-lg border text-sm font-semibold transition-colors',
                division.id === divisionId
                  ? 'border-foreground bg-primary text-primary-foreground'
                  : 'border-border bg-background hover:bg-muted',
              ].join(' ')}
            >
              {division.name}
            </button>
          ))}
        </div>
      </div>

      <div>
        <Label>Date</Label>
        <Popover open={calendarOpen} onOpenChange={setCalendarOpen}>
          <PopoverTrigger asChild>
            <Button
              ref={setTrigger}
              type="button"
              variant="outline"
              className="h-11 w-full justify-start px-3 font-normal"
            >
              <CalendarIcon />
              {formatDate(date)}
            </Button>
          </PopoverTrigger>
          {/* Into the dialog: a popover sent to document.body lands under it,
              since a dialog opened with showModal is in the top layer. */}
          <PopoverContent className="w-auto p-0" align="start" container={trigger?.closest('dialog')}>
            <Calendar
              mode="single"
              selected={fromDayString(date)}
              defaultMonth={fromDayString(date)}
              onSelect={(picked) => {
                if (!picked) return
                setDate(toDayString(picked))
                setCalendarOpen(false)
              }}
              autoFocus
            />
          </PopoverContent>
        </Popover>
      </div>

      {/* Always said, even when there is nothing to decide. A session should
          state what it will be scored under; only the CHOOSING is conditional
          on the church having more than one set. */}
      {scoringProfiles.length === 1 && (
        <div>
          <Label>Scoring</Label>
          <p className="text-sm text-muted-foreground">
            <span className="font-medium text-foreground">{scoringProfiles[0].name}</span>
            <span className="tabular-nums"> · {scoringProfiles[0].config.placePoints.join(' / ')}</span>
          </p>
        </div>
      )}

      {scoringProfiles.length > 1 && (
        <div>
          <Label>Scoring</Label>
          <div className="flex flex-wrap gap-2">
            {scoringProfiles.map((profile) => {
              const selected =
                profileId === profile.id || (profileId === null && profile.isDefault)

              return (
                <button
                  key={profile.id}
                  type="button"
                  onClick={() => setProfileId(profile.id)}
                  aria-pressed={selected}
                  className={[
                    'h-11 rounded-lg border px-3 text-sm font-semibold transition-colors',
                    selected
                      ? 'border-foreground bg-primary text-primary-foreground'
                      : 'border-border bg-background hover:bg-muted',
                  ].join(' ')}
                >
                  {profile.name}
                </button>
              )
            })}
          </div>
        </div>
      )}

      {clash && (
        <p className="flex items-start gap-2 rounded-lg bg-muted p-3 text-sm text-muted-foreground">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" />
          <span>
            {division?.name} already has a session on this date. Making another is allowed, but
            the board will have two to choose between.
          </span>
        </p>
      )}

      {error instanceof ApiError && <p className="text-sm text-destructive">{error.message}</p>}

      <div className="flex flex-col gap-2">
        <Button
          size="lg"
          className="h-12"
          disabled={!ready}
          onClick={() => onCreate(divisionId, date, true, profileId)}
        >
          <Play />
          {/* Named for what it does rather than for the two calls behind it.
              A session made in advance is started by a button with this same
              label on the setup screen, and the two routes to a live session
              should not be two different words for starting one. */}
          {busy ? 'Working...' : 'Start session'}
        </Button>

        {/* Starting freezes tonight's scoring rules onto the session and
            cannot be undone, so the other way out is spelled out rather than
            left as an unexplained second button. */}
        <p className="text-center text-xs text-muted-foreground">
          Starting opens the board and fixes tonight&rsquo;s scoring rules.
        </p>

        <div className="mt-2 flex gap-2 border-t pt-3">
          <Button
            variant="outline"
            className="flex-1"
            disabled={!ready}
            onClick={() => onCreate(divisionId, date, false, profileId)}
          >
            Create for later
          </Button>

          <Button variant="ghost" disabled={busy} onClick={onClose}>
            Cancel
          </Button>
        </div>
      </div>
    </div>
  )
}

function Label({ children }: { children: React.ReactNode }) {
  return (
    <span className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground uppercase">
      {children}
    </span>
  )
}

/**
 * A session date is a calendar day, not an instant.
 *
 * Both directions go through local date parts rather than toISOString, which
 * shifts by the timezone offset and lands a Friday night session on the
 * Saturday for anyone west of UTC.
 */
function toDayString(value: Date): string {
  const month = `${value.getMonth() + 1}`.padStart(2, '0')
  const day = `${value.getDate()}`.padStart(2, '0')
  return `${value.getFullYear()}-${month}-${day}`
}

function fromDayString(value: string): Date {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year, month - 1, day)
}
