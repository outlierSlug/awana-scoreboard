import { useQuery } from '@tanstack/react-query'
import { Minus, Plus } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { api, ApiError } from '@/lib/apiClient'
import { useDebounced } from '@/lib/hooks/useDebounced'
import {
  DEFAULT_SCORING_CONFIG,
  DqRule,
  RoundingMode,
  TieRule,
  type SaveScoringProfileRequest,
  type ScoringConfig,
  type ScoringProfile,
} from '@/lib/types'

/**
 * A set of scoring rules, with a worked example of what they do.
 *
 * The preview is the point. Nobody can predict from the words "average shared
 * slots" what two teams tying for first will actually earn, so the dialog runs
 * four rounds through the real engine on the server and shows the answers. A
 * second implementation in the browser would be faster and would eventually
 * disagree with what Friday does, which is the one thing this must not do.
 */
export function ScoringRulesDialog({
  open,
  onClose,
  profile,
  busy,
  error,
  onSave,
}: {
  open: boolean
  onClose: () => void
  /** Null creates a set. Anything else edits that one. */
  profile: ScoringProfile | null
  busy: boolean
  error: unknown
  onSave: (body: SaveScoringProfileRequest) => void
}) {
  return (
    <Modal open={open} onClose={onClose} locked={busy} className="w-[min(40rem,calc(100%-2rem))]">
      {open && <Form profile={profile} busy={busy} error={error} onClose={onClose} onSave={onSave} />}
    </Modal>
  )
}

function Form({
  profile,
  busy,
  error,
  onClose,
  onSave,
}: {
  profile: ScoringProfile | null
  busy: boolean
  error: unknown
  onClose: () => void
  onSave: (body: SaveScoringProfileRequest) => void
}) {
  const [name, setName] = useState(profile?.name ?? '')
  const [config, setConfig] = useState<ScoringConfig>(profile?.config ?? DEFAULT_SCORING_CONFIG)

  // Seeded sets are shown, not edited. The name is a claim about a standard,
  // and the way to change anything is to duplicate it.
  const locked = profile?.isSeeded === true

  // Asked of the DOM rather than threaded down: whatever the trigger sits in is
  // the right place to portal a menu to. A menu sent to document.body lands
  // under a dialog opened with showModal, correctly sized and invisible.
  const [trigger, setTrigger] = useState<HTMLElement | null>(null)

  const ready = !locked && name.trim() !== '' && config.placePoints.length > 0 && !busy

  const set = <K extends keyof ScoringConfig>(key: K, value: ScoringConfig[K]) =>
    setConfig((current) => ({ ...current, [key]: value }))

  const setPlace = (index: number, value: number) =>
    setConfig((current) => ({
      ...current,
      placePoints: current.placePoints.map((p, i) => (i === index ? value : p)),
    }))

  return (
    <div ref={setTrigger} className="flex flex-col gap-5 p-5">
      <div>
        <h2 className="text-lg font-bold tracking-tight">
          {profile ? profile.name : 'New scoring rules'}
        </h2>

        {locked && (
          <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
            The standard table, shown as it is. Duplicate it from the list to make a set you can
            change.
          </p>
        )}
      </div>

      <div>
        <Label htmlFor="rules-name">Name</Label>
        <input
          id="rules-name"
          type="text"
          value={name}
          maxLength={120}
          readOnly={locked}
          autoFocus={profile === null}
          placeholder="Official AWANA"
          onChange={(event) => setName(event.target.value)}
          className="h-11 w-full rounded-lg border border-border bg-background px-3 text-sm font-medium outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
        />
      </div>

      <section>
        <Label>Points table</Label>

        <div className="flex flex-wrap items-end gap-2">
          {config.placePoints.map((points, index) => (
            <label key={index} className="flex flex-col gap-1">
              <span className="text-[11px] font-medium text-muted-foreground">
                {ordinal(index + 1)}
              </span>
              <input
                type="text"
                inputMode="decimal"
                value={points}
                aria-label={`Points for ${ordinal(index + 1)} place`}
                readOnly={locked}
                onChange={(event) => setPlace(index, toNumber(event.target.value))}
                className="h-10 w-16 rounded-lg border border-border bg-background px-2 text-center text-sm font-bold tabular-nums outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
              />
            </label>
          ))}

          <span className="flex h-10 items-center gap-1">
            <Button
              variant="outline"
              size="icon-sm"
              aria-label="Remove the last place"
              disabled={locked || config.placePoints.length <= 1}
              onClick={() => set('placePoints', config.placePoints.slice(0, -1))}
            >
              <Minus />
            </Button>
            <Button
              variant="outline"
              size="icon-sm"
              aria-label="Add a place"
              disabled={locked || config.placePoints.length >= 32}
              onClick={() => set('placePoints', [...config.placePoints, 0])}
            >
              <Plus />
            </Button>
          </span>
        </div>

      </section>

      <section>
        <Label>When teams tie</Label>
        <Choice
          value={config.tieRule}
          container={trigger}
          ariaLabel="What happens when teams tie"
          disabled={locked}
          options={[
            [TieRule.AverageSharedSlots, 'Share the average of the places they used'],
            [TieRule.HighestSlot, 'Each gets the best place they used'],
            [TieRule.LowestSlot, 'Each gets the worst place they used'],
            [TieRule.NoTiesAllowed, 'Ties are not allowed, break them on the night'],
          ]}
          onChange={(value) => set('tieRule', value as typeof config.tieRule)}
        />
      </section>

      <section>
        <Label>When a team is disqualified</Label>
        <Choice
          value={config.dqRule}
          container={trigger}
          ariaLabel="What happens when a team is disqualified"
          disabled={locked}
          options={[
            [DqRule.ZeroButHoldSlot, 'Keeps its place and earns nothing, nobody moves up'],
            [DqRule.ZeroAndPromoteOthers, 'Removed from the order, everyone behind moves up'],
            [DqRule.DropToLast, 'Moved to last, everyone else moves up'],
            [DqRule.CustomPenalty, 'Keeps its place and takes a set penalty'],
          ]}
          onChange={(value) => set('dqRule', value as typeof config.dqRule)}
        />

        {config.dqRule === DqRule.CustomPenalty && (
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <label htmlFor="dq-penalty" className="text-sm text-muted-foreground">
              A disqualified team earns
            </label>
            <input
              id="dq-penalty"
              type="text"
              inputMode="decimal"
              value={config.dqPenaltyPoints}
              readOnly={locked}
              onChange={(event) => set('dqPenaltyPoints', toNumber(event.target.value, true))}
              className="h-9 w-20 rounded-lg border border-border bg-background px-2 text-center text-sm font-bold tabular-nums"
            />
            <span className="text-sm text-muted-foreground">points. Negative takes points away.</span>
          </div>
        )}
      </section>

      {/* Folded away, because on the official table neither of these ever
          comes up: four teams never run out of table, and every possible tie
          on 40/30/20/10 already averages to a whole number. They are here for
          a church whose table does not, rather than for a Friday night. */}
      <details className="group rounded-xl border border-border px-4 py-3">
        <summary className="cursor-pointer list-none text-sm font-medium text-muted-foreground marker:content-none hover:text-foreground">
          <span className="inline-block transition-transform group-open:rotate-90">&rsaquo;</span>{' '}
          More settings
        </summary>

        <div className="mt-4 flex flex-col gap-4">
          <div className="flex flex-wrap items-center gap-2">
            <label htmlFor="beyond-table" className="text-sm text-muted-foreground">
              A team finishing past the end of the table earns
            </label>
            <input
              id="beyond-table"
              type="text"
              inputMode="decimal"
              value={config.pointsBeyondTable}
              readOnly={locked}
              onChange={(event) => set('pointsBeyondTable', toNumber(event.target.value))}
              className="h-9 w-16 rounded-lg border border-border bg-background px-2 text-center text-sm font-bold tabular-nums"
            />
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <label htmlFor="rounding-decimals" className="text-sm text-muted-foreground">
              Round to
            </label>
            <input
              id="rounding-decimals"
              type="text"
              inputMode="numeric"
              value={config.rounding.decimals}
              readOnly={locked}
              onChange={(event) =>
                set('rounding', {
                  ...config.rounding,
                  decimals: Math.min(9, Math.max(0, Math.trunc(toNumber(event.target.value)))),
                })
              }
              className="h-9 w-14 rounded-lg border border-border bg-background px-2 text-center text-sm font-bold tabular-nums"
            />
            <span className="text-sm text-muted-foreground">decimal places, a half going</span>

            <Choice
              value={config.rounding.mode}
              container={trigger}
              ariaLabel="How a half rounds"
              className="w-40"
              disabled={locked}
              options={[
                [RoundingMode.HalfAwayFromZero, 'up, away from zero'],
                [RoundingMode.HalfToEven, "to even, banker's"],
              ]}
              onChange={(value) =>
                set('rounding', { ...config.rounding, mode: value as typeof config.rounding.mode })
              }
            />
          </div>
        </div>
      </details>

      <Preview config={config} />

      {error instanceof ApiError && <p className="text-sm text-destructive">{error.message}</p>}

      <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <Button variant="outline" size="lg" disabled={busy} onClick={onClose}>
          {locked ? 'Close' : 'Cancel'}
        </Button>

        {!locked && (
          <Button
            size="lg"
            disabled={!ready}
            onClick={() => onSave({ name: name.trim(), config })}
          >
            {busy ? 'Saving...' : profile ? 'Save changes' : 'Add rules'}
          </Button>
        )}
      </div>
    </div>
  )
}

/**
 * Four rounds, scored by the server with the rules currently on screen.
 *
 * Debounced rather than fired per keystroke: typing "40" passes through "4",
 * and a table nobody meant to write is not worth a request.
 */
function Preview({ config }: { config: ScoringConfig }) {
  const settled = useDebounced(config, 400)

  const preview = useQuery({
    queryKey: ['scoring-preview', settled],
    queryFn: ({ signal }) => api.previewScoring(settled, signal),
    // Rules being edited are invalid most of the way to being valid, and a
    // rejected preview is an answer rather than a failure to keep asking about.
    retry: false,
    staleTime: Infinity,
  })

  return (
    <section className="rounded-xl bg-muted/50 p-4">
      <h3 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
        What these rules do
      </h3>

      {preview.error instanceof ApiError && (
        <p className="mt-2 text-sm text-destructive">{preview.error.message}</p>
      )}

      {preview.data && (
        <ul className="mt-3 flex flex-col gap-3">
          {preview.data.examples.map((example) => (
            <li key={example.title}>
              <p className="text-sm font-medium">{example.title}</p>

              {example.rejected ? (
                <p className="mt-0.5 text-sm text-muted-foreground italic">{example.rejected}</p>
              ) : (
                <div className="mt-1 flex flex-wrap gap-1.5">
                  {example.teams.map((team) => (
                    <span
                      key={team.teamName}
                      title={team.explanation}
                      className={[
                        'inline-flex items-baseline gap-1.5 rounded-lg bg-background px-2 py-1 text-sm ring-1 ring-foreground/10',
                        team.isDisqualified ? 'text-muted-foreground line-through' : '',
                      ].join(' ')}
                    >
                      <span className="text-xs text-muted-foreground">{team.teamName}</span>
                      <span className="font-bold tabular-nums">{team.points}</span>
                    </span>
                  ))}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function Choice({
  value,
  options,
  container,
  ariaLabel,
  className = 'w-full',
  disabled = false,
  onChange,
}: {
  value: number
  options: [number, string][]
  container: HTMLElement | null
  ariaLabel: string
  className?: string
  disabled?: boolean
  onChange: (value: number) => void
}) {
  return (
    <Select value={String(value)} disabled={disabled} onValueChange={(next) => onChange(Number(next))}>
      <SelectTrigger className={className} aria-label={ariaLabel}>
        <SelectValue />
      </SelectTrigger>
      {/* Into the dialog. A menu portalled to document.body renders beneath a
          dialog opened with showModal, which is in the browser's top layer. */}
      <SelectContent container={container?.closest('dialog')}>
        {options.map(([option, label]) => (
          <SelectItem key={option} value={String(option)}>
            {label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

function Label({ htmlFor, children }: { htmlFor?: string; children: React.ReactNode }) {
  return (
    <label
      htmlFor={htmlFor}
      className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground uppercase"
    >
      {children}
    </label>
  )
}

/** 1st, 2nd, 3rd, 4th. Correct through the teens, which catch naive versions. */
function ordinal(n: number): string {
  const last = n % 10
  const teens = n % 100

  if (teens >= 11 && teens <= 13) return `${n}th`
  if (last === 1) return `${n}st`
  if (last === 2) return `${n}nd`
  if (last === 3) return `${n}rd`
  return `${n}th`
}

/** Empty and half-typed values become 0 rather than NaN, which renders blank. */
function toNumber(value: string, allowNegative = false): number {
  const cleaned = value.replace(allowNegative ? /[^0-9.-]/g : /[^0-9.]/g, '')
  const parsed = Number(cleaned)
  return Number.isFinite(parsed) ? parsed : 0
}
