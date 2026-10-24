# CutWire Studio backend

.NET 10, Clean Architecture. See `../docs/architecture.md`.

## Layering

Dependencies point inward only: `Domain` <- `Application` <- `Infrastructure` <- `Api`. Domain has no dependencies (entities, `Result`/`Error`). Application holds use-case handlers (`ICommandHandler`/`IQueryHandler`, plain DI services, no MediatR) and the abstractions they need (`IAppDbContext`, `IBlobStore`, `IClock`); it may reference EF Core types (`DbSet`) but never Npgsql or ASP.NET. Infrastructure implements those abstractions (EF/Npgsql, filesystem blob store, recurring jobs). Api is the host: endpoints translate `Result`/`Error` to RFC 7807 problems in one place (`Http/ProblemMapping.cs`). Enforced by `ArchitectureTests`.

Validation is hand-rolled: handlers return `Error.Validation(code, message, errors)` with a field-to-messages dictionary, which maps straight onto the problem `errors` extension. The upcoming quality gates are a custom rule pipeline, so FluentValidation would add a dependency without simplifying them.

## Run (dev)

```sh
podman run -d --rm --name studio-pg -p 127.0.0.1:5432:5432 \
  -e POSTGRES_USER=studio -e POSTGRES_PASSWORD=studio -e POSTGRES_DB=studio \
  docker.io/library/postgres:17-alpine
dotnet run --project src/CutWire.Studio.Api   # http://localhost:8087, migrations apply on startup
podman stop studio-pg
```

Configuration is standard ASP.NET (appsettings + env vars, `__` as separator): `ConnectionStrings__Default`, `Storage__BlobRoot`, `PublicUrl`, `Database__AutoMigrate`, `Jobs__Enabled`, `BlobGc__Retention`, `RateLimiting__PermitLimit`, `Spa__Root`, `ASPNETCORE_URLS`. Auth, email and OAuth settings (`Auth__*`, `Email__Smtp__*`) are described in `docs/auth.md`.

In Development the SPAs are served from `../web/dist` (found by walking up from the content root); `Spa__Root` overrides this.

## Test

```sh
export DOCKER_HOST=unix:///run/user/$UID/podman/podman.sock
export TESTCONTAINERS_RYUK_DISABLED=true   # if Ryuk fails under rootless podman
dotnet test
```

`dotnet test` runs on Microsoft.Testing.Platform (`global.json`). Integration tests start `postgres:17-alpine` via Testcontainers. `OpenApiTests` regenerates `openapi.json` (consumed by `web/packages/api-client`); it needs no database:

```sh
dotnet test --project tests/CutWire.Studio.IntegrationTests --filter-class '*OpenApiTests'
```

## Migrations

```sh
dotnet tool restore
dotnet ef migrations add <Name> -p src/CutWire.Studio.Infrastructure -s src/CutWire.Studio.Api -o Persistence/Migrations
```

## Publish

```sh
dotnet publish src/CutWire.Studio.Api -c Release -r linux-x64 -o out
```

Produces a self-contained single-file `CutWire.Studio.Api` (about 58 MB) with `web/dist/**` embedded when it exists (Release builds; `-p:EmbedSpa=false` to skip). `appsettings.json` is written next to it and is optional if everything is supplied by environment variables.

## Adding an app host

Add an `AppHost(prefix, dir, headers)` to `AppHostRegistry.Default` (`Api/Spa/AppHost.cs`). Each gets SPA fallback for extensionless paths, `index.html` as `no-cache`, and `assets/*` as immutable. `/api`, `/connect`, `/mcp` and `/files` never fall back.
