# Deploying

Three services, in this order: **Neon** (database), **Render** (API), **Cloudflare**
(DNS and the web app). Google's OAuth client sits alongside them.

The order is not arbitrary. Render needs connection strings from Neon and credentials
from Google before it can boot. Cloudflare needs the hostname Render assigns before it
can point a subdomain at it. Doing it the other way round means a service that starts,
crashes, and tells you very little about why.

No secret belongs in this file. It names the settings; the values live in each
dashboard.

## Hostnames

| Host | Serves |
|---|---|
| `awanascoreboard.org` | Cloudflare Pages: the board and the console |
| `www.awanascoreboard.org` | redirect to the apex |
| `api.awanascoreboard.org` | Render: the API and the SignalR hub |

`www` must **redirect**, not serve. A page served from `www` is a different origin from
the API's allowed origin, so every request would fail CORS while the apex worked fine.

Both names share the registrable domain `awanascoreboard.org`, which is what makes the
session cookie same-site. That is the whole reason the app is not on `*.pages.dev` and
`*.onrender.com`: those are both on the Public Suffix List, so they are cross-site to
each other, and the cookie would need `SameSite=None` — blocked in Safari.

## 0. Point the domain at Cloudflare

Skip if the domain was bought through Cloudflare Registrar; it is already there.

Otherwise: add the site in Cloudflare, then change the nameservers at the registrar to
the two Cloudflare gives you. Propagation is usually minutes and occasionally hours.
Nothing below works until Cloudflare is authoritative, so start here and let it settle
while you do Neon.

## 1. Neon

1. Create a project in a region near the users (US West for Seattle).
2. Take **both** connection strings from the dashboard:
   - the **pooled** one, whose host contains `-pooler`
   - the **direct** one, without it
3. Append `;Max Auto Prepare=0` to the **pooled** string.

   Neon fronts Postgres with PgBouncer in transaction mode, which is incompatible with
   Npgsql's automatic prepared statements: the symptom is intermittent failures under
   load naming a prepared statement nobody wrote.

   Being precise about what this setting is for, because it is easy to over-trust:
   Npgsql already defaults `Max Auto Prepare` to 0, so an unset connection string is
   safe today. Writing it explicitly is a guard against somebody turning it on later
   without knowing what is downstream, not a fix for a live problem. Verified against
   Npgsql 9.0.4: with the parameter absent, `MaxAutoPrepare` reads 0.

   Spell it exactly. Npgsql rejects an unknown keyword outright rather than ignoring
   it, so a typo here is not a subtle misconfiguration; the API refuses to start with
   `Couldn't set max auto prepare`.
4. Leave the direct string alone. Migrations use it, and DDL must not go through a
   pooler.

Keep the local Docker Postgres as the development database. A Neon branch is the right
home for staging if one is ever wanted.

## 2. Google Cloud Console

In the OAuth client for this project, the **authorized redirect URIs** must include
both:

```
https://api.awanascoreboard.org/signin-google
http://localhost:5201/signin-google
```

The production one is on the **API's** host, not the web app's, because the browser is
redirected to the API and the API completes the exchange.

This is the one setting that cannot be discovered by trying. Everything else in this
document announces its own failure; a missing redirect URI produces
`redirect_uri_mismatch` in Google's UI and nothing at all in the API's logs.

## 3. Render

`render.yaml` at the repo root describes the service, so deploy it as a **Blueprint**
rather than configuring a web service by hand. It already sets the region, the Starter
plan, the Docker runtime, `rootDir`, the health check path, and `numInstances: 1`.

`numInstances` must stay **1**. SignalR keeps its connection and group state in the
memory of whichever instance holds the socket, so with two instances a board connected
to A never hears a broadcast raised on B, and it stops updating with no error anywhere.
Scaling past one needs a Redis backplane first.

Then set the variables marked `sync: false`, which Render will prompt for:

| Key | Value |
|---|---|
| `ConnectionStrings__Default` | Neon **pooled** + `Max Auto Prepare=0` |
| `Database__MigrationsConnectionString` | Neon **direct** |
| `Cors__AllowedOrigins__0` | `https://awanascoreboard.org` |
| `Authentication__Google__ClientId` | from the Google OAuth client |
| `Authentication__Google__ClientSecret` | from the Google OAuth client |
| `Seed__AdminEmails` | your address |
| `Seed__GamesLeaderEmails` | comma separated, may be empty |
| `Seed__ScorekeeperEmails` | comma separated, may be empty |
| `Seed__ChurchName` | the display name for the club |

Two traps in that table:

**The migrations key is `Database__MigrationsConnectionString`.** Not
`ConnectionStrings__Migrations`, which looks right and is the correct name for the
design-time factory `dotnet ef` uses locally. At runtime the value is read off
`DatabaseOptions`, whose section is `Database`. Get this wrong and migrations run
through the pooler, which is the PgBouncer failure above.

**Do not set `Seed__ChurchSlug`.** It defaults to `church` and the public home page asks
for that exact slug, hardcoded, because v1 ships configured for one church. Setting it
to something truer leaves the API answering correctly and the home page listing nothing,
with no error. Change both or neither.

An address in none of the three allowlists cannot sign in at all: the API refuses it
rather than quietly creating a viewer. Put your own address in `Seed__AdminEmails`
before the first deploy or you will be locked out of your own instance.

Once the first deploy is green, add `api.awanascoreboard.org` as a custom domain. Render
will show the CNAME target to use in the next step.

## 4. Cloudflare

### DNS

Add a CNAME for `api` pointing at the target Render gave you, and set it to **DNS only**
(grey cloud) to begin with. Render needs to reach the name directly to verify it and
issue a certificate; proxying before that is the usual reason verification hangs. It can
be proxied later, but if you do, set the zone's SSL mode to **Full (strict)**, or
Cloudflare and Render will each terminate TLS and disagree about it.

The apex is created for you in the next step. Add a redirect rule sending
`www.awanascoreboard.org/*` to `https://awanascoreboard.org/$1`.

### Workers

Cloudflare steers new projects to **Workers**, not Pages. Take it. `apps/web/wrangler.jsonc`
is written for it, and its `not_found_handling: "single-page-application"` is native SPA
routing, which is more dependable than the `_redirects` rule Pages relied on.

Create a Worker from the repo:

- **Root directory** `apps/web`
- **Build command** `npm ci && npm run build`
- **Deploy command** `npx wrangler deploy`
- **Node version** 22

There is no output-directory field. `wrangler.jsonc` names `./dist` instead, and the
Worker has no `main`: it serves files and runs no code.

One environment variable, and the build fails without it by design:

```
VITE_API_BASE_URL = https://api.awanascoreboard.org
```

Vite inlines this at **build** time, so changing it needs a rebuild, not a restart, and
`src/config.ts` throws at module load if it is missing rather than letting the app fetch
the string `undefined` at runtime.

Then add `awanascoreboard.org` as a custom domain on the Worker. Cloudflare writes the
apex DNS record itself.

One thing to know: preview and branch deployments get `*.workers.dev` URLs, which are
cross-site to the API, so **auth does not work on previews**. They still exercise the
public board, which is most of what they are useful for.

## 5. Smoke test

In this order, because each step depends on the one above it:

1. `https://api.awanascoreboard.org/api/health` returns `{"status":"ok"}`.
2. `https://awanascoreboard.org` loads and shows the public home page.
3. Sign in. This is the step that fails if the redirect URI or the forwarded headers are
   wrong.
4. Create a session, start it, and open the board in a second window.
5. Record a round with a tie and a round with a disqualification, and watch both reach
   the board without reloading it. This is the console to SignalR to board path, and it
   is the one that cannot be checked any other way.
6. Edit a round, then clear one, and confirm the totals recompute on the board.
7. Finish the session and confirm the board says so and stops saying Live.

## Kill switches

- `Database__RunMigrationsOnStartup=false` stops migrations on the next restart, for
  when a migration is the thing going wrong at a bad moment. The schema must already be
  up to date.
- Render keeps previous deploys and can roll back to one without a rebuild.
- The board degrades rather than disappears: if the API is unreachable it keeps the last
  standings on screen and marks them stale. It does not blank.

## The thing to keep on paper

A clipboard and the current scores, at every dry run and on launch night. Nothing in
this document removes the need for it.
