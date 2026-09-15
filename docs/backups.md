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

**One repository secret**, under **Settings → Secrets and variables → Actions**:

**`NEON_DIRECT_URL`** — the `postgres://` URI from Neon's dashboard, with
connection pooling **off**, so the host has no `-pooler` in it.

This is not the .NET connection string the API uses. `pg_dump` speaks URIs, and
it needs the direct endpoint because it holds a session open and sets session
state, which PgBouncer in transaction mode does not carry between statements.

**One key pair**, made once on your machine:

```bash
age-keygen -o awana-backup-key.txt
```

It prints `Public key: age1...`. Put that line in `.github/backup-recipient.txt`,
replacing the placeholder, and commit it. Then move `awana-backup-key.txt` into
a password manager and delete it from wherever it landed.

That file is the only thing in the world that can read a backup. Keep a second
copy somewhere that is not the same laptop. If it is lost, every dump taken with
it is scrap.

## Why asymmetric, and not a passphrase

This repository is public, and so are its build artifacts: anyone with a GitHub
account can download them, then attack the file offline, forever, with no rate
limit and nothing to notice it happening.

A passphrase would be the only thing standing in the way, and its strength would
be unauditable from outside the head of whoever chose it. GPG's symmetric mode
also derives its key with S2K, which is salted and iterated but not memory hard,
so guessing is cheap on a GPU.

Encrypting to a public key removes the question rather than answering it. The
key in the repository can only encrypt. Decryption needs a private key that has
never been near GitHub, so there is no shared secret to guess and nothing in
CI worth stealing.

No clubber names are in the database by design, so a dump is a list of
volunteers and a lot of scores. Thin, but not something to publish.

## Restoring

You will need `age` on the machine doing the restore. It is a single binary with
no installer, from <https://github.com/FiloSottile/age/releases>, and it is worth
knowing that now rather than discovering it during an incident.

**Check Neon first.** For anything recent, Neon can branch from a timestamp
before the damage, which is faster than a dump and loses nothing since the last
backup. Reach for a dump when that window has passed, or when Neon itself is the
problem.

Download the artifact from the Actions run, unzip it, then:

```bash
age --decrypt --identity awana-backup-key.txt --output backup.sql backup.sql.age
psql "<direct URI of a NEW, EMPTY Neon branch>" < backup.sql
```

Restore into a **new branch**, never over the live database. Check it, then
point Render's `ConnectionStrings__Default` at that branch's pooled URI and
restart. A restore straight onto a live database turns one bad day into two.

Two things make this work: the dump is taken `--no-owner --no-privileges`, so it
restores cleanly under a different role, and it includes `__EFMigrationsHistory`,
so the API will not try to re-run migrations over restored data.

To check a backup without restoring it, the dump is plain SQL. Open it and read
it.

Backups taken before the switch to `age` are GPG files and still need the old
passphrase. Keep it until those have aged out.

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
