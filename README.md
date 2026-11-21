# BoricuaCoder.API.CoreSetup

[![CI](https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/actions/workflows/ci.yml/badge.svg)](https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/actions/workflows/ci.yml)
[![codecov](https://codecov.io/github/nemesis312/BoricuaCoder.API.CoreSetup/branch/main/graph/badge.svg?token=F8OSYQGVCA)](https://codecov.io/github/nemesis312/BoricuaCoder.API.CoreSetup)
[![Changelog](https://img.shields.io/badge/changelog-CHANGELOG.md-blue)](CHANGELOG.md)

A lightweight library that simplifies ASP.NET Core API setup with pre-configured **JWT Bearer authentication**, **Swagger/OpenAPI documentation** with OAuth2 support, and optional **Redis response caching** — all configured via `appsettings.json`.

## Purpose

This package eliminates boilerplate code when setting up new ASP.NET Core APIs. Instead of manually configuring JWT authentication and Swagger with OAuth security schemes, you can configure everything via `appsettings.json` with just **two lines of code**.

## Features

- **JWT Bearer Authentication** - Pre-configured with Authority, Audience, and HTTPS metadata settings
- **Swagger/OpenAPI with OAuth2** - Auto-configured with Authorization Code flow + PKCE
- **Redis Response Caching** - Optional `[Cache]` attribute/endpoint filter with per-user scoping by default, collision-safe auto keys, custom keys, TTL fallback, short-circuit protection, cascade invalidation, and full respect for the app's `JsonSerializerOptions` (camelCase, custom converters, etc.)
- **Keycloak Ready** - Works out of the box with Keycloak or any OIDC provider
- **Configuration-driven** - All settings via `appsettings.json`
- **Minimal API friendly** - Works with both Minimal APIs and Controller-based APIs

## Installation

```bash
dotnet add package BoricuaCoder.API.CoreSetup
```

## Quick Start

### Step 1: Add Configuration

Add the `CoreSetup` section to your `appsettings.json`:

```json
{
  "CoreSetup": {
    "Jwt": {
      "Authority": "https://your-keycloak.com/realms/your-realm",
      "Audience": "my-api",
      "RequireHttpsMetadata": true
    },
    "Swagger": {
      "Enabled": true,
      "Title": "My API",
      "Version": "v1",
      "RoutePrefix": "swagger",
      "OAuth": {
        "AuthorizationUrl": "https://your-keycloak.com/realms/your-realm/protocol/openid-connect/auth",
        "TokenUrl": "https://your-keycloak.com/realms/your-realm/protocol/openid-connect/token",
        "ClientId": "swagger-ui",
        "Scopes": {
          "openid": "OpenID Connect",
          "profile": "User profile",
          "email": "Email address"
        }
      }
    },
    "Redis": {
      "Enabled": false,
      "PrefixKey": "myapp:api::",
      "ConnectionString": "",
      "DefaultTTL": 300,
      "ShortCircuit": 2
    }
  }
}
```

> Redis is **opt-in** — set `Enabled: false` (or omit the section) and no Redis connection is ever attempted.

> `Audience` is this API's client id in Keycloak (`my-api` here), delivered in the token by an audience mapper. Do not use `account`: see [step 4](#4-add-an-audience-mapper-for-the-api) of the Keycloak configuration.

### Step 2: Configure Services

In your `Program.cs`, add the core setup services:

```csharp
using BoricuaCoder.API.CoreSetup.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add CoreSetup (JWT + Swagger with OAuth)
builder.Services.AddCoreSetup(builder.Configuration);

var app = builder.Build();

// Use CoreSetup middleware
app.UseCoreSetup();

app.MapGet("/", () => "Hello World!")
   .RequireAuthorization();

app.Run();
```

That's it! Your API now has:

- JWT Bearer authentication configured
- Swagger UI available at `/swagger` with OAuth2 authorization (redirects to Keycloak)

## Configuration Options

### JwtOptions

| Property               | Type     | Default | Description                                                                                                                                                     |
| ---------------------- | -------- | ------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Authority`            | string   | `""`    | The URL of your identity provider (e.g., Keycloak realm URL). Empty logs a startup warning and every token is rejected                                          |
| `Audience`             | string   | `""`    | The expected `aud` claim: this API's client id, added by a Keycloak audience mapper. Do not use `account` (see [step 4](#4-add-an-audience-mapper-for-the-api)) |
| `RequireHttpsMetadata` | bool     | `true`  | Set to `false` for local development with HTTP identity providers                                                                                               |
| `ValidAlgorithms`      | string[] | `[]`    | Opt-in allow-list of signing algorithms (e.g. `["RS256"]`). Empty keeps the JWT Bearer handler default                                                          |
| `TokenValidation`      | object   |         | Fine-grained token validation overrides (see below)                                                                                                             |

### TokenValidationOptions

All properties are optional. Set only the ones you need to override — `null` keeps the JWT Bearer handler's default behavior. Each value you set changes only that property of the handler's default `TokenValidationParameters`, so anything you configure with `Configure`/`PostConfigure<JwtBearerOptions>` is kept. Setting `ValidateIssuer`, `ValidateAudience` or `ValidateLifetime` to `false` logs a startup warning.

| Property            | Type  | Default | Description                                                                          |
| ------------------- | ----- | ------- | ------------------------------------------------------------------------------------ |
| `ValidateIssuer`    | bool? | `null`  | Override issuer validation. Set to `false` when using multiple issuers              |
| `ValidateAudience`  | bool? | `null`  | Override audience validation                                                         |
| `ValidateLifetime`  | bool? | `null`  | Override lifetime/expiration validation                                              |
| `ClockSkewSeconds`  | int?  | `null`  | Override clock skew tolerance. Default is 300 (5 min). Set to `0` for exact expiry  |

### ValidAlgorithms

By default the JWT Bearer handler accepts any signing algorithm the realm's key supports. To accept only specific algorithms, list them:

```json
"CoreSetup": {
  "Jwt": {
    "Authority": "https://your-keycloak.com/realms/your-realm",
    "Audience": "my-api",
    "ValidAlgorithms": [ "RS256" ]
  }
}
```

Keycloak signs access tokens with `RS256` unless you change the realm's default signature algorithm. A token signed with an algorithm that is not in the list is rejected with 401.

### SwaggerOptions

| Property      | Type   | Default     | Description                                                                                                       |
| ------------- | ------ | ----------- | ----------------------------------------------------------------------------------------------------------------- |
| `Enabled`     | bool   | `true`      | Enable or disable Swagger UI. Enabled outside Development logs a startup warning                                  |
| `Title`       | string | `"API"`     | The title displayed in Swagger UI                                                                                 |
| `Version`     | string | `"v1"`      | API version for the Swagger document                                                                              |
| `RoutePrefix` | string | `"swagger"` | URL prefix for the UI (`/swagger`) and the JSON (`/swagger/v1/swagger.json`). `""` serves the UI at the site root |
| `OAuth`       | object |             | OAuth2 configuration for Swagger authentication                                                                   |

> A custom `RoutePrefix` such as `api-docs` serves the UI at `/api-docs` and the JSON at `/api-docs/v1/swagger.json`. Before 1.5.0 the JSON stayed under `/swagger`, so the UI failed to load it. Spaces and leading or trailing slashes are trimmed, so `/api-docs/` and ` api-docs ` behave like `api-docs`.

### SwaggerOAuthOptions

| Property           | Type   | Default                        | Description                                            |
| ------------------ | ------ | ------------------------------ | ------------------------------------------------------ |
| `AuthorizationUrl` | string | `""`                           | OAuth2 authorization endpoint (Keycloak auth URL)      |
| `TokenUrl`         | string | `""`                           | OAuth2 token endpoint (Keycloak token URL)             |
| `ClientId`         | string | `""`                           | OAuth2 client ID registered in Keycloak                |
| `Scopes`           | object | `{"openid": "OpenID Connect"}` | Available scopes (key: scope name, value: description) |

### RedisOptions

| Property           | Type   | Default | Description                                                                                                                                    |
| ------------------ | ------ | ------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `Enabled`          | bool   | `false` | Activates Redis caching. No connection is opened when `false`.                                                                                 |
| `PrefixKey`        | string | `""`    | Prefix prepended to every cache key (e.g. `"myapp:api::"`). Set it: when empty, a startup warning is logged and `DeleteAllAsync` does nothing. |
| `ConnectionString` | string | `""`    | StackExchange.Redis configuration string (`host:port,password=...,ssl=true`). Required when `Enabled` is `true`. `redis://` URIs are rejected. |
| `DefaultTTL`       | int    | `300`   | Default cache TTL in seconds, used when `[Cache]` is applied without an explicit value.                                                        |
| `ShortCircuit`     | int    | `0`     | Max seconds to wait for Redis before bypassing the cache and calling through. `0` disables the timeout.                                        |

---

## Redis Caching

When `Redis.Enabled` is `true`, the `[Cache]` attribute becomes available. Apply it to an endpoint to cache its response in Redis automatically.

### What gets cached

- Only `GET` and `HEAD` requests. Any other method runs the endpoint without reading or writing the cache.
- Only successful responses: the final status code must be `200` and the endpoint must return a value.
  - **MVC:** an `ObjectResult` with a non-null value and a status of `200` (or none). `Ok(value)` and an `ActionResult<T>` that returns a value both qualify.
  - **Minimal APIs:** a value result such as `Results.Ok(value)` or `TypedResults.Ok(value)` (a `Results<Ok<T>, NotFound>` union is unwrapped first), or a plain return value that is not an `IResult`.
- Never cached: `string` values, `ProblemDetails` / `ValidationProblemDetails`, and any non-200 result (`NotFound`, `Forbid`, `Created`, `Problem`, ...). A value that fails to serialize is returned normally and not cached.
- A cache hit is replayed as `200` with `Content-Type: application/json`.

### Per-user cache scope

By default a cached response is kept **per user** whenever the request is authenticated or the endpoint requires authorization, so one user's response is never served to another user. `UserScope` controls this:

| `UserScope`      | Behavior                                                                                                                                                                         |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Auto` (default) | Per-user when the request is authenticated or the endpoint requires authorization (`[Authorize]`, `.RequireAuthorization()`). Shared for anonymous requests to public endpoints. |
| `PerUser`        | Always per-user. Anonymous requests share a single `%a` entry.                                                                                                                   |
| `Shared`         | One entry for every caller, authenticated or not.                                                                                                                                |

> **Use `Shared` only for data that is identical for every caller**, such as a public catalog or reference data. On an endpoint that returns user-specific or permission-filtered data, `Shared` serves the first caller's response to everyone.

```csharp
// MVC
[Cache(600, UserScope = CacheUserScope.Shared)]

// Minimal API
.AddEndpointFilter(new CacheAttribute(600) { UserScope = CacheUserScope.Shared })
```

The per-user scope marker is `%u` followed by the first 32 hex characters of the SHA-256 hash of the `sub` claim (or `ClaimTypes.NameIdentifier` when there is no `sub`), so the raw user id never appears in the key. An authenticated request with neither claim is **never cached**.

> **Keys vary only by the bound arguments and the user.** Values the handler reads from `HttpContext` itself, such as `Host`, other request headers, cookies or `Accept-Language`, are not part of the key. An endpoint that is multi-tenant by host or personalized by a header must take those values as bound parameters (for example `[FromHeader(Name = "X-Tenant")] string tenant`), or must not use `[Cache]`.

### How cache keys are built

|                  | Key layout                                              |
| ---------------- | ------------------------------------------------------- |
| No `customKey`   | `{PrefixKey}{endpointId}:{args...}:{scope}`             |
| With `customKey` | `{PrefixKey}{customKey}:{args...}:{endpointId}:{scope}` |

- **`endpointId`**: a readable name followed by `%i` and 8 hex characters that identify the endpoint. The readable name is `{Controller}::{Action}` for MVC (e.g. `Users::Get%i…`). For Minimal APIs it is the endpoint name set with `.WithName(...)`; without a name, the route pattern with `/` replaced by `_`, URI-escaped (`/users/{userId}` becomes `users_%7BuserId%7D%i…`). Use `.WithName(...)` to get readable keys. The `%i` hash covers the area, controller type, method and parameter types (MVC), or the route pattern, handler method and `RequireHost` hosts (Minimal APIs), so two controllers named `UsersController` in different namespaces or areas never share keys.
- **`args`**: the action or handler arguments in parameter order, URI-escaped. `null` becomes `%` and an empty string `%e`. Complex objects (DTOs) and strings longer than 64 characters become `%h` plus a SHA-256 hash. Services injected from DI, `HttpContext`, `ClaimsPrincipal` and `CancellationToken` are left out. An injected `IEnumerable<IMyService>` needs `[FromServices]` to be left out. MVC `[BindProperty]` controller properties are not part of the key.
- **`scope`**: always the last segment and always present. `%u` plus 32 hex characters for per-user entries, `%a` for anonymous `PerUser` requests, `%s` for shared entries.
- Every marker (`%`, `%e`, `%h`, `%i`, `%u`, `%a`, `%s`) starts with `%`. Arguments are URI-escaped, so a literal `%` becomes `%25` and no argument can forge an endpoint id or a scope.
- **Short string arguments are stored in clear text.** A string of up to 64 characters appears as-is (URI-escaped) in the Redis key name, where anyone with `SCAN`, `KEYS` or `MONITOR` access can read it. Do not cache endpoints that take secrets (tokens, passwords, API keys) as arguments.
- Keys longer than 512 characters have their arguments hashed, then the endpoint id and the custom key if the key is still too long.

| Example (`PrefixKey` = `myapp:api::`, authenticated caller)                                             | Generated key                                         |
| ------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| `[Cache(300)]` on `ProductsController.GetAll(page=1)` (`[Authorize]`)                                   | `myapp:api::Products::GetAll%i…:1:%u…`                |
| `[Cache(60, "UserInfo")]` on `UsersController.Get(id=42)`                                               | `myapp:api::UserInfo:42:Users::Get%i…:%u…`            |
| `[Cache(120, "UserInfo")]` on `UsersController.GetPermissions(id=42)`                                   | `myapp:api::UserInfo:42:Users::GetPermissions%i…:%u…` |
| `new CacheAttribute(60, "UserInfo")` on `GET /users/42`, `.WithName("GetUser")`                         | `myapp:api::UserInfo:42:GetUser%i…:%u…`               |
| `new CacheAttribute(120, "UserInfo")` on `GET /users/42/permissions`, `.WithName("GetUserPermissions")` | `myapp:api::UserInfo:42:GetUserPermissions%i…:%u…`    |
| `new CacheAttribute(300)` on `GET /users/42` without `.WithName(...)`                                   | `myapp:api::users_%7BuserId%7D%i…:42:%u…`             |
| `new CacheAttribute(600) { UserScope = CacheUserScope.Shared }` on a route named `"GetCatalog"`         | `myapp:api::GetCatalog%i…:%s`                         |

Because the `customKey` and the arguments come first, both `UserInfo` endpoints store user 42 under `UserInfo:42:…`, and a single `DeleteCascadeAsync("UserInfo:42")` removes both entries for every user. Without a `customKey` the key starts with the endpoint id, so there is no shared parent to cascade on: use a `customKey` for entries you need to invalidate together.

> **Build cascade segments with the same encoding as the keys.** Arguments are URI-escaped in the key, so an entry for `a@b.com` is stored under `UserInfo:a%40b.com:…`. Escape each value when you build the segment: `DeleteCascadeAsync($"UserInfo:{Uri.EscapeDataString(email)}")`. Numbers and plain ASCII ids are unchanged by escaping.

> **Changed in 1.5.0:** the key layout is new, so entries written by 1.4.x are never read again. The cache starts cold after you deploy 1.5.0, and the old entries expire with their TTL.

### TTL resolution order

1. Explicit `seconds` passed to `[Cache(seconds)]`
2. `CoreSetup:Redis:DefaultTTL` from configuration
3. Hard default: 300 seconds

### Minimal APIs

Use `.AddEndpointFilter(new CacheAttribute(...))` on the route builder:

```csharp
using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Extensions;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCoreSetup(builder.Configuration);

var app = builder.Build();
app.UseCoreSetup();

// Auto key, per user ([Authorize]) — key: "myapp:api::GetProducts%i…:%u…"
app.MapGet("/products", [Authorize] () =>
{
    return Results.Ok(new[]
    {
        new { Id = 1, Name = "Widget A", Price = 9.99 },
        new { Id = 2, Name = "Widget B", Price = 19.99 }
    });
})
.AddEndpointFilter(new CacheAttribute(300))
.WithName("GetProducts")
.WithTags("Products");

// Public data, one entry for every caller — key: "myapp:api::GetCatalog%i…:%s"
app.MapGet("/catalog", () => Results.Ok(new[] { "Widget A", "Widget B" }))
.AllowAnonymous()
.AddEndpointFilter(new CacheAttribute(600) { UserScope = CacheUserScope.Shared })
.WithName("GetCatalog")
.WithTags("Products");

// Custom key with TTL — key: "myapp:api::UserInfo:{userId}:GetUser%i…:%u…"
app.MapGet("/users/{userId}", [Authorize] (int userId) =>
    Results.Ok(new { userId, name = $"User {userId}" }))
.AddEndpointFilter(new CacheAttribute(60, "UserInfo"))
.WithName("GetUser")
.WithTags("Users");

// Same custom key — key: "myapp:api::UserInfo:{userId}:GetUserPermissions%i…:%u…"
// DeleteCascadeAsync("UserInfo:{userId}") removes this entry and the one above, for every user
app.MapGet("/users/{userId}/permissions", [Authorize] (int userId) =>
    Results.Ok(new { userId, permissions = new[] { "read", "write" } }))
.AddEndpointFilter(new CacheAttribute(120, "UserInfo"))
.WithName("GetUserPermissions")
.WithTags("Users");

app.Run();
```

### MVC Controllers

Apply `[Cache]` directly on action methods. The endpoint id is `{ControllerName}::{ActionName}`:

```csharp
using BoricuaCoder.API.CoreSetup.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

public record ProductDto(int Id, string Name);

[ApiController]
[Route("products")]
[Authorize]
public class ProductsController : ControllerBase
{
    // Auto key, per user — "myapp:api::Products::GetAll%i…:{page}:%u…"
    [HttpGet]
    [Cache(300)]
    public IActionResult GetAll(int page = 1)
    {
        return Ok(new[]
        {
            new { Id = 1, Name = "Widget A", Price = 9.99 },
            new { Id = 2, Name = "Widget B", Price = 19.99 }
        });
    }

    // Custom key, ActionResult<T> — "myapp:api::ProductDetail:{id}:Products::GetById%i…:%u…"
    [HttpGet("{id}")]
    [Cache(60, "ProductDetail")]
    public ActionResult<ProductDto> GetById(int id)
    {
        return new ProductDto(id, $"Widget {id}");
    }

    // Public reference data, one entry for every caller — "myapp:api::Products::GetCategories%i…:%s"
    [HttpGet("categories")]
    [AllowAnonymous]
    [Cache(3600, UserScope = CacheUserScope.Shared)]
    public IActionResult GetCategories()
    {
        return Ok(new[] { "Tools", "Parts" });
    }
}
```

```csharp
[ApiController]
[Route("users")]
[Authorize]
public class UsersController : ControllerBase
{
    // Key: "myapp:api::UserInfo:{id}:Users::Get%i…:%u…"
    [HttpGet("{id}")]
    [Cache(60, "UserInfo")]
    public IActionResult Get(int id)
        => Ok(new { id, name = $"User {id}" });

    // Key: "myapp:api::UserInfo:{id}:Users::GetPermissions%i…:%u…"
    // DeleteCascadeAsync("UserInfo:{id}") removes this entry and the one above
    [HttpGet("{id}/permissions")]
    [Cache(120, "UserInfo")]
    public IActionResult GetPermissions(int id)
        => Ok(new { id, permissions = new[] { "read", "write" } });
}
```

### Cache invalidation

Inject `ICacheService` anywhere in your application to manage keys directly. Keys are always relative to `PrefixKey`:

```csharp
using BoricuaCoder.API.CoreSetup.Caching;

public class UserService
{
    private readonly ICacheService _cache;

    public UserService(ICacheService cache) => _cache = cache;

    // Remove every [Cache] entry stored under UserInfo:{userId} (both endpoints above, for every user)
    public Task InvalidateUserAsync(int userId)
        => _cache.DeleteCascadeAsync($"UserInfo:{userId}");

    // Store and remove your own entries by exact key
    public Task SetDisplayNameAsync(int userId, string displayName)
        => _cache.SetAsync($"DisplayName:{userId}", displayName, TimeSpan.FromMinutes(10));

    public Task RemoveDisplayNameAsync(int userId)
        => _cache.DeleteAsync($"DisplayName:{userId}");

    // List cached user keys (full Redis keys, prefix included) for debugging/monitoring.
    // The pattern is a Redis glob: pass a constant, never user input.
    public Task<IEnumerable<string>> ListUserKeysAsync()
        => _cache.GetKeysAsync("UserInfo:*");
}
```

- `DeleteCascadeAsync(segment)` deletes `{PrefixKey}{segment}` and every key under `{PrefixKey}{segment}:`. The segment matches literally (glob characters such as `*` are escaped), and an empty segment is refused with a warning.
- `DeleteAllAsync()` deletes every key under `PrefixKey`. **With an empty `PrefixKey` it does nothing and logs a warning**, so set `PrefixKey` if you use it.
- Scans run on every connected primary, so Redis Cluster works, and deletes are batched per hash slot. Redis failures are logged as warnings and never thrown to your code.

### Redis connection

- The connection opens lazily on first use, with `AbortOnConnectFail=false`. The app starts even when Redis is down, and `[Cache]` endpoints are served uncached until Redis is reachable. The first cached request can wait up to the StackExchange.Redis `connectTimeout` (5 seconds by default) while it connects. Add `connectTimeout=...` to the connection string to shorten it.
- Use the StackExchange.Redis format: `host:port,password=...,ssl=true`. `redis://` and `rediss://` URIs are rejected at startup. A non-loopback host without `ssl=true` logs a startup warning.
- CoreSetup registers its multiplexer as a keyed service (`"BoricuaCoder.API.CoreSetup.Redis"`) and adds an unkeyed `IConnectionMultiplexer` alias with `TryAdd`, so an `IConnectionMultiplexer` you register yourself wins.
- Known caveat: CoreSetup calls `AddStackExchangeRedisCache`, which replaces an `IDistributedCache` you registered before `AddCoreSetup`.

### Short-circuit protection

When `ShortCircuit` is set (e.g. `2` seconds), any Redis operation that takes longer than the configured limit is abandoned and the request proceeds directly to the handler — no error, no cache. This prevents a slow or unavailable Redis from degrading API response times.

```json
"Redis": {
  "Enabled": true,
  "ShortCircuit": 2
}
```

### JSON serialization

`[Cache]` reads the application's `JsonSerializerOptions` from DI before serializing responses to Redis. This means cached responses always use the same casing, converters, and policies as non-cached responses.

**Default behavior (no options configured):** `JsonSerializerOptions.Default` is used — System.Text.Json defaults apply, which means **PascalCase**.

**MVC — configure via `AddJsonOptions`:**

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
```

**Minimal API — configure via `ConfigureHttpJsonOptions`:**

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
```

Any converter or policy registered this way is automatically applied to both cache misses and cache hits — no extra configuration is needed.

---

## Keycloak Configuration

### 1. Create a Client in Keycloak

1. Go to your Keycloak Admin Console
2. Select your realm
3. Go to **Clients** > **Create client**
4. Configure the client:
   - **Client ID**: `swagger-ui` (or your preferred name)
   - **Client authentication**: `Off` (public client for PKCE)
   - **Authorization**: `Off`

### 2. Configure Client Settings

In the client settings:

| Setting                             | Value                                               |
| ----------------------------------- | --------------------------------------------------- |
| **Root URL**                        | `https://your-api.com`                              |
| **Valid redirect URIs**             | `https://your-api.com/swagger/oauth2-redirect.html` |
| **Valid post logout redirect URIs** | `https://your-api.com/*`                            |
| **Web origins**                     | `https://your-api.com`                              |

### 3. Get the URLs

Your Keycloak URLs follow this pattern:

- **Authorization URL**: `https://{keycloak-host}/realms/{realm}/protocol/openid-connect/auth`
- **Token URL**: `https://{keycloak-host}/realms/{realm}/protocol/openid-connect/token`
- **Authority (for JWT)**: `https://{keycloak-host}/realms/{realm}`

### 4. Add an audience mapper for the API

Keycloak adds the `account` audience to the tokens of most clients in a realm, so `Audience: "account"` accepts tokens that were issued for other applications. Give the API its own audience instead (CoreSetup logs a warning when `Audience` is `account`):

1. Create a client that represents the API, for example **Client ID** `my-api`. It does not need any login flow.
2. Go to **Client scopes** > **Create client scope**, name it `my-api-audience` and save.
3. In that scope, open **Mappers** > **Configure a new mapper** > **Audience**. Set **Included Client Audience** to `my-api` and turn **Add to access token** on.
4. Open the Swagger client (and every other client that calls the API) > **Client scopes** > **Add client scope**, and add `my-api-audience` as **Default**.
5. Set `CoreSetup:Jwt:Audience` to `my-api`.

Decode a token at [jwt.io](https://jwt.io) to check that its `aud` claim contains `my-api`.

## Environment-Specific Configuration

### appsettings.Development.json

```json
{
  "CoreSetup": {
    "Jwt": {
      "Authority": "http://localhost:8080/realms/dev-realm",
      "Audience": "my-api",
      "RequireHttpsMetadata": false
    },
    "Swagger": {
      "Enabled": true,
      "Title": "My API (Dev)",
      "Version": "v1",
      "OAuth": {
        "AuthorizationUrl": "http://localhost:8080/realms/dev-realm/protocol/openid-connect/auth",
        "TokenUrl": "http://localhost:8080/realms/dev-realm/protocol/openid-connect/token",
        "ClientId": "swagger-ui-dev",
        "Scopes": {
          "openid": "OpenID Connect",
          "profile": "User profile",
          "email": "Email address"
        }
      }
    },
    "Redis": {
      "Enabled": true,
      "PrefixKey": "myapp:dev::",
      "ConnectionString": "localhost:6379",
      "DefaultTTL": 60,
      "ShortCircuit": 2
    }
  }
}
```

> To run a local Redis in Docker: `docker run -d -p 6379:6379 redis:alpine`

### appsettings.Production.json

```json
{
  "CoreSetup": {
    "Jwt": {
      "Authority": "https://auth.mycompany.com/realms/prod-realm",
      "Audience": "my-api",
      "RequireHttpsMetadata": true
    },
    "Swagger": {
      "Enabled": false
    },
    "Redis": {
      "Enabled": true,
      "PrefixKey": "myapp:prod::",
      "ConnectionString": "your-redis-host:6379,password=secret,ssl=true",
      "DefaultTTL": 300,
      "ShortCircuit": 2
    }
  }
}
```

## Using the Swagger UI

1. Run your API
2. Navigate to `https://localhost:{port}/swagger`
3. Click the **Authorize** button
4. Select the scopes you want to request
5. Click **Authorize** - you'll be redirected to Keycloak
6. Enter your credentials in Keycloak
7. After successful login, you'll be redirected back to Swagger with the token applied

## What Gets Configured

### Authentication & Authorization

- JWT Bearer authentication scheme as default
- Authorization services registered
- `UseAuthentication()` and `UseAuthorization()` middleware added

### Swagger/OpenAPI

- OpenAPI document generation, served at `/{RoutePrefix}/{Version}/swagger.json`
- OAuth2 Authorization Code flow with PKCE
- Configurable scopes for user selection
- Swagger UI with OAuth client configuration
- A startup warning when Swagger is enabled outside Development

### Extending the defaults

CoreSetup configures the standard ASP.NET Core options, so you can change anything it does not expose in `appsettings.json` with the options pattern. Register these calls after `AddCoreSetup`:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

builder.Services.AddCoreSetup(builder.Configuration);

// JWT Bearer: PostConfigure runs after CoreSetup's configuration
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters.NameClaimType = "preferred_username";
});

// Swagger document generation (filters, schema ids, XML comments, ...)
builder.Services.Configure<SwaggerGenOptions>(o =>
{
    o.CustomSchemaIds(type => type.FullName);
});

// Swagger UI
builder.Services.Configure<SwaggerUIOptions>(o =>
{
    o.DocumentTitle = "My API docs";
});
```

The settings CoreSetup applies itself (the Swagger UI route prefix, JSON endpoint, OAuth client id and PKCE) are set when `UseCoreSetup()` runs, so they take precedence over the same settings in `Configure<SwaggerUIOptions>`. Change them in `appsettings.json` instead.

## Troubleshooting

### Swagger UI returns 404

**Symptoms:** Navigating to `/swagger` returns a 404 Not Found.

**Causes and fixes:**

1. `Swagger:Enabled` is `false` — check your `appsettings.json` (or the environment-specific override that is active).
2. The `RoutePrefix` is not what you expect — the default is `swagger`, so the URL is `/swagger`. If you set `RoutePrefix: "api-docs"`, the URL becomes `/api-docs` and the JSON is at `/api-docs/v1/swagger.json`.
3. `UseCoreSetup()` was not called in `Program.cs` — make sure both `AddCoreSetup()` and `UseCoreSetup()` are present.

```json
"CoreSetup": {
  "Swagger": {
    "Enabled": true,
    "RoutePrefix": "swagger"
  }
}
```

---

### All requests return 401 Unauthorized

**Symptoms:** Every API call returns 401, even with a valid token.

**Causes and fixes:**

1. **`Authority` mismatch** — The JWT handler downloads the OIDC discovery document from `Authority`. If this URL is wrong or unreachable, all tokens will be rejected. Verify the URL resolves and matches the `iss` claim in your token (decode it at [jwt.io](https://jwt.io)).
2. **`Audience` mismatch** — The `aud` claim in the token must contain `Audience`. Add an [audience mapper](#4-add-an-audience-mapper-for-the-api) so tokens carry the API's client id, and set `Audience` to that id.
3. **`RequireHttpsMetadata: true` in local dev** — If your local Keycloak runs on HTTP, set this to `false` in `appsettings.Development.json`.
4. **`ValidAlgorithms` excludes the token's algorithm** — If you set `ValidAlgorithms`, it must include the `alg` from the token header.

```json
"CoreSetup": {
  "Jwt": {
    "Authority": "https://keycloak.example.com/realms/your-realm",
    "Audience": "my-api",
    "RequireHttpsMetadata": false
  }
}
```

> **Tip:** Enable debug logging to see what the library configured at startup:
> ```json
> "Logging": { "LogLevel": { "BoricuaCoder.API.CoreSetup": "Debug" } }
> ```

---

### Startup fails with OptionsValidationException

**Symptoms:** The application throws on startup with a message like `CoreSetup:Jwt:Authority must be a valid absolute URI`.

**Cause:** The library validates all URLs at startup. This happens when a URL is malformed or when only one of `AuthorizationUrl`/`TokenUrl` is provided (they must be set together).

**Fix:** Correct the invalid value in your `appsettings.json`. If you are not using OAuth in Swagger, leave both `AuthorizationUrl` and `TokenUrl` empty (or omit them entirely).

---

### Swagger Authorize button redirects to the wrong URL

**Symptoms:** Clicking **Authorize** in Swagger UI opens a Keycloak login page with an error, or redirects to a wrong callback URL.

**Causes and fixes:**

1. **Wrong `AuthorizationUrl`** — Must point to the Keycloak authorization endpoint: `https://{host}/realms/{realm}/protocol/openid-connect/auth`.
2. **Redirect URI not registered in Keycloak** — Add `https://your-api.com/swagger/oauth2-redirect.html` to the **Valid redirect URIs** of your Keycloak client.
3. **Wrong `ClientId`** — Must match the client registered in Keycloak exactly (case-sensitive).

---

### Tokens expire too quickly / clock skew errors

**Symptoms:** `IDX10223: Lifetime validation failed. The token is expired` appears even with a freshly issued token.

**Cause:** Clock difference between your server and the identity provider. The default tolerance is 5 minutes.

**Fix:** Increase `ClockSkewSeconds` or disable lifetime validation during development:

```json
"CoreSetup": {
  "Jwt": {
    "TokenValidation": {
      "ClockSkewSeconds": 600
    }
  }
}
```

---

### CORS errors when Swagger tries to get a token

**Symptoms:** The browser console shows a CORS error when Swagger UI calls the token endpoint.

**Cause:** CORS is a Keycloak/IdP configuration issue, not a library issue. The API itself does not proxy the token request — the browser calls Keycloak directly.

**Fix:** In Keycloak, add your API's origin (e.g., `https://localhost:5001`) to the **Web origins** of the Swagger client.

---

### Startup fails with "ConnectionString is required" or another Redis connection string error

**Symptoms:** `OptionsValidationException` with one of these messages:

- `CoreSetup:Redis:ConnectionString is required when Redis is enabled.` (an empty or whitespace value)
- `CoreSetup:Redis:ConnectionString uses URI format (redis://), which StackExchange.Redis does not support. Use "host:port,password=...,ssl=true".`
- `CoreSetup:Redis:ConnectionString is not a valid StackExchange.Redis configuration string.`
- `CoreSetup:Redis:ConnectionString must contain at least one host:port endpoint.`

**Fix:** Provide a StackExchange.Redis configuration string, or set `Enabled: false`. Rewrite a `redis://user:password@host:port` URI as `host:port,user=user,password=password` (add `ssl=true` for `rediss://`):

```json
"Redis": {
  "Enabled": true,
  "ConnectionString": "localhost:6379"
}
```

---

### `[Cache]` has no effect / responses are never cached

**Symptoms:** Every request hits the handler; Redis keys are never created.

**Causes and fixes:**

1. `Redis.Enabled` is `false` — `ICacheService` is not registered, so `[Cache]` silently passes through.
2. For Minimal APIs, the attribute must be chained with `.AddEndpointFilter(new CacheAttribute(...))`. Using it as a plain `[Cache]` attribute on a lambda has no effect.
3. The request is not a `GET` or `HEAD` — other methods are never cached.
4. The response is not cacheable — only `200` responses with a value are cached, never `string` values or `ProblemDetails` (see [What gets cached](#what-gets-cached)).
5. The caller is authenticated but the token has neither a `sub` nor a `NameIdentifier` claim, so there is no user to key the entry on and it is never cached.
6. The cache service failed to start — look for the `CoreSetup: Cache service unavailable; serving uncached response.` warning.

---

### Cached responses use PascalCase instead of camelCase

**Symptoms:** The first request returns correctly cased JSON (e.g., `{ "userId": 1 }`), but subsequent requests served from the Redis cache return PascalCase (e.g., `{ "UserId": 1 }`).

**Cause:** This was a bug in versions prior to v1.4.2. The `[Cache]` attribute serialized responses without reading the application's `JsonSerializerOptions`, always producing PascalCase (System.Text.Json default).

**Fix:** Upgrade to `v1.4.2` or later. The `[Cache]` attribute now reads the configured options from DI automatically:
- **MVC:** reads `IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>` (configured via `AddControllers().AddJsonOptions(...)`)
- **Minimal API:** reads `IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>` (configured via `ConfigureHttpJsonOptions(...)`)

No configuration changes are required after upgrading.

---

### Cascade deletion does not remove all expected keys

**Symptoms:** `DeleteCascadeAsync("UserInfo:42")` removes the root key but not the child keys.

**Cause:** Only keys that start with the segment are removed. An endpoint without `customKey: "UserInfo"` stores its key under its endpoint id (e.g. `Users::GetRoles%i…:42:%u…`), not under `UserInfo:42`. Give every endpoint you want to invalidate together the same `customKey`, with the id as its first argument. Segments match the escaped form of the arguments: use `Uri.EscapeDataString(id)` when the id can contain characters such as `@`, `:` or spaces. Use `GetKeysAsync("UserInfo:*")` to inspect what is actually stored.

---

### Warnings logged by CoreSetup

All CoreSetup messages use the `BoricuaCoder.API.CoreSetup` log category and start with `CoreSetup:`. Placeholders in braces are filled in at runtime.

**At startup** (`UseCoreSetup()`):

| Warning                                                                                                                                                                                                                       | What to do                                                                                                                                                          |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CoreSetup: Jwt:Authority is not set; every bearer token will be rejected.`                                                                                                                                                   | Set `CoreSetup:Jwt:Authority` to your realm URL.                                                                                                                    |
| `CoreSetup: Jwt:Audience is 'account'. Keycloak adds this audience to tokens of most clients in the realm, so tokens issued for other applications will be accepted. Configure an audience mapper with this API's client id.` | Add an [audience mapper](#4-add-an-audience-mapper-for-the-api) and set `Audience` to the API's client id.                                                          |
| `CoreSetup: Jwt:TokenValidation:{Name} is disabled. Do not use this setting in production.`                                                                                                                                   | Remove the `false` override of `ValidateIssuer`, `ValidateAudience` or `ValidateLifetime` outside local development.                                                |
| `CoreSetup: RequireHttpsMetadata is disabled for Authority={Authority}. Do not use this setting in production.`                                                                                                               | Keep `RequireHttpsMetadata: false` for a local HTTP Keycloak only.                                                                                                  |
| `CoreSetup: Swagger UI is enabled in the '{Environment}' environment and exposes the full API surface. It will be disabled by default outside Development in 2.0.0. Set CoreSetup:Swagger:Enabled explicitly.`                | Set `CoreSetup:Swagger:Enabled` to `false` in that environment, or to `true` if you mean to expose it. In 2.0.0 Swagger will be off by default outside Development. |
| `CoreSetup: Redis PrefixKey is empty. Cache keys share the database namespace with other apps and DeleteAllAsync is disabled. Set CoreSetup:Redis:PrefixKey.`                                                                 | Set a prefix unique to the app and environment, e.g. `myapp:prod::`.                                                                                                |
| `CoreSetup: Redis connection to a non-loopback host does not use TLS (ssl=true). Credentials and cached responses travel in plain text.`                                                                                      | Add `ssl=true` to the connection string (and the TLS port your Redis provider uses).                                                                                |
| `CoreSetup: Redis PrefixKey '{PrefixKey}' does not end with ':'. DeleteAllAsync and cascade deletes may also match keys of other apps whose prefix starts with the same text.`                                                | End the prefix with `:` (e.g. `myapp:prod::`). Without a separator, `myapp` also matches the keys of an app whose prefix is `myapp2`.                               |

**At runtime:**

| Warning                                                                                                                             | What to do                                                                                                                                                                     |
| ----------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `CoreSetup: Cache service unavailable; serving uncached response.`                                                                  | The cache service could not be created (usually a bad Redis configuration). The exception is attached to the log entry; fix the configuration. Requests keep working uncached. |
| `CoreSetup: Redis cache {Operation} failed for key '{Key}'; continuing without the cache.`                                          | Redis returned an error or is unreachable. Check connectivity and credentials; the request was served without the cache.                                                       |
| `CoreSetup: Redis cache {Operation} for key '{Key}' exceeded the {ShortCircuit}s short-circuit; bypassing the cache.`               | Redis is slower than `ShortCircuit`. Check Redis latency, or raise `ShortCircuit`.                                                                                             |
| `CoreSetup: Redis primary {EndPoint} is not connected; its keys were not scanned.`                                                  | `GetKeysAsync`, `DeleteAllAsync` or `DeleteCascadeAsync` skipped a disconnected primary, so keys on it remain. Retry once it reconnects.                                       |
| `CoreSetup: DeleteAllAsync skipped because CoreSetup:Redis:PrefixKey is empty; refusing to delete every key in the Redis database.` | Set `CoreSetup:Redis:PrefixKey`. Without a prefix, `DeleteAllAsync` would delete the whole database, so it does nothing.                                                       |
| `CoreSetup: DeleteCascadeAsync skipped because the key segment is empty; refusing to delete every key under the prefix.`            | Pass a non-empty segment such as `UserInfo:42`. Use `DeleteAllAsync` to clear the whole prefix.                                                                                |

---

### How to enable detailed logs

Add this to your `appsettings.Development.json` to see what the library configures at startup:

```json
{
  "Logging": {
    "LogLevel": {
      "BoricuaCoder.API.CoreSetup": "Debug",
      "Microsoft.AspNetCore.Authentication": "Debug"
    }
  }
}
```

The `BoricuaCoder.API.CoreSetup` category emits the configured Authority, Audience, Swagger route, and OAuth client ID. `Microsoft.AspNetCore.Authentication` emits detailed JWT validation failures.

---

## Sample Project

A runnable sample is available in [`samples/SampleApi`](samples/SampleApi).

```bash
# Clone the repo, then:
cd samples/SampleApi
dotnet run
# Swagger UI opens at http://localhost:5000/swagger
```

The sample demonstrates:
- `AddCoreSetup()` and `UseCoreSetup()` wired up in `Program.cs`
- A public `/health` endpoint (no auth required)
- A protected `/me` endpoint that returns the caller's JWT claims
- `/products` — cached per user with `CacheAttribute(300)` (auto key `GetProducts%i…:%u…`)
- `/catalog` — public, cached once for every caller with `UserScope = CacheUserScope.Shared`
- `/users/{userId}` — cached with `CacheAttribute(60, "UserInfo")` (key `UserInfo:{userId}:GetUser%i…:%u…`)
- `/users/{userId}/permissions` — same `UserInfo` custom key; `DeleteCascadeAsync("UserInfo:{userId}")` removes both
- Full `appsettings.json` and `appsettings.Development.json` with all available options including Redis

## Releasing

Maintainer checklist for a new version:

1. Add a `## [x.y.z] - YYYY-MM-DD` section to `CHANGELOG.md`. The release workflow publishes that section as the release notes and fails without it.
2. Merge to `main`, then tag the merge commit `vx.y.z` and push the tag. The workflow refuses tags that are not on `main` or are not a SemVer version, and waits for approval in the `nuget-release` environment.
3. After the release, bump the csproj `<Version>` fallback to the next `-local` version (e.g. `x.y.(z+1)-local`) and set `PackageValidationBaselineVersion` to the version just released.

## Requirements

- .NET 10.0 or later
- ASP.NET Core application
- Keycloak or any OpenID Connect provider

## License

MIT
