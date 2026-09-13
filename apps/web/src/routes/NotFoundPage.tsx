import { Link } from 'react-router'
import { Button } from '@/components/ui/button'

export function NotFoundPage() {
  return (
    <div className="flex min-h-dvh items-center justify-center bg-background px-5 text-foreground">
      <div className="text-center">
        <h1 className="text-2xl font-bold tracking-tight">Page not found</h1>
        <p className="mx-auto mt-2 max-w-sm text-sm leading-relaxed text-muted-foreground">
          If you were opening a board, check the address on the screen. Board links look like{' '}
          <code className="rounded bg-muted px-1 py-0.5">/board/tnt-2026-10-02</code>.
        </p>
        <Button asChild size="lg" className="mt-6">
          <Link to="/">Go to live boards</Link>
        </Button>
      </div>
    </div>
  )
}
