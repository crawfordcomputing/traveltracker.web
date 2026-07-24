# Travel Tracker

Open-source business travel tracker. Log trips, run approvals, and track travel
expenses and mileage. Built as a single ASP.NET Core app that deploys to Azure
App Service via GitHub Actions with minimal fuss.

> **Status:** Milestone M1 (authentication + roles). See
> [`tasks/todo.md`](tasks/todo.md) for the full roadmap.

## Tech stack

| Concern | Choice | Switch to production with |
|---------|--------|---------------------------|
| Web | ASP.NET Core 10, Razor Pages | — |
| Data | EF Core, **SQL Server** | LocalDB for dev/test, Azure SQL in production (connection string only) |
| Receipts | **Local disk** default | `Storage:Provider=Blob` (M3) |
| Auth | ASP.NET Core Identity (local accounts) | `Auth:EnableEntraId=true` (Entra ID) |
| CI/CD | GitHub Actions | `.github/workflows/deploy.yml` |
| Infra | Bicep (optional) | `infra/main.bicep` |

The design pattern throughout: a sensible zero-config default for local dev and
evaluation, with a single configuration switch to a production-grade service.

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a SQL
Server instance. Any of Azure SQL, a full SQL Server, or Windows
[LocalDB](https://learn.microsoft.com/sql/database-engine/configure-windows/sql-server-express-localdb)
works.

The app has no committed connection string, so supply one before first run. For
local development, store it in [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
so credentials never touch source control:

```bash
# SQL authentication against an on-prem / named server
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=sql01;Database=TravelTracker;User ID=traveltracker.web;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True" \
  --project src/TravelTracker.Web

# ...or Windows LocalDB with integrated auth (no password needed)
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=(localdb)\MSSQLLocalDB;Database=TravelTracker;Trusted_Connection=True;Encrypt=False" \
  --project src/TravelTracker.Web
```

```bash
dotnet run --project src/TravelTracker.Web
```

On first run the app applies EF Core migrations (creating the database if the
login has permission) and seeds baseline data. Then open the printed URL.

- `/` shows a system-status page (provider, seeded counts).
- `/healthz` is the health-check endpoint used by CI and Azure.

> **On-prem TLS:** SQL Server instances without a CA-trusted certificate need
> `Encrypt=True;TrustServerCertificate=True` (encrypted, skips chain
> validation) or `Encrypt=False`. The modern client default (`Encrypt=True`
> with full validation) will otherwise fail to connect.

### Run the tests

```bash
dotnet test
```

Tests target SQL Server too. By default they use Windows LocalDB
(`MSSQLLocalDB`), creating and dropping an isolated database per test. Point
them at another server with the `TT_TEST_SQL` environment variable (useful for
CI with a SQL Server service container):

```bash
TT_TEST_SQL="Server=localhost,1433;User ID=sa;Password=Your_password123;Encrypt=True;TrustServerCertificate=True"
```

## Authentication and roles

The app uses **ASP.NET Core Identity** for local email/password accounts. Three
roles ship out of the box:

- **Employee** — default role for self-registered users.
- **Manager** — approves trips (used from M2 onward).
- **Admin** — manages users and departments.

On first run the database is seeded with the three roles, a `General`
department, and an admin account:

| Field | Default | Override |
|-------|---------|----------|
| Email | `admin@example.com` | `Seed:AdminEmail` |
| Password | `Admin123!` | `Seed:AdminPassword` |

> **Change the seeded admin password before any real deployment.** Set
> `Seed__AdminEmail` / `Seed__AdminPassword` as environment variables (or App
> Service settings) so the default credentials never reach production.

Register, log in, and log out from the nav bar. Admins get an **Admin** menu for
managing users (assign role + department) and departments.

### Optional: Microsoft Entra ID

Single sign-on with Entra ID is off by default and enabled with one switch. Set:

```bash
Auth__EnableEntraId=true
Auth__EntraId__TenantId=<tenant-guid>
Auth__EntraId__ClientId=<app-registration-client-id>
Auth__EntraId__ClientSecret=<client-secret>
```

Register `https://<your-host>/signin-oidc` as a redirect URI on the app
registration. First-time Entra sign-ins are auto-provisioned as local Employee
accounts and linked to the external login. Local accounts keep working
alongside Entra.

## Configuration

All settings live in `appsettings.json` and can be overridden by environment
variables (use `__` as the section separator, e.g. `ConnectionStrings__Default`).

```jsonc
{
  "ConnectionStrings": { "Default": "" },      // required; set per environment
  "Storage": { "Provider": "LocalDisk", "LocalPath": "App_Data/receipts" },
  "Auth": { "EnableEntraId": false },
  "Seed": { "AdminEmail": "admin@example.com", "AdminPassword": "Admin123!" }
}
```

`ConnectionStrings:Default` is intentionally empty in `appsettings.json` and
must be supplied out-of-band: user-secrets locally (see [Run locally](#run-locally)),
and app settings / Key Vault in Azure. The app uses the EF Core SQL Server
provider everywhere, so only the connection string changes between environments.

### Azure SQL (production)

Provide the connection string via an app setting or Key Vault reference:

```bash
ConnectionStrings__Default="Server=tcp:<server>.database.windows.net;Database=traveltracker;User ID=...;Password=...;Encrypt=True;"
```

EF Core migrations run automatically on startup.

## Deploy to Azure

### 1. Provision infrastructure (optional, one command)

```bash
az group create -n travel-tracker-rg -l eastus
az deployment group create -g travel-tracker-rg -f infra/main.bicep \
  -p appName=<globally-unique-name>
```

Add `-p deploySql=true -p sqlAdminPassword=<strong-password>` to also create an
Azure SQL database and point the app at it.

> **Connection string:** set `ConnectionStrings__Default` as an App Service app
> setting (or a Key Vault reference) so credentials stay out of source control
> and image builds.

### 2. Configure the GitHub Actions deploy

Deployment runs from `.github/workflows/deploy.yml` using a service principal
(app registration) authenticated with a client ID + secret.

1. On your app registration, create a client secret and note its value. Ensure
   the registration has the **Website Contributor** role on the App Service (or
   its resource group).
2. In GitHub, under **Settings > Secrets and variables > Actions**, add:
   - **Secrets** `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`,
     `AZURE_SUBSCRIPTION_ID`
   - **Variable** `AZURE_WEBAPP_NAME`

Every push to `main` builds, tests, and deploys. Pull requests build and test
only. After a deploy, hit `https://<app>.azurewebsites.net/healthz` to confirm.

## Project layout

```
src/TravelTracker.Web/       ASP.NET Core Razor Pages app
  Data/                      EF Core context, provider switch, seed, migrations
  Pages/                     UI
test/TravelTracker.Tests/    xUnit unit + integration tests
infra/main.bicep             Optional Azure infrastructure
.github/workflows/deploy.yml GitHub Actions CI/CD
tasks/todo.md                Roadmap (M0–M5)
```

## Contributing

Issues and PRs welcome. See the roadmap in `tasks/todo.md` for what's planned.

## License

[MIT](LICENSE)
