import { Moon, Sun } from 'lucide-react'
import { useTheme, type ResolvedTheme } from '@/lib/themeContext'

const OPTIONS: { value: ResolvedTheme; label: string; Icon: typeof Sun }[] = [
  { value: 'light', label: 'Light', Icon: Sun },
  { value: 'dark', label: 'Dark', Icon: Moon },
]

/**
 * Pick one of two, rather than a single button that flips.
 *
 * A segmented control shows the current state without the reader having to work
 * out whether the icon means "you are here" or "go here", which is the standing
 * ambiguity of a one-button toggle.
 *
 * The theme still starts on whatever the device is set to. Highlighting is
 * driven by the RESOLVED theme, so before anyone touches it the control already
 * shows which way the system went. "System" is not offered as a third option:
 * it is the default and nobody picks it deliberately.
 *
 * On a phone it collapses to one button showing where it would take you, and
 * the ambiguity is worth accepting there: the header has three destinations and
 * a connection state to fit, and a control costing twice the width to be
 * marginally clearer is the wrong trade at 390px.
 */
export function ThemeToggle({ className }: { className?: string }) {
  const { resolved, setTheme } = useTheme()

  const other = resolved === 'dark' ? OPTIONS[0] : OPTIONS[1]

  return (
    <>
      <button
        type="button"
        onClick={() => setTheme(other.value)}
        aria-label={`Switch to ${other.label.toLowerCase()} theme`}
        title={`Switch to ${other.label.toLowerCase()}`}
        className={['flex size-8 shrink-0 items-center justify-center rounded-lg sm:hidden', className]
          .filter(Boolean)
          .join(' ')}
      >
        <other.Icon className="size-4" />
      </button>

      <div
      className={['hidden shrink-0 gap-0.5 rounded-lg p-0.5 sm:inline-flex', className]
        .filter(Boolean)
        .join(' ')}
      style={{ background: 'color-mix(in oklch, currentColor, transparent 92%)' }}
      role="group"
      aria-label="Color theme"
    >
      {OPTIONS.map(({ value, label, Icon }) => {
        const active = resolved === value

        return (
          <button
            key={value}
            type="button"
            onClick={() => setTheme(value)}
            aria-label={label}
            aria-pressed={active}
            title={label}
            className="flex size-7 items-center justify-center rounded-md transition-opacity"
            style={{
              background: active
                ? 'color-mix(in oklch, currentColor, transparent 82%)'
                : 'transparent',
              opacity: active ? 1 : 0.55,
            }}
          >
            <Icon className="size-3.5" />
          </button>
        )
      })}
      </div>
    </>
  )
}
