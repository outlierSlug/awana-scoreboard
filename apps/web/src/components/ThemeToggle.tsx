import { Moon, Sun } from 'lucide-react'
import { useTheme } from '@/lib/themeContext'

/**
 * One button that flips between light and dark.
 *
 * The icon is where a tap takes you: a moon in light mode, a sun in dark. That
 * is the convention most sites use, and the tooltip and accessible name say it
 * in words for anyone who reads the icon the other way.
 *
 * The theme still starts on whatever the device is set to, because the icon is
 * driven by the RESOLVED theme. "System" is not something to click back to: it
 * is the default, and nobody picks it deliberately.
 *
 * This replaced a two-segment control on wide screens. It showed the current
 * state more explicitly, but at twice the width for a setting touched once, and
 * phones already used this button, so the two layouts disagreed.
 */
export function ThemeToggle({ className }: { className?: string }) {
  const { resolved, setTheme } = useTheme()

  const next = resolved === 'dark' ? 'light' : 'dark'
  const label = `Switch to ${next} mode`

  return (
    <button
      type="button"
      onClick={() => setTheme(next)}
      aria-label={label}
      title={label}
      className={[
        'relative flex size-8 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none',
        className,
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {/* Both drawn, one visible, so the swap can turn rather than blink. The
          motion is skipped for anyone who has asked for less of it. */}
      <Sun
        aria-hidden
        className={[
          'absolute size-4 transition-all duration-300 motion-reduce:transition-none',
          resolved === 'dark' ? 'scale-100 rotate-0 opacity-100' : 'scale-50 -rotate-90 opacity-0',
        ].join(' ')}
      />
      <Moon
        aria-hidden
        className={[
          'absolute size-4 transition-all duration-300 motion-reduce:transition-none',
          resolved === 'dark' ? 'scale-50 rotate-90 opacity-0' : 'scale-100 rotate-0 opacity-100',
        ].join(' ')}
      />
    </button>
  )
}
