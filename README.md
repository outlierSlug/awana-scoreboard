# Awana Scoreboard

A real-time scoreboard for Awana games night. The scorekeeper enters each round's finish
order on a phone or laptop. Points are calculated from a configurable scoring profile and
shown for review before anything is saved. A projected scoreboard and the leaders' phones
update immediately.

The scoring rules are configurable so that other churches can adapt it, but v1 ships
configured for a single church.

## Status

Live at **<https://awanascoreboard.org>**, ahead of first use on Friday, 2026-10-02.

A whole night runs end to end: create a session and start it, record rounds including
ties, disqualifications and score multipliers, correct or clear a round after the fact,
adjust points with a written reason, finish, and reopen. The public scoreboard follows
along over SignalR without anyone reloading it, and holds the last known scores on screen
rather than blanking if the connection drops.

Still to do before the first real night: a rehearsal on the gym's own TV and wifi, and a
printed card for whoever runs the console when the author is away.

## Stack

| Layer | Choice |
|---|---|
| Frontend | Vite + React 19 + TypeScript, Tailwind CSS v4, shadcn/ui, deployed to Cloudflare Workers |
| Backend | ASP.NET Core (.NET 10) + EF Core, deployed to Render |
| Database | Neon Postgres in production, Postgres via Docker Compose locally |
| Real-time | SignalR, broadcast only. All writes go through REST. |
| Auth | Google OAuth with an HttpOnly same-site cookie and a seeded email allowlist |

The public scoreboard is a public URL with no login. The scorekeeper and admin views
require sign-in.

Both hosts sit under one registrable domain (`awanascoreboard.org` and
`api.awanascoreboard.org`) so the session cookie stays same-site. On `*.workers.dev` and
`*.onrender.com` it would not, since both are on the Public Suffix List, and the cookie
would need `SameSite=None` — which Safari blocks outright.

## Documentation

| Document | What it covers |
|---|---|
| [docs/session-flow.md](docs/session-flow.md) | What a session is and how a night is run |
| [docs/scoring-rules.md](docs/scoring-rules.md) | Placement points, ties, disqualifications, multipliers |
| [docs/game-catalog.md](docs/game-catalog.md) | The games list and how it is edited |
| [docs/deploy.md](docs/deploy.md) | Neon, Render and Cloudflare, in the order they have to be done |
| [docs/backups.md](docs/backups.md) | Weekly encrypted dumps, and how to restore one |
| [docs/dry-run.md](docs/dry-run.md) | Rehearsing a night, including breaking the network on purpose |

## Privacy

This project does not store personally identifiable information about children, by design.
It is a scoreboard, not an attendance system. The database never stores a child's name,
age, or any other identifying detail.

The only person-level records are accounts for the adult leaders who sign in, holding the
email address and display name supplied by Google. Per-team headcounts are an optional
nullable integer, and a session with none recorded is still valid.

## Development

Requires the .NET 10 SDK, Node 22 or later, and Docker Desktop.

```sh
# 1. Database
docker compose up -d

# 2. API, in its own terminal
dotnet run --project apps/api/src/Awana.Api

# 3. Web app, in another terminal
npm install --prefix apps/web
npm run dev --prefix apps/web
```

Then open http://localhost:5200.

| Service | Port |
|---|---|
| Web app | 5200 |
| API | 5201 |
| Postgres | 5433 |

Postgres is on 5433 rather than the default 5432 so it can run alongside
another project's database.

Both the web app and the API run over plain HTTP locally, and that is
deliberate. Browsers treat HTTP and HTTPS as different sites, so serving one
over HTTPS would make the auth cookie cross-site and break sign-in in a way
that looks like an authentication bug rather than a scheme mismatch.

`apps/web/.env.development` is committed because it holds no secrets and the
dev server does not start without it. For personal overrides create
`apps/web/.env.development.local`, which is ignored.

### Tests

```sh
dotnet test apps/api/Awana.slnx      # scoring engine + API against a real Postgres
npm test --prefix apps/web           # round entry reducer
npm run lint --prefix apps/web
npm run build --prefix apps/web      # runs tsc -b, which is the real type check
```

The API integration tests start their own Postgres through Testcontainers, so Docker has
to be running, and they do not touch the development database.

Kill the API before `dotnet test`. A running `Awana.Api.exe` holds a lock on the DLLs it
is about to rebuild, and the failure names MSB3027 rather than the cause.

Type checking must go through `tsc -b`, not `tsc --noEmit`. The web app's `tsconfig.json`
is solution style, with `"files": []` and project references, so `--noEmit` compiles
nothing at all and exits zero however broken the code is.

## License

[MIT](LICENSE).

## Trademark

Awana is a registered trademark of Awana Clubs International. This project is an
independent tool and is not affiliated with or endorsed by Awana Clubs International.
