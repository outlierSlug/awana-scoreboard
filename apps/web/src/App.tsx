import { Route, Routes } from 'react-router'
import { AppShell } from '@/routes/AppShell'
import { BoardPage } from '@/routes/BoardPage'
import { ConsolePage } from '@/routes/ConsolePage'
import { HomePage } from '@/routes/HomePage'
import { NotFoundPage } from '@/routes/NotFoundPage'
import { SessionsPage } from '@/routes/SessionsPage'

export default function App() {
  return (
    <Routes>
      {/* Public. No sign-in, because these are URLs typed into a TV. */}
      <Route path="/" element={<HomePage />} />
      <Route path="/board/:slug" element={<BoardPage />} />

      {/* The console. Authorization attaches to this shell on day 10, which is
          why it is a separate branch of the tree from the moment it exists. */}
      <Route path="/app" element={<AppShell />}>
        <Route index element={<SessionsPage />} />
        <Route path="sessions" element={<SessionsPage />} />
        <Route path="sessions/:id" element={<ConsolePage />} />
      </Route>

      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
