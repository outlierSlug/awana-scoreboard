import { Monitor, Moon, Sun } from 'lucide-react'
import { useTheme, type Theme } from '@/lib/themeContext'

const OPTIONS: { value: Theme; label: string; Icon: typeof Sun }[] = [
  { value: 'light', label: 'Light', Icon: Sun },
  { value: 'dark', label: 'Dark', Icon: Moon },
  { value: 'system', label: 'System', Icon: Monitor },
]

/**
 * Three explicit states rather than a two-way switch, so following the device
 * stays reachable. A plain toggle silently drops that option, and on a shared
 * TV "whatever the machine is set to" is often the right answer.
 */
export function ThemeToggle({ className }: { className?: string }) {
  const { theme, setTheme } = useTheme()

  return (
    <div
      className={['inline-flex gap-0.5 rounded-lg p-0.5', className].filter(Boolean).join(' ')}
      style={{ background: 'color-mix(in oklch, currentColor, transparent 92%)' }}
      role="group"
      aria-label="Color theme"
    >
      {OPTIONS.map(({ value, label, Icon }) => {
        const active = theme === value
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
              background: active ? 'color-mix(in oklch, currentColor, transparent 82%)' : 'transparent',
              opacity: active ? 1 : 0.6,
            }}
          >
            <Icon className="size-3.5" />
          </button>
        )
      })}
    </div>
  )
}
