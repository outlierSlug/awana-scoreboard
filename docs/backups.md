# Backups

Two layers, and they are not substitutes for each other.

**Neon's point-in-time restore** covers the mistake you notice quickly: a bad
delete, a migration that went wrong this morning. It is built in, it needs no
setup, and its window depends on the plan. Check what yours is; on the free plan
it is short, and that is the number that decides how much the second layer
matters.

**`.github/workflows/backup.yml`** covers everything Neon's window does not: a
problem found weeks later, an account or billing lapse, or simply wanting last
October back. It runs every Saturday at 11:00 UTC, after the Friday night that
produced the data, and can be run by hand from the Actions tab before anything
risky.

## Setting it up

Two repository secrets, under **Settings → Secrets and variables → Actions**.

**`NEON_DIRECT_URL`** — the `postgres://` URI from Neon's dashboard, with
connection pooling **off**, so the host has no `-pooler` in it.

This is not the .NET connection string the API uses. `pg_dump` speaks URIs, and
it needs the direct endpoint because it holds a session open and sets session
state, which PgBouncer in transaction mode does not carry between statements.

**`BACKUP_PASSPHRASE`** — a long random passphrase, generated and stored in a
password manager.

**Store it somewhere that is not this database and not this repository.** A
backup whose key was only ever in the thing that broke is not a backup. If it is
lost, every dump taken with it is scrap.

## Why the dumps are encrypted

This repository is public, and so are its build artifacts: anyone can download
them. The dump contains volunteer names and email addresses, so it is encrypted
with AES-256 inside the job and the plaintext is deleted before anything is
stored.

No clubber names are in the database by design, so a dump is a list of
volunteers and a lot of scores. Still not something to publish.

## Restoring

Download the artifact from the Actions run, then:

```bash
gpg --decrypt backup.sql.gpg > backup.sql
psql "<direct connection URI>" < backup.sql
```

Restore into a **new** Neon branch or an empty database first and look at it
before pointing anything at it. A restore straight over a live database turns
one bad day into two.

To check a backup without a full restore, the dump is plain SQL: open it and
read it.

## Two ways this quietly stops working

**GitHub disables scheduled workflows in a repository with no activity for 60
days.** For a project being worked on this never fires, but after launch, if
development goes quiet over the winter, the backups stop and nothing announces
it. If the repository goes quiet, check the Actions tab occasionally, or run the
workflow by hand once in a while to keep it alive.

**A failed run emails the repository owner, and a green run tells you nothing
about what is in the file.** That is why the job refuses to store a dump that is
empty or missing its main tables, rather than succeeding and leaving a pile of
useless files to be discovered on the day they are needed.

## If this ever outgrows artifacts

The natural next home is a private Cloudflare R2 bucket, which you already have
an account for and whose free tier is far larger than this will ever need. That
removes the 90-day retention ceiling and the public-artifact problem in one
move. Not needed yet.
