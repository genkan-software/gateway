# gateway

YARP reverse proxy: session/token validation (via auth), rate limiting, merged Swagger.

Part of Genkan. Architecture, endpoints and the full stack: see the **setup** repo.

## Layout

```
src/
  Genkan.Gateway.Application/
  Genkan.Gateway.Api/
Genkan.Gateway.slnx
```

## Build / run

`Genkan.Shared` comes from GitHub Packages, so restore needs a token with `read:packages`:

```bash
export GITHUB_PACKAGES_TOKEN=ghp_xxx
dotnet build Genkan.Gateway.slnx
docker build --secret id=github_token,env=GITHUB_PACKAGES_TOKEN -t gateway .
```

## Configuration (env)

`Services__AuthUrl`, `ConnectionStrings__Redis`, `Swagger__Enabled`, `RateLimiting__Store` (Memory | Redis), `RateLimiting__Enabled`, `RateLimiting__Policies__user__*`. Routes live in `appsettings.json` (YARP).

## CI / release

- push to `main` -> image `ghcr.io/genkan-software/gateway:main`, `:sha-xxxxxxx`, `:latest`
- tag `vX.Y.Z` -> also `:X.Y.Z` and `:X.Y`. Normally created for you: tagging **setup** releases every service with the same version.
- Pull requests only build.

Secrets: if `GITHUB_TOKEN` cannot read the `Genkan.Shared` package, either grant this repo access in the package settings
(Package settings > Manage Actions access) or add a `PACKAGES_TOKEN` secret (PAT, `read:packages`).
Dependabot (`.github/dependabot.yml`) opens a PR when a new `Genkan.Shared` is published; it needs `PACKAGES_TOKEN` as a *Dependabot* secret too.
