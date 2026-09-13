import { createContext, useContext } from 'react'

export type Theme = 'light' | 'dark' | 'system'
export type ResolvedTheme = 'light' | 'dark'

export interface ThemeContextValue {
  theme: Theme
  /** What "system" actually resolved to, so consumers never have to ask again. */
  resolved: ResolvedTheme
  setTheme: (theme: Theme) => void
}

/** Kept apart from the provider so editing one does not invalidate the other. */
export const ThemeContext = createContext<ThemeContextValue | null>(null)

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext)
  if (!context) throw new Error('useTheme must be used inside a ThemeProvider.')
  return context
}
