# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.5.0] - 2026-11-21

**Upgrade notes**

- **The cache starts cold after you deploy.** The `[Cache]` key format changed, so entries written by 1.4.x are never read again; they expire with their TTL.
- **Set `CoreSetup:Redis:PrefixKey`.** With an empty prefix, `DeleteAllAsync` now does nothing and a startup warning is logged.
- **Replace `Audience: "account"`.** Keycloak adds `account` to the tokens of most clients in a realm. Add an audience mapper that puts this API's client id in the token, and set `CoreSetup:Jwt:Audience` to that id.
- **Rewrite a `redis://` or `rediss://` connection string** in the StackExchange.Redis format (`host:port,password=...,ssl=true`). URI forms now fail validation at startup.
- Review endpoints that return the same data to every caller and mark them `UserScope = CacheUserScope.Shared` if you want one shared entry; everything authenticated is now cached per user.

### Security
- **`[Cache]` could serve one user's response to another user.** Keys did not include the caller, so the first response to an authenticated endpoint was replayed to everyone, and a cache hit skipped the authorization checks inside the handler. Entries are now kept per user (a hash of the `sub` claim) whenever the request is authenticated or the endpoint requires authorization. An authenticated caller without a `sub` or `NameIdentifier` claim is never cached.
- **Minimal API `[Cache]` stored error results and replayed them as 200.** `Forbid`, `NotFound`, `Problem` and any other `IResult` were cached and served as a successful response. Only 200 responses with a value are stored now, and nested `Results<...>` unions are unwrapped first.
- **Cache keys could collide.** Arguments were written with `ToString()`, so different DTOs, values containing `:` or `*`, and endpoints sharing a custom key (`/users/42` and `/users/42/permissions` both mapped to `UserInfo:42`) could share one entry. Arguments are now escaped, complex values are hashed, and every key carries its endpoint id.
- **`DeleteAllAsync` with the default empty `PrefixKey` deleted every key in the Redis database.** It now does nothing and logs a warning.
- **Glob characters in the prefix or in a cascade segment matched unrelated keys.** `GetKeysAsync`, `DeleteAllAsync` and `DeleteCascadeAsync` now escape the prefix and the segment, so they match literally, and `DeleteCascadeAsync` refuses an empty segment.
- **Non-GET requests were cached.** A `POST` or `DELETE` on a `[Cache]` endpoint could be answered from the cache without running. Only `GET` and `HEAD` are cached now.
- **A remote Redis without TLS went unnoticed.** A startup warning is logged when the connection string points to a non-loopback host without `ssl=true`.
- **`Audience: "account"` accepted tokens issued for other applications in the realm.** A startup warning now explains how to configure an audience mapper, and the README and sample no longer use `account`.

### ⚠ Behavior changes
- `[Cache]` entries are per user by default (`CacheUserScope.Auto`): per-user when the request is authenticated or the endpoint requires authorization, shared for anonymous requests to public endpoints.
- The cache key format changed to `{customKey}:{args}:{endpointId}:{userSegment}` (or `{endpointId}:{args}:{userSegment}` without a custom key). The cache starts cold after deploy, and code that read `[Cache]` entries by exact key must use the new layout or `DeleteCascadeAsync`.
- Only `GET`/`HEAD` requests with a final 200 response and a value are cached. `string` values, `ProblemDetails` and non-200 results are never cached.
- `DeleteAllAsync` is a no-op, with a warning, when `PrefixKey` is empty.
- Redis connects lazily (`AbortOnConnectFail=false`) and no longer crashes startup when Redis is down; `[Cache]` endpoints are served uncached until it is reachable. The first cached request can wait up to `connectTimeout` (5 s by default). If the cache service cannot be created, `[Cache]` serves uncached and logs a warning.
- `CoreSetup:Redis:ConnectionString` is validated at startup: whitespace counts as missing, it must parse as a StackExchange.Redis configuration string with at least one endpoint, and `redis://`/`rediss://` URIs are rejected.
- `TokenValidation` overrides now change only the properties you set on the default `TokenValidationParameters` instead of replacing the instance, so settings from your own `Configure`/`PostConfigure` calls survive.
- The library's `IConnectionMultiplexer` is a keyed service (`"BoricuaCoder.API.CoreSetup.Redis"`); the unkeyed alias is added with `TryAdd`, so a multiplexer you register yourself wins.

### Added
- `CacheUserScope` enum (`Auto`, `PerUser`, `Shared`) and `CacheAttribute.UserScope`, e.g. `[Cache(600, UserScope = CacheUserScope.Shared)]` or `new CacheAttribute(600) { UserScope = CacheUserScope.Shared }`.
- `CoreSetup:Jwt:ValidAlgorithms`: opt-in allow-list of signing algorithms (e.g. `["RS256"]`). Empty keeps the handler default.
- Startup warnings: empty `Jwt:Authority`, `Jwt:Audience` set to `account`, `ValidateIssuer`/`ValidateAudience`/`ValidateLifetime` disabled, Swagger enabled outside Development (it will be off by default outside Development in 2.0.0), empty `Redis:PrefixKey`, and a non-TLS remote Redis.
- Runtime warnings instead of silently swallowed Redis errors: failed operations, short-circuit timeouts, disconnected primaries, skipped `DeleteAllAsync`/`DeleteCascadeAsync`, and an unavailable cache service. Caller cancellation is logged at Debug. Connection strings are never logged.
- `GetKeysAsync`, `DeleteAllAsync` and `DeleteCascadeAsync` scan every connected primary (Redis Cluster), and deletes are batched per hash slot.
- SourceLink and `.snupkg` symbol packages.
- README: per-user scope, key layout, Keycloak audience mapper, `ValidAlgorithms`, "Extending the defaults" and a Troubleshooting entry for every warning.

### Fixed
- A custom Swagger `RoutePrefix` (e.g. `api-docs`) now serves the JSON at `/{prefix}/{version}/swagger.json`, so the UI loads it. `RoutePrefix: ""` serves the UI at the site root and the JSON at `/{version}/swagger.json`.
- MVC `[Cache]` now caches `ActionResult<T>` and any `ObjectResult` with a 200 (or unset) status; before, only `OkObjectResult` was cached.
- A value that fails to serialize (including a throwing custom converter) is returned normally instead of turning the response into a 500.

### Changed
- Package metadata: SourceLink repository info, symbol packages, NuGet audit, and package validation against 1.4.2 (the public API changes are additive only). Dependency floors are unchanged.
- CI: least-privilege permissions, actions pinned by SHA, and Dependabot.
- Release: publishes with NuGet Trusted Publishing (OIDC) instead of an API key, requires a `## [x.y.z]` section in this CHANGELOG (used as the release notes) and the tag on `main`.

## [1.4.2] - 2026-06-05

### Fixed
- `[Cache]` attribute now respects the application's `JsonSerializerOptions` when serializing responses to Redis. Previously, all cached responses were serialized with System.Text.Json defaults (PascalCase), ignoring any `AddJsonOptions()`/`ConfigureHttpJsonOptions()` configuration. Cache hits now produce the same JSON casing and converters as cache misses.
- `RedisCacheService` now uses the configured HTTP JSON options when serializing/deserializing values in `SetAsync<T>` and `GetAsync<T>` direct calls.

## [1.4.0] - 2026-05-23

### Added
- **Optional Redis response caching** via `CoreSetup:Redis` configuration section — zero impact on existing consumers when `Enabled: false`
- `[Cache(seconds, customKey?)]` attribute that works as both `IAsyncActionFilter` (MVC controllers) and `IEndpointFilter` (Minimal APIs) from a single class
- `ICacheService` public interface with full key management: `GetAsync`, `SetAsync`, `GetKeysAsync`, `DeleteAsync`, `DeleteAllAsync`, and `DeleteCascadeAsync` for hierarchical key invalidation (e.g. `UserInfo:42` + all `UserInfo:42:*` descendants)
- `RedisOptions` configuration class with `Enabled`, `PrefixKey`, `ConnectionString`, `DefaultTTL`, and `ShortCircuit` properties
- Short-circuit protection: Redis operations exceeding `ShortCircuit` seconds are abandoned so a slow or unavailable Redis instance never degrades API response time
- Startup validation for Redis options: `ConnectionString` required when enabled, `DefaultTTL` must be greater than 0, `ShortCircuit` must be non-negative
- Startup log for Redis configuration (prefix key, TTL, short-circuit status)
- `Microsoft.Extensions.Caching.StackExchangeRedis` added as a direct dependency

### Fixed
- All `ICacheService` methods now consistently prepend `PrefixKey` internally; callers always pass relative keys

## [1.3.0] - 2026-04-28

### Added
- `ILogger` support in `UseCoreSetup()` using the `BoricuaCoder.API.CoreSetup` log category
- `Debug` log on startup for JWT configuration (Authority, Audience) and Swagger configuration (Title, Version, RoutePrefix, OAuth ClientId)
- `Warning` log when `RequireHttpsMetadata` is `false` to help catch accidental production misconfigurations
- `Debug` log when Swagger UI is disabled

## [1.2.0] - 2026-04-28

### Added
- `TokenValidationOptions` nested under `JwtOptions` — allows overriding `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, and `ClockSkewSeconds` via `appsettings.json` without breaking existing configurations (all properties default to `null`, meaning "use JWT Bearer handler defaults")

## [1.1.0] - 2026-04-28

### Added
- Startup configuration validation via `IValidateOptions<CoreSetupOptions>` — invalid URIs and incomplete OAuth URL pairs now produce a clear `OptionsValidationException` at startup instead of failing silently or crashing on the first request
- `ValidateOnStart()` registration so misconfigured apps fail fast during startup
- Integration tests using `Microsoft.AspNetCore.TestHost` covering the full middleware pipeline (`UseCoreSetup`, Swagger enabled/disabled, authentication registration)

### Fixed
- `SwaggerSetup` no longer throws `UriFormatException` when OAuth URLs are empty strings; the OAuth security scheme is now only added when both `AuthorizationUrl` and `TokenUrl` are provided

### Changed
- `ServiceCollectionExtensions` now uses `AddOptions<T>().Bind(...).ValidateOnStart()` instead of `Configure<T>()` for options registration

## [1.0.1] - 2025-xx-xx

### Changed
- Updated README with correct CI and Codecov badge links
- Improved CI configuration
- Added code coverage reporting with ReportGenerator and Codecov integration

## [1.0.0] - 2025-xx-xx

### Added
- `AddCoreSetup(IConfiguration)` extension method for registering JWT Bearer authentication and Swagger/OpenAPI services from `appsettings.json`
- `UseCoreSetup()` extension method for configuring the middleware pipeline (Swagger UI, Authentication, Authorization)
- `CoreSetupOptions`, `JwtOptions`, `SwaggerOptions`, and `SwaggerOAuthOptions` configuration classes
- JWT Bearer authentication with configurable `Authority`, `Audience`, and `RequireHttpsMetadata`
- Swagger/OpenAPI with OAuth2 Authorization Code + PKCE flow
- Keycloak-ready configuration with full `appsettings.json` support
- MIT license

[Unreleased]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.5.0...HEAD
[1.5.0]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.4.2...v1.5.0
[1.4.2]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.4.0...v1.4.2
[1.4.0]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/releases/tag/v1.0.0
