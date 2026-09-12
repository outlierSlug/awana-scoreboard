# Awana Scoreboard

A real-time scoreboard for Awana games night. The scorekeeper enters each round's finish
order on a phone or laptop. Points are calculated from a configurable scoring profile and
shown for review before anything is saved. A projected scoreboard and the leaders' phones
update immediately.

The scoring rules are configurable so that other churches can adapt it, but v1 ships
configured for a single church.

## Status

Pre-alpha, scaffolding in progress. Target launch is Friday, 2026-10-02.

Nothing here is usable yet. The design is settled. Architecture notes and the scoring
rules will be documented here as they get built.

## Planned stack

| Layer | Choice |
|---|---|
| Frontend | Vite + React 19 + TypeScript, Tailwind CSS v4, shadcn/ui, deployed to Cloudflare Pages |
| Backend | ASP.NET Core (.NET 10 LTS) + EF Core, deployed to Render |
| Database | Neon Postgres in production, Postgres via Docker Compose locally |
| Real-time | SignalR, broadcast only. All writes go through REST. |
| Auth | Google OAuth with an HttpOnly same-site cookie and a seeded admin allowlist |

The public scoreboard is a public URL with no login. The scorekeeper and admin views
require sign-in.

## Privacy

This project does not store personally identifiable information about children, by design.
It is a scoreboard, not an attendance system. The database never stores a child's name,
age, or any other identifying detail.

The only person-level records are accounts for the adult leaders who sign in, holding the
email address and display name supplied by Google. Per-team headcounts are an optional
nullable integer, and a session with none recorded is still valid.

## Development

Requires the .NET 10 SDK, Node 22 or later, and Docker Desktop.

Setup instructions will be added along with the scaffold.

## License

[MIT](LICENSE).

## Trademark

Awana is a registered trademark of Awana Clubs International. This project is an
independent tool and is not affiliated with or endorsed by Awana Clubs International.
