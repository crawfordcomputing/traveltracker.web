# Contributing to Travel Tracker

Thanks for taking a look. This is a small open-source project (MIT), and issues
and pull requests are both welcome.

## How the project is shaped

One ASP.NET Core Razor Pages app, EF Core on SQL Server, deployed to Azure App
Service by GitHub Actions. The design rule throughout is a **sensible zero-config
default for local dev, with a single configuration switch to a production-grade
service**. Keep that property: anything new that depends on an external service
should be optional, off by default, and documented.

## Before you start

- Planned and in-flight work lives in [GitHub Issues](https://github.com/crawfordcomputing/traveltracker.web/issues).
  Search there first.
- For anything beyond a small fix, open an issue and fill in **Why**, **Already in
  place**, and **Done when**. That is how work gets written up here, and it is
  what makes an issue pickup-able by someone else.
- Typos, doc corrections, and obvious small bugs can go straight to a pull request.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a SQL Server
instance. Windows LocalDB is fine. Azurite covers receipt storage locally.

The repo ships no connection string, so supply one through user-secrets:

```bash
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=(localdb)\MSSQLLocalDB;Database=TravelTracker;Trusted_Connection=True;Encrypt=False" \
  --project src/TravelTracker.Web

dotnet run --project src/TravelTracker.Web
```

First run applies migrations, creates the database if the login can, and seeds
baseline data. `/` is a status page and `/healthz` is the health check. The README
has the full configuration reference.

**Never commit a credential.** `appsettings.json` keeps `ConnectionStrings:Default`
empty on purpose. Sample keys belong in `appsettings.json.example`.

## Build and test

```bash
dotnet build
dotnet test
```

Two things to know:

1. **Warnings fail the build.** `Directory.Build.props` sets
   `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, and
   `AnalysisLevel=latest-recommended`, so `.editorconfig` style violations break
   the build rather than accumulating. Fix them instead of suppressing. If a
   suppression really is correct, scope it as narrowly as possible and explain it
   in the pull request.
2. **Tests run against real SQL Server**, creating and dropping an isolated
   database per test. They default to LocalDB (`MSSQLLocalDB`). Point them
   somewhere else with `TT_TEST_SQL`:

   ```bash
   TT_TEST_SQL="Server=localhost,1433;User ID=sa;Password=Your_password123;Encrypt=True;TrustServerCertificate=True"
   ```

## What CI checks

Every pull request against `main` runs `.github/workflows/deploy.yml`: restore,
build in Release, `dotnet test` against a SQL Server 2022 service container with
coverage collection, then a **line-coverage gate** (currently a floor of 0.80),
then publish. Pushes to `main` run the same build.

The floor is meant to ratchet up as coverage grows. Do not lower it to get a pull
request green.

## Testing expectations

This project has historically shipped some features with their tests deferred:
the full build and test gate ran later, or follow-up tests landed in a separate
pass. That was a solo-development shortcut and it is **not** the bar for
contributions.

For a pull request:

- Behavior changes need tests in `test/TravelTracker.Tests`.
- Run the **full** suite before opening it. A filtered run is a fine inner loop;
  the full run is the gate.
- If you truly have to defer a test, say so in the pull request and open a
  follow-up issue. Do not leave it implicit.
- Do not delete or weaken an existing test to make a change pass. If a test is
  actually wrong, fix it in its own commit and explain why.

## Conventions worth knowing

- **Razor Pages with page models.** Bind through input models rather than
  entities, so new fields cannot be overposted.
- **Migrations.** Any entity change needs an EF Core migration that applies
  cleanly to an existing database. Name it for what it does.
- **Authorization.** New pages that read trips, expenses, or receipts must go
  through the existing access scoping rather than querying the context unscoped.
- **Configuration.** Every new setting needs a default that keeps local dev
  working with no setup, a row in the README settings tables, and a documented
  `Section__Key` environment-variable form.
- **Email.** Wording is admin-editable and stored in the database; code holds the
  defaults. Add new template keys to the defaults and the token spec together.
- **Decisions.** When a change picks between real alternatives (a provider, a
  protocol, a storage model), put the reasoning and the rejected options in the
  pull request description.

## Commits and pull requests

- Branch off `main`, one logical change per pull request.
- Imperative commit subjects ("Add xlsx export", not "Added"), roughly 72
  characters, with the why in the body.
- Link the issue with `Closes #123`.
- Keep unrelated reformatting out of the diff.
- Green CI is required: clean build, passing tests, coverage above the floor.

## Security

Please do not open a public issue for a vulnerability. Report it privately
through the repository's **Security** tab. If private reporting is unavailable,
open an issue asking for a contact address and leave the details out of it.

## License

By contributing you agree that your contributions are licensed under the
[MIT License](LICENSE).
