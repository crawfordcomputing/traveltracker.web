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
| Receipts | **Azure Blob** only | `Storage:Blob:ConnectionString` (Azurite `UseDevelopmentStorage=true` in dev) |
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
  "Storage": { "Blob": { "ConnectionString": "UseDevelopmentStorage=true" } },  // Azurite in dev
  "Auth": { "EnableEntraId": false },
  "Seed": { "AdminEmail": "admin@example.com", "AdminPassword": "Admin123!" }
}
```

`ConnectionStrings:Default` is intentionally empty in `appsettings.json` and
must be supplied out-of-band: user-secrets locally (see [Run locally](#run-locally)),
and app settings / Key Vault in Azure. The app uses the EF Core SQL Server
provider everywhere, so only the connection string changes between environments.

> The keys above are the common ones. The app reads more settings than `appsettings.json` shows inline (storage, expenses, auth/Entra ID, registration, sessions, email, seeding). See [Azure App Service settings](#azure-app-service-settings) for the full required/optional reference.

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

## Azure App Service settings

App Service holds these under **Settings > Environment variables > App settings**.
Use the double-underscore form of each key (`Section__Key`), because `:` is not a
valid character in an environment variable name (e.g. `ConnectionStrings__Default`
maps to `ConnectionStrings:Default`). Anything here can instead be a
[Key Vault reference](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references).

### Required

| App setting | What it is |
|-------------|------------|
| `ConnectionStrings__Default` | SQL connection string. **Startup fails if it is empty.** Use a managed-identity string (see below) or a Key Vault reference so no password sits in plain app settings. |
| `Seed__AdminPassword` | Password for the seeded admin account. **Outside Development the app refuses to start until this is set** (the `Admin123!` fallback is dev-only and never reaches production). Only needed until the admin account exists; safe to remove afterward. |

### Required only when a feature is switched on

| App setting(s) | Required when |
|----------------|---------------|
| `Auth__EntraId__TenantId`, `Auth__EntraId__ClientId`, `Auth__EntraId__ClientSecret` | `Auth__EnableEntraId=true` (Entra ID SSO). |
| `Storage__Blob__ConnectionString` | Always (receipts are Blob-only; use `UseDevelopmentStorage=true` for Azurite in dev). |
| `Email__Smtp__Host` **and** `Email__FromAddress` (or `Email__Smtp__Username`) | `Email__Provider=Smtp` (real outbound email). |

> **Heads-up:** `appsettings.json` ships `Auth__EnableEntraId=true`. On a fresh
> deploy that makes the three Entra settings above effectively required. If you are
> not using SSO yet, set `Auth__EnableEntraId=false`.

### Optional (defaults shown)

**Runtime / seeding**

| App setting | Default | Notes |
|-------------|---------|-------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | App Service default. Leave as `Production` for real deployments. |
| `Seed__AdminEmail` | `admin@example.com` | Email for the seeded admin account. |

**Receipt storage**

| App setting | Default | Notes |
|-------------|---------|-------|
| `Storage__MaxReceiptMb` | `10` | Max upload size per receipt. |
| `Storage__Blob__Container` | `receipts` | Blob container name. |

**Expenses**

| App setting | Default | Notes |
|-------------|---------|-------|
| `Expenses__BaseCurrency` | `USD` | Currency assigned to new expenses. |
| `Expenses__ReceiptRequired` | `false` | Require a receipt on expense entry. |
| `Expenses__ReceiptThreshold` | `25.00` | Amount above which a receipt is required. |

**Auth / Entra ID**

| App setting | Default | Notes |
|-------------|---------|-------|
| `Auth__EnableEntraId` | `true` (as shipped) | Master switch for Entra ID SSO. |
| `Auth__RequireConfirmedEmail` | `false` | Require a confirmed email before sign-in. |
| `Auth__EntraId__Instance` | `https://login.microsoftonline.com/` | Authority host. |
| `Auth__EntraId__SyncRolesOnLogin` | `false` | Sync group-mapped roles on each external login. Off by default: the app is the source of truth for roles. |
| `Auth__EntraId__RoleClaimPassthrough` | `true` | Honor role claims sent in the token. |
| `Auth__EntraId__GroupClaimType` | `groups` | Claim type that carries group IDs. |
| `Auth__EntraId__GroupRoleMappings__{n}__GroupId` / `__Role` | (none) | Indexed array mapping an Entra group to an app role, e.g. `Auth__EntraId__GroupRoleMappings__0__GroupId` + `Auth__EntraId__GroupRoleMappings__0__Role`. |

**Registration & sessions**

| App setting | Default | Notes |
|-------------|---------|-------|
| `Auth__Registration__Mode` | `Domain` | `Open`, `Domain`, `Invite`, or `Closed`. Unknown/missing fails closed to `Closed`. |
| `Auth__Registration__AllowedDomains__{n}` | (none) | Indexed array of allowed email domains for `Domain` mode, e.g. `Auth__Registration__AllowedDomains__0=example.com`. |
| `Auth__Mfa__RequireForAdmins` | `false` | Force TOTP enrollment for Admins. |
| `Auth__Session__IdleTimeoutMinutes` | `30` | Sliding session idle timeout. |
| `Auth__Session__AbsoluteExpiryHours` | `8` | Absolute session lifetime. |

**Email (when `Email__Provider=Smtp`)**

| App setting | Default | Notes |
|-------------|---------|-------|
| `Email__Provider` | `Log` | `Log` writes emails to the log; `Smtp` sends for real. |
| `Email__FromAddress` | `no-reply@example.com` | Sender address. |
| `Email__Smtp__Port` | `587` | SMTP port. |
| `Email__Smtp__UseSsl` | `true` | Use SSL/TLS. |
| `Email__Smtp__Username` / `Email__Smtp__Password` | (none) | SMTP credentials. |
| `Email__AppName` | `Travel Tracker` | Product name used by the `{{AppName}}` email template token. |
| `Email__InternalDomains__{n}` | (none) | Indexed array of "our" email domains, e.g. `Email__InternalDomains__0=example.com`. Recipients on these domains get the **Internal** template variant, everyone else **External**. Exact match only (list subdomains explicitly). Falls back to `Auth__Registration__AllowedDomains`; with neither set, every recipient gets the **Any** variant. |

Email wording is editable by admins at **Admin → Email templates** (ADR-0005). Overrides live in the database; the built-in defaults live in code, so "Reset to default" just deletes the override.

**Eval / QA data** (production-safe; act only when explicitly set)

| App setting | Default | Notes |
|-------------|---------|-------|
| `Eval__Seed` / `Eval__Teardown` | `false` | Seed or remove a tagged QA batch on startup. Requires a non-empty `Eval__BatchId` or startup fails. |
| `Eval__BatchId` | (none) | Batch tag; teardown removes exactly this batch. |
| `Eval__EmailDomain` | `eval.traveltracker.test` | Email domain for seeded eval users. |
| `Eval__Password` | (none) | Password for seeded eval users. |

### Connect to SQL with the App Service managed identity

Managed identity removes the SQL password from your app settings entirely: Azure
issues the token, so there is nothing to store or rotate. The EF Core SQL Server
provider (`Microsoft.Data.SqlClient`) supports it through the connection string
alone, no code change required.

**1. Give the App Service a system-assigned identity and point it at SQL:**

```bash
az webapp identity assign -g travel-tracker-rg -n <app-name>

az webapp config appsettings set -g travel-tracker-rg -n <app-name> --settings \
  ConnectionStrings__Default="Server=tcp:<server>.database.windows.net,1433;Database=traveltracker;Authentication=Active Directory Default;Encrypt=True;"
```

For a **user-assigned** identity instead, use
`Authentication=Active Directory Managed Identity;User Id=<identity-client-id>;`.

**2. Make yourself the Entra admin on the SQL server** (needed to create the DB
user in the next step):

```bash
az sql server ad-admin create -g travel-tracker-rg -s <server> \
  --display-name "<you@tenant>" --object-id <your-entra-object-id>
```

**3. Run this against the `traveltracker` database**, connected as that Entra
admin (portal Query editor, or `sqlcmd -G -S <server>.database.windows.net -d traveltracker`).
The name in brackets is the identity's display name, which for a system-assigned
identity equals the App Service resource name:

```sql
-- Create a contained DB user backed by the App Service managed identity
CREATE USER [<app-name>] FROM EXTERNAL PROVIDER;

-- Read + write application data
ALTER ROLE db_datareader ADD MEMBER [<app-name>];
ALTER ROLE db_datawriter ADD MEMBER [<app-name>];

-- Required: the app applies EF Core migrations on startup (CREATE/ALTER TABLE)
ALTER ROLE db_ddladmin ADD MEMBER [<app-name>];
```

> `db_ddladmin` is included because the app self-initializes its schema by running
> migrations at startup. If you apply migrations out-of-band and want least
> privilege at runtime, drop `db_ddladmin` and keep only reader/writer.

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
