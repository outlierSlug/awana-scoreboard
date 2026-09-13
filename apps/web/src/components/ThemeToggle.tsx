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
 */
export function ThemeToggle({ className }: { className?: string }) {
  const { resolved, setTheme } = useTheme()

  return (
    <div
      className={['inline-flex shrink-0 gap-0.5 rounded-lg p-0.5', className]
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
  )
}
