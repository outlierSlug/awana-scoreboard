import { Component, type ErrorInfo, type ReactNode } from 'react'
import { RotateCcw, TriangleAlert } from 'lucide-react'
import { Button } from '@/components/ui/button'

/**
 * Stops one broken render from blanking the screen.
 *
 * React unmounts the whole tree when a render throws, so a single bad value
 * somewhere takes the entire console with it and leaves white. That is a bad
 * outcome anywhere and a terrible one in a gym: the scorekeeper has no console
 * and no idea why, mid round, with a room waiting.
 *
 * A class because this is the one thing hooks cannot do.
 */
export class ErrorBoundary extends Component<
  { children: ReactNode; label?: string },
  { error: Error | null }
> {
  state: { error: Error | null } = { error: null }

  static getDerivedStateFromError(error: Error) {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Until there is somewhere to send these, the console is where a developer
    // will look and the only record that exists.
    console.error('Render failed', error, info.componentStack)
  }

  render() {
    const { error } = this.state
    if (!error) return this.props.children

    return (
      <div className="rounded-xl border border-destructive/30 bg-destructive/5 p-5">
        <p className="flex items-center gap-2 font-medium">
          <TriangleAlert className="size-5 text-destructive" />
          {this.props.label ?? 'This part of the page stopped working'}
        </p>

        <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
          Nothing has been lost: a round is only recorded when the server confirms it. Try again,
          and if it keeps happening reload the page.
        </p>

        <p className="mt-3 font-mono text-xs break-words text-muted-foreground">{error.message}</p>

        <div className="mt-4 flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => this.setState({ error: null })}>
            <RotateCcw />
            Try again
          </Button>
          <Button variant="ghost" onClick={() => window.location.reload()}>
            Reload the page
          </Button>
        </div>
      </div>
    )
  }
}
