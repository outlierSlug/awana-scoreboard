/**
 * The only place in the web app that reads an environment variable.
 *
 * Everything else imports `config`. The v1 draft hardcoded the API URL in
 * eight separate files, which meant pointing the app at a different backend
 * was a find-and-replace and missing one of them was silent.
 *
 * Vite inlines these at BUILD time, not at runtime, so a missing variable
 * produces a bundle that is already broken before it is deployed. Throwing at
 * module load turns that into an immediate, obvious failure on first paint
 * rather than a mystery fetch to the string "undefined" later on.
 */

function required(name: string, value: string | undefined): string {
  if (value === undefined || value.trim() === '') {
    throw new Error(
      `Missing required environment variable ${name}. ` +
        `Add it to apps/web/.env.development for local work, or to the ` +
        `Cloudflare Pages environment variables for a deployed build. ` +
        `See apps/web/.env.example.`,
    )
  }
  return value
}

/** Strip any trailing slash so callers can always write `${baseUrl}/api/...`. */
function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, '')
}

export const config = {
  /** Origin of the ASP.NET Core API, with no trailing slash. */
  apiBaseUrl: normalizeBaseUrl(
    required('VITE_API_BASE_URL', import.meta.env.VITE_API_BASE_URL),
  ),
} as const
