import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import App from './App.tsx'
import './index.css'
import { queryClient } from './lib/queryClient'
import { HubProvider } from './lib/signalr/HubProvider'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      {/* One hub connection for the whole app, above the router, so navigating
          between the board and the console does not tear it down. */}
      <HubProvider>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </HubProvider>
    </QueryClientProvider>
  </StrictMode>,
)
