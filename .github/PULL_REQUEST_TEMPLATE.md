## What and why

<!-- One or two sentences. What changes, and the problem it solves. -->

Closes #

## How it was tested

<!-- Tests you added or ran, plus anything you exercised by hand. -->

- [ ] `dotnet build` is clean (warnings fail the build here)
- [ ] `dotnet test` passes locally (full suite, not a filtered run)
- [ ] Checked by hand:

## Checklist

- [ ] No secrets, connection strings, or real data in the diff (`appsettings.json` stays credential-free; sample keys go in `appsettings.json.example`)
- [ ] EF Core migration included if an entity changed, and it applies cleanly to an existing database
- [ ] New configuration defaults to off and is documented in the README settings tables
- [ ] Coverage stays above the CI floor without lowering the floor
- [ ] README or other docs updated if behavior changed
