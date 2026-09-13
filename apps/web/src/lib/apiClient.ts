import { config } from '@/config'
import type {
  CreateRoundRequest,
  Division,
  Game,
  Preview,
  PreviewRequest,
  RoundRecorded,
  Scoreboard,
  SessionDetail,
  Me,
  SessionSummary,
  UpdateRoundRequest,
} from './types'

/**
 * A failure the server described, as opposed to the network falling over.
 *
 * The API answers with RFC 9457 problem documents, which carry a machine
 * readable `code` alongside the prose. Branching on the code rather than on the
 * message means the wording can be improved without breaking the UI.
 */
export class ApiError extends Error {
  // Declared as fields rather than constructor parameter properties, which
  // erasableSyntaxOnly forbids because they emit runtime code.
  readonly status: number
  readonly code: string
  readonly errors: { code: string; message: string }[]

  constructor(
    status: number,
    code: string,
    message: string,
    errors: { code: string; message: string }[] = [],
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.errors = errors
  }

  /** True when retrying the identical request could plausibly succeed. */
  get isRetryable(): boolean {
    return this.status >= 500 || this.status === 408 || this.status === 429
  }
}

interface ProblemDocument {
  title?: string
  detail?: string
  code?: string
  errors?: { code: string; message: string }[]
}

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  signal?: AbortSignal,
): Promise<T> {
  let response: Response

  try {
    response = await fetch(`${config.apiBaseUrl}${path}`, {
      method,
      signal,
      // The auth cookie rides along on every call. Same-site in every
      // environment, which is why the local ports both stay on http.
      credentials: 'include',
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (cause) {
    // Distinguish "the gym wifi dropped" from "the server said no". The UI
    // treats these very differently.
    if (signal?.aborted) throw cause
    throw new ApiError(0, 'network_unreachable', 'Could not reach the server.', [])
  }

  if (response.status === 204) return undefined as T

  const text = await response.text()
  const payload: unknown = text.length > 0 ? JSON.parse(text) : undefined

  if (!response.ok) {
    const problem = (payload ?? {}) as ProblemDocument
    throw new ApiError(
      response.status,
      problem.code ?? 'error',
      problem.detail ?? problem.title ?? `Request failed with ${response.status}.`,
      problem.errors ?? [],
    )
  }

  return payload as T
}

export const api = {
  // Public. No sign-in, because the board is a URL typed into a TV.
  liveSessions: (church: string, signal?: AbortSignal) =>
    request<SessionSummary[]>('GET', `/api/public/live?church=${encodeURIComponent(church)}`, undefined, signal),

  finishedSessions: (church: string, signal?: AbortSignal) =>
    request<SessionSummary[]>(
      'GET',
      `/api/public/finished?church=${encodeURIComponent(church)}`,
      undefined,
      signal,
    ),

  publicScoreboard: (slug: string, signal?: AbortSignal) =>
    request<Scoreboard>('GET', `/api/public/sessions/${encodeURIComponent(slug)}/scoreboard`, undefined, signal),

  // Catalog.
  divisions: (signal?: AbortSignal) =>
    request<Division[]>('GET', '/api/divisions', undefined, signal),

  games: (divisionId?: string, signal?: AbortSignal) =>
    request<Game[]>('GET', divisionId ? `/api/games?divisionId=${divisionId}` : '/api/games', undefined, signal),

  // Sessions.
  sessions: (signal?: AbortSignal) =>
    request<SessionSummary[]>('GET', '/api/sessions', undefined, signal),

  session: (id: string, signal?: AbortSignal) =>
    request<SessionDetail>('GET', `/api/sessions/${id}`, undefined, signal),

  createSession: (divisionId: string, date: string) =>
    request<SessionDetail>('POST', '/api/sessions', { divisionId, date }),

  startSession: (id: string) => request<Scoreboard>('POST', `/api/sessions/${id}/start`),
  finishSession: (id: string) => request<Scoreboard>('POST', `/api/sessions/${id}/finish`),
  reopenSession: (id: string) => request<Scoreboard>('POST', `/api/sessions/${id}/reopen`),

  // Auth.
  me: (signal?: AbortSignal) => request<Me>('GET', '/api/auth/me', undefined, signal),

  logout: () => request<{ returnUrl: string }>('POST', '/api/auth/logout'),

  /**
   * Where to SEND the browser to sign in, rather than something to fetch.
   *
   * The whole point of the round trip is that Google gets the browser and hands
   * it back, and neither leg of that can happen inside an XHR.
   */
  loginUrl: (returnUrl: string) =>
    `${config.apiBaseUrl}/api/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`,

  // Rounds.
  preview: (sessionId: string, body: PreviewRequest, signal?: AbortSignal) =>
    request<Preview>('POST', `/api/sessions/${sessionId}/scoring/preview`, body, signal),

  recordRound: (sessionId: string, body: CreateRoundRequest) =>
    request<RoundRecorded>('POST', `/api/sessions/${sessionId}/rounds`, body),

  /** Corrects a recorded round in place. It keeps its id and its number. */
  updateRound: (roundId: string, body: UpdateRoundRequest) =>
    request<RoundRecorded>('PUT', `/api/rounds/${roundId}`, body),

  voidRound: (roundId: string, reason: string) =>
    request<Scoreboard>('POST', `/api/rounds/${roundId}/void`, { reason }),
}
