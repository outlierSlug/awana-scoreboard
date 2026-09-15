import { Route, Routes } from 'react-router'
import { AppShell } from '@/routes/AppShell'
import { BoardPage } from '@/routes/BoardPage'
import { ConsolePage } from '@/routes/ConsolePage'
import { GamesPage } from '@/routes/GamesPage'
import { HomePage } from '@/routes/HomePage'
import { ScoringPage } from '@/routes/ScoringPage'
import { LoginPage } from '@/routes/LoginPage'
import { NotFoundPage } from '@/routes/NotFoundPage'
import { RequireSignIn } from '@/components/RequireSignIn'
import { SessionsPage } from '@/routes/SessionsPage'

export default function App() {
  return (
    <Routes>
      {/* Public. No sign-in, because these are URLs typed into a TV. */}
      <Route path="/" element={<HomePage />} />
      <Route path="/board/:slug" element={<BoardPage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/auth/denied" element={<LoginPage denied />} />

      {/* The console, behind the guard. The API refuses these calls regardless;
          this is so a signed-out visitor meets the sign-in page rather than a
          console filling with permission errors. */}
      <Route
        path="/app"
        element={
          <RequireSignIn>
            <AppShell />
          </RequireSignIn>
        }
      >
        <Route index element={<SessionsPage />} />
        <Route path="sessions" element={<SessionsPage />} />
        <Route path="sessions/:id" element={<ConsolePage />} />
        <Route path="games" element={<GamesPage />} />
        <Route path="scoring" element={<ScoringPage />} />
      </Route>

      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
