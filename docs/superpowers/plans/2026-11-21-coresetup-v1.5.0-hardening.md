# CoreSetup v1.5.0 Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship BoricuaCoder.API.CoreSetup 1.5.0. It closes the critical cross-user cache leak and the High cache/Redis findings, fixes the Swagger RoutePrefix bug, adds JWT hardening warnings, and brings the NuGet package and CI/CD up to publishing standard. It stays on `net10.0`.

**Architecture:** The public API changes only by addition (`CacheUserScope`, `CacheAttribute.UserScope`, `JwtOptions.ValidAlgorithms`). `ICacheService` keeps its exact signature. Behavior changes are limited to `[Cache]` (key format, per-user scoping, what gets cached), the Redis connection (lazy, keyed) and `DeleteAllAsync` with an empty prefix. All of them are listed in the CHANGELOG under "⚠ Behavior changes". The redesign (native OpenAPI, OutputCaching/HybridCache) is out of scope and goes to 2.0.0.

**Tech Stack:** .NET 10 (SDK 10.0.300), ASP.NET Core, Swashbuckle 7.3.2, Microsoft.OpenApi 1.6.23, StackExchange.Redis through Microsoft.Extensions.Caching.StackExchangeRedis 10.0.2, xunit 2.9.3, NSubstitute 5.3, Testcontainers.Redis (new, tests only).

**Spec:** The 2026-11-21 analysis (architect, security, qa and devops reports) plus the owner's decisions, both saved in bc-dev-memory (project `BoricuaCoder.API.CoreSetup`, tags `full-analysis`, `decision`). The finding IDs (#1, N1, #3…) come from the security report.

## Global Constraints

- `TargetFramework` stays `net10.0` only. No multi-targeting.
- **Library dependency floors do not change:** JwtBearer 10.0.2, StackExchangeRedis 10.0.2, Microsoft.OpenApi 1.6.23, Swashbuckle 7.3.2. Raising a floor triggers NU1605 in consumers that pin these versions (TestAPI-CoreSetup pins OpenApi 1.6.23). Only test-project packages may change.
- No Swashbuckle 8+ and no Microsoft.OpenApi 2.x. Both are reserved for 2.0.0.
- `ICacheService` member signatures do not change. `EnablePackageValidation` against baseline 1.4.2 must pass, so every public API change is additive.
- `[Cache]` default user scope is `Auto`: per-user when the request is authenticated or the endpoint carries `IAuthorizeData`, with `Shared` as an explicit opt-out (owner decision).
- Swagger stays enabled by default. 1.5.0 only logs a Warning outside Development (owner decision).
- Redis integration tests use Testcontainers in the existing test project with `[Trait("Category", "Integration")]` (owner decision).
- Logs use the category `BoricuaCoder.API.CoreSetup` (`ApplicationBuilderExtensions.LogCategory`), and every message starts with `CoreSetup:`.
- Git: work on the branch `feat/v1.5.0-hardening` from `main`. Stage explicit paths only and run `git diff --cached --stat` before each commit. No push, tag or PR.
- Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Authenticated user without a `sub` or `NameIdentifier` claim on a per-user endpoint: the response must **not** be cached, and must never be cached into a shared key. Covered in Task 2, `Resolve_AuthenticatedWithoutIdClaim_CannotCache`.
2. Minimal API handler typed `Results<Ok<T>, NotFound>`: NotFound is never cached, and Ok caches only the value. Covered in Task 3, `InvokeAsync_NestedResultsNotFound_DoesNotCache`.
3. Redis unreachable at startup: the app must still start and serve uncached responses. Covered in Task 5, `App_StartsAndServes_WhenRedisUnreachable`.
4. An argument containing `:` or `*`, and `DeleteCascadeAsync("*")`: keys must not collide, and the call must not delete unrelated keys. Covered in Task 2, `EncodeArg_EscapesSeparatorAndGlob`, and Task 5, `DeleteCascadeAsync_GlobSegment_DeletesOnlyLiteral`.
5. `RoutePrefix = ""` (UI at the site root): the UI must load `/v1/swagger.json`. Covered in Task 6, `BuildSwaggerJsonPath_EmptyPrefix`.

## File Structure

| File | Responsibility |
|---|---|
| `Caching/CacheUserScope.cs` (new) | Public enum `Auto`/`PerUser`/`Shared` |
| `Caching/CacheUserScopeResolver.cs` (new) | Decides whether a request can be cached and returns its user segment |
| `Caching/CacheKeyGenerator.cs` | v2 key format and argument encoding |
| `Caching/CacheAttribute.cs` | Method filter, user scope, response filtering |
| `Caching/RedisCacheService.cs` | Glob escaping, all primaries, logging, empty-prefix guard, short-circuit |
| `Setup/RedisSetup.cs` | Lazy keyed multiplexer, insecure-remote detection |
| `Setup/JwtAuthenticationSetup.cs` | Mutates the default token validation parameters; `ValidAlgorithms` |
| `Options/JwtOptions.cs` | `ValidAlgorithms` |
| `Extensions/ApplicationBuilderExtensions.cs` | Swagger route fix and startup warnings |
| `Extensions/ServiceCollectionExtensions.cs` | Skips Redis wiring when the connection string is empty |
| Tests: `Caching/*`, `Integration/Redis/*` (new), `Integration/*` | Per task |
| `BoricuaCoder.API.CoreSetup.csproj` | Package metadata and validation |
| `.github/workflows/ci.yml`, `release.yml`, `.github/dependabot.yml` (new) | CI/CD |
| `README.md`, `BoricuaCoder.API.CoreSetup/README.md`, `CHANGELOG.md`, `samples/SampleApi/*` | Docs |

All library paths below are relative to `BoricuaCoder.API.CoreSetup/`, and test paths to `BoricuaCoder.API.CoreSetup.Tests/`.

---

### Task 1: Branch, test infrastructure and Redis container fixture

**Files:**
- Modify: `BoricuaCoder.API.CoreSetup.Tests/BoricuaCoder.API.CoreSetup.Tests.csproj`
- Create: `Tests/Integration/Redis/RedisContainerFixture.cs`
- Create: `Tests/Integration/Redis/RedisSmokeTests.cs`

**Interfaces:**
- Produces: `RedisContainerFixture : IAsyncLifetime` with `string ConnectionString { get; }`, and `[CollectionDefinition("Redis")] public class RedisCollection : ICollectionFixture<RedisContainerFixture>`. Tasks 5 and 6 use `[Collection("Redis")]` and `[Trait("Category","Integration")]`.

- [ ] **Step 1:** `git switch -c feat/v1.5.0-hardening` from a clean `main`, then run `dotnet test` from the repo root. Expected: 115 passed.
- [ ] **Step 2:** Add `Testcontainers.Redis` (latest stable 4.x) to the test csproj and bump `Microsoft.AspNetCore.Mvc.Testing` to `10.0.15`. Do **not** touch the library csproj.
- [ ] **Step 3:** Write the fixture. It uses `new RedisBuilder().WithImage("redis:7-alpine").Build()`, starts in `InitializeAsync` and disposes in `DisposeAsync`. Then write the smoke test:

```csharp
[Collection("Redis"), Trait("Category", "Integration")]
public class RedisSmokeTests(RedisContainerFixture redis)
{
    [Fact]
    public async Task Container_AcceptsPing()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var latency = await mux.GetDatabase().PingAsync();
        Assert.True(latency < TimeSpan.FromSeconds(5));
    }
}
```

- [ ] **Step 4:** Run `dotnet test --filter "Category=Integration"`. Expected: 1 passed (Docker must be running). Then run `dotnet test --filter "Category!=Integration"`. Expected: 115 passed.
- [ ] **Step 5:** Commit: `test: add Testcontainers Redis fixture for integration tests`.

---

### Task 2: Cache key v2, user scope resolution and argument encoding

Closes #1 (key side), #3, N8 (partially: complex args are hashed) and N11.

**Files:**
- Create: `Caching/CacheUserScope.cs`, `Caching/CacheUserScopeResolver.cs`
- Modify: `Caching/CacheKeyGenerator.cs` (full rewrite of the `Generate` overloads, `BuildArgsSegment` and `BuildKey`)
- Test: `Tests/Caching/CacheKeyGeneratorTests.cs`, and new `Tests/Caching/CacheUserScopeResolverTests.cs`

**Interfaces:**
- Produces:
  - `public enum CacheUserScope { Auto = 0, PerUser = 1, Shared = 2 }`. Each member needs an XML doc. Auto means "per-user when the request is authenticated or the endpoint requires authorization".
  - `internal readonly record struct CacheUserScopeResolution(bool CanCache, string? UserSegment)`
  - `internal static class CacheUserScopeResolver { internal static CacheUserScopeResolution Resolve(HttpContext httpContext, CacheUserScope scope); }`
  - `internal static string? CacheKeyGenerator.Generate(string? customKey, ActionExecutingContext context, string? userSegment)`. Returns `null` when an argument cannot be encoded.
  - `internal static string? CacheKeyGenerator.Generate(string? customKey, EndpointFilterInvocationContext context, string? userSegment)`
  - `internal static string? CacheKeyGenerator.EncodeArg(object? value, JsonSerializerOptions options)`
  - The `prefix` parameter is removed. `RedisCacheService` adds the prefix itself.

**Rules the implementer cannot infer:**
- **User segment:**
  - `Shared` → `null`.
  - `PerUser`, or `Auto` where `User.Identity.IsAuthenticated` is true or the endpoint metadata contains any `IAuthorizeData`:
    - Authenticated with a claim `sub` (checked first) or `ClaimTypes.NameIdentifier`: `"u-" + SHA256(id) hex lowercase [..32]`.
    - Authenticated with neither claim: `CanCache = false`.
    - Not authenticated: `"u-anon"`.
  - `Auto` on an anonymous request to an endpoint with no `IAuthorizeData`: `null`.
- **EncodeArg:**
  - `null` → `"%"`.
  - `string` of length ≤ 64 → `Uri.EscapeDataString(s)`. Longer → `"%h" + SHA256(UTF8 s) hex [..32]`.
  - Primitives, `decimal`, `Guid` and enums → `Convert.ToString(v, InvariantCulture)` then escaped. `DateTime`/`DateTimeOffset` → `ToString("O")` then escaped.
  - Anything else → `"%h" + SHA256(JsonSerializer.SerializeToUtf8Bytes(v, options)) hex [..32]`. If serialization throws → `null`.
- **Skipped arguments** (left out entirely):
  - `CancellationToken`, `HttpContext`, `HttpRequest`, `HttpResponse`, `ClaimsPrincipal`.
  - Any value whose type `IServiceProviderIsService.IsService` reports as a DI service.
- **MVC argument order:** iterate `context.ActionDescriptor.Parameters` in declaration order and look each one up in `ActionArguments`. A missing value counts as `null`.
- **Endpoint id:**
  - MVC: `{controller}::{action}`, with `"Unknown"` as the fallback.
  - Minimal API, first match wins: `IEndpointNameMetadata.EndpointName`; then `(endpoint as RouteEndpoint)?.RoutePattern.RawText`, trimmed of `/` and with `/` replaced by `_`; then `Request.Path` treated the same way; then `"unknown"`. The chosen value goes through `Uri.EscapeDataString`.
- **Key layout** (segments joined by `:`, empty parts omitted):
  - With no `customKey`: `{endpointId}:{args...}:{userSegment}`.
  - With a `customKey`: `{customKey}:{args...}:{endpointId}:{userSegment}`. This keeps `DeleteCascadeAsync("UserInfo:42")` working across `/users/42` and `/users/42/permissions`.
- **Max length 512:** if the key is longer, replace the args part with `"%h" + full 64-char SHA256 hex` of the joined args.
- JSON options for EncodeArg: MVC uses `IOptions<Mvc.JsonOptions>`, Minimal API uses `IOptions<Http.Json.JsonOptions>`, and both fall back to `JsonSerializerOptions.Default`.

- [ ] **Step 1: Write the failing tests.** Replace the existing `BuildKey*`/`BuildArgsSegment*` tests, which test removed internals, with:

```csharp
[Fact] public void EncodeArg_Null_IsPercentToken() => Assert.Equal("%", CacheKeyGenerator.EncodeArg(null, Json));
[Fact] public void EncodeArg_EscapesSeparatorAndGlob() => Assert.Equal("42%3Apermissions%2A", CacheKeyGenerator.EncodeArg("42:permissions*", Json));
[Fact] public void EncodeArg_DistinctDtos_ProduceDistinctHashes()
    => Assert.NotEqual(CacheKeyGenerator.EncodeArg(new Filter("a"), Json), CacheKeyGenerator.EncodeArg(new Filter("b"), Json));
[Fact] public void EncodeArg_Dto_StartsWithHashMarker() => Assert.StartsWith("%h", CacheKeyGenerator.EncodeArg(new Filter("a"), Json));
[Fact] public void Generate_Mvc_NullPositionMatters()   // (null,5) != (5,null)
[Fact] public void Generate_Mvc_CustomKey_AppendsEndpointIdAfterArgs()
    // customKey "UserInfo", Users/GetPermissions, id=42 → "UserInfo:42:Users::GetPermissions"
[Fact] public void Generate_Mvc_SameCustomKeyDifferentActions_DoNotCollide()
[Fact] public void Generate_Mvc_UserSegment_IsLast()      // ends with ":u-abc"
[Fact] public void Generate_Endpoint_SkipsDiServicesAndClaimsPrincipal()
[Fact] public void Generate_Endpoint_UsesRoutePatternWhenNoName()   // "/users/{id}" → "users_%7Bid%7D:42"
[Fact] public void Generate_LongArgs_HashedWithFullSha256()         // key ≤ 512, contains 64 hex chars
```

`CacheUserScopeResolverTests`:

```csharp
[Fact] public void Resolve_Shared_ReturnsNullSegment()
[Fact] public void Resolve_Auto_Anonymous_NoAuthorizeMetadata_ReturnsNullSegment()
[Fact] public void Resolve_Auto_Authenticated_UsesSubHash()        // "u-" + 32 hex, same for same sub, different for different sub
[Fact] public void Resolve_Auto_AnonymousOnAuthorizeEndpoint_ReturnsAnon()   // "u-anon"
[Fact] public void Resolve_AuthenticatedWithoutIdClaim_CannotCache()          // CanCache == false
[Fact] public void Resolve_PrefersSubOverNameIdentifier()
```

- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~CacheKeyGenerator|FullyQualifiedName~CacheUserScopeResolver"`. Expected: compile failure or FAIL.
- [ ] **Step 3:** Implement the enum, the resolver and the generator following the rules above. Update the two `Generate` call sites in `CacheAttribute.cs` so it compiles: pass `userSegment: null` for now, and fall through to `next` when the key is `null`. Task 3 wires the real values.
- [ ] **Step 4:** Run `dotnet test --filter "Category!=Integration"`. Expected: all pass. Fix any `CacheAttributeTests` that asserted old key strings by asserting the new format.
- [ ] **Step 5:** Commit: `fix(cache): v2 cache keys with per-user scope and collision-safe args (#1, #3)`.

---

### Task 3: CacheAttribute — method filter, user scope and response filtering

Closes #1 (wiring), N1 and N5, plus the architect's bug where MVC `ActionResult<T>` responses are never cached.

**Files:**
- Modify: `Caching/CacheAttribute.cs`
- Test: `Tests/Caching/CacheAttributeTests.cs`

**Interfaces:**
- Consumes: `CacheUserScopeResolver.Resolve`, the `CacheKeyGenerator.Generate(…, userSegment)` overloads from Task 2.
- Produces: `public CacheUserScope UserScope { get; set; } = CacheUserScope.Auto;`. It must be `set`, not `init`, so it works as an attribute named argument: `[Cache(60, UserScope = CacheUserScope.Shared)]`.

**Rules:**
- Only `GET` and `HEAD` are cached (`HttpMethods.IsGet`/`IsHead`). Any other method goes straight to `next`.
- Order: method check → resolve user scope (`!CanCache` → `next`) → key (`null` → `next`) → get → miss → `next` → store if eligible.
- **MVC, eligible:** `executed.Exception is null && executed.Result is ObjectResult { Value: not null } r && (r.StatusCode is null or 200)`.
- **Minimal API:** unwrap `INestedHttpResult` repeatedly, then:
  - An `IResult` is eligible only if it is `IValueHttpResult { Value: not null }` and its `IStatusCodeHttpResult.StatusCode` is `null` or `200`. Every other `IResult` is not cached.
  - A non-`IResult` value is eligible unless it is a `string`.
  - Return the original (wrapped) result to the pipeline.
- Wrap serialization in try/catch. On failure, do not cache and return the result normally.
- The hit path is unchanged: 200 with `application/json`.

- [ ] **Step 1: Write failing tests** (reuse the existing `BuildMvcContext`/`BuildNext` helpers, and extend them to accept the HTTP method, a `ClaimsPrincipal` and endpoint metadata):

```csharp
[Fact] public async Task OnActionExecutionAsync_Post_BypassesCache()                 // GetAsync never called, next called
[Fact] public async Task OnActionExecutionAsync_ObjectResultWithNullStatus_IsCached() // ActionResult<T> path
[Fact] public async Task OnActionExecutionAsync_ObjectResult404_IsNotCached()
[Fact] public async Task OnActionExecutionAsync_TwoUsers_UseDifferentKeys()           // capture keys passed to GetAsync; sub "a" vs "b" → different
[Fact] public async Task OnActionExecutionAsync_SharedScope_TwoUsers_SameKey()
[Fact] public async Task OnActionExecutionAsync_AuthenticatedWithoutSub_NeverTouchesCache()
[Fact] public async Task InvokeAsync_ForbidResult_IsNotCached()
[Fact] public async Task InvokeAsync_NotFound_IsNotCached()
[Fact] public async Task InvokeAsync_NestedResultsNotFound_DoesNotCache()            // Results<Ok<Item>, NotFound> holding NotFound
[Fact] public async Task InvokeAsync_NestedResultsOk_CachesOnlyValue()               // stored JSON == serialized Item, no "result" wrapper
[Fact] public async Task InvokeAsync_Created201_IsNotCached()
[Fact] public async Task InvokeAsync_RawString_IsNotCached()
[Fact] public async Task InvokeAsync_UnserializableValue_ReturnsResultWithoutCaching()
```

- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~CacheAttributeTests"`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement the rules. Update the XML `<remarks>` on the class to describe GET/HEAD-only, 200-only and `UserScope`.
- [ ] **Step 4:** Run `dotnet test --filter "Category!=Integration"`. Expected: all pass.
- [ ] **Step 5:** Commit: `fix(cache): cache only successful GET responses and scope per user by default (#1, N1, N5)`.

---

### Task 4: RedisCacheService hardening

Closes #8, N2, N3 and the short-circuit edge cases, and removes silent `catch {}`.

**Files:**
- Modify: `Caching/RedisCacheService.cs`
- Test: `Tests/Caching/RedisCacheServiceTests.cs`

**Interfaces:**
- Produces:
  - Constructor `RedisCacheService(IDistributedCache cache, [FromKeyedServices(RedisSetup.MultiplexerServiceKey)] IConnectionMultiplexer multiplexer, RedisOptions options, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>? jsonOptions = null, ILogger<RedisCacheService>? logger = null)`.
  - `internal static string EscapeGlob(string value)`: prefixes each of `\ * ? [ ]` with `\`.
  - Define `internal const string MultiplexerServiceKey = "BoricuaCoder.API.CoreSetup.Redis";` in `Setup/RedisSetup.cs` now. Task 5 uses it.

**Rules:**
- Server set: `_multiplexer.GetServers().Where(s => s.IsConnected && !s.IsReplica)`. Scan every server, dedupe keys with a `HashSet<string>`.
- `GetKeysAsync(pattern)`: the pattern is `EscapeGlob(PrefixKey) + pattern`. The caller's pattern stays a glob, so document in the XML doc that it must not contain user input.
- `DeleteAllAsync`: if `PrefixKey` is empty, log a Warning (`CoreSetup: DeleteAllAsync skipped because CoreSetup:Redis:PrefixKey is empty; refusing to delete every key in the Redis database.`) and return without scanning. Otherwise use the pattern `EscapeGlob(PrefixKey) + "*"`, delete in batches of 500 with `KeyDeleteAsync(RedisKey[])`, and log the total count at Information.
- `DeleteCascadeAsync(segment)`: if `segment` is empty or whitespace, log a Warning and return. Delete the exact key, then the pattern `EscapeGlob(PrefixKey + segment) + ":*"`, in the same batches.
- Every `catch` logs a Warning with the exception, naming the operation and the relative key (never the connection string), then keeps today's return value.
- Short-circuit uses `task.WaitAsync(TimeSpan.FromSeconds(ShortCircuit), ct)` and catches `TimeoutException`. On timeout, attach `ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)` so a late fault is observed.

- [ ] **Step 1: Write failing tests.** Update the existing mocks from `GetEndPoints`/`GetServer(ep)` to `GetServers()` returning substitutes with `IsConnected = true`, `IsReplica = false`.

```csharp
[Fact] public async Task DeleteAllAsync_EmptyPrefix_DoesNotScanAndLogsWarning()   // server.KeysAsync never received
[Fact] public async Task DeleteAllAsync_ScansEveryPrimaryAndSkipsReplicas()       // 2 primaries + 1 replica → KeysAsync on 2
[Fact] public async Task DeleteCascadeAsync_EscapesGlobInSegment()                // segment "*" → pattern "app::\\*:*"
[Fact] public async Task DeleteCascadeAsync_EmptySegment_DoesNothing()
[Fact] public void EscapeGlob_EscapesAllMetacharacters() => Assert.Equal(@"a\*b\?c\[d\]e\\f", RedisCacheService.EscapeGlob(@"a*b?c[d]e\f"));
[Fact] public async Task GetAsync_SlowCache_ReturnsDefaultWithinShortCircuit()    // TaskCompletionSource never completes, ShortCircuit=1, completes < 2s
[Fact] public async Task SetAsync_SlowCache_ReturnsWithinShortCircuit()
[Fact] public async Task SetAsync_CacheThrows_LogsWarningAndDoesNotThrow()
[Fact] public async Task DeleteAsync_CacheThrows_LogsWarningAndDoesNotThrow()
```

Use `TestLogCollector` or a substitute `ILogger<RedisCacheService>` for the log assertions.

- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~RedisCacheServiceTests"`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement the rules.
- [ ] **Step 4:** Run `dotnet test --filter "Category!=Integration"`. Expected: all pass.
- [ ] **Step 5:** Commit: `fix(redis): guard empty prefix, escape glob patterns, scan all primaries, log failures (#8, N2, N3)`.

---

### Task 5: Lazy keyed Redis connection, startup warnings and real-Redis integration

Closes N7 and #7, and covers `RedisSetup` (0 % → covered).

**Files:**
- Modify: `Setup/RedisSetup.cs`, `Extensions/ServiceCollectionExtensions.cs:34-35`, `Extensions/ApplicationBuilderExtensions.cs` (`LogRedisConfiguration`)
- Create: `Tests/Integration/Redis/RedisCachingIntegrationTests.cs`
- Test: `Tests/Integration/LoggingIntegrationTests.cs`

**Interfaces:**
- Consumes: `RedisContainerFixture` (Task 1), `RedisSetup.MultiplexerServiceKey` and the `RedisCacheService` constructor (Task 4).
- Produces: `internal static bool RedisSetup.IsInsecureRemote(string connectionString)`. It returns true when `ConfigurationOptions.Parse(cs).Ssl` is false and any endpoint is not loopback (a `DnsEndPoint` host other than `localhost`, or an `IPEndPoint` that is not `IPAddress.IsLoopback`).

**Rules:**
- Register the multiplexer with `AddKeyedSingleton<IConnectionMultiplexer>(MultiplexerServiceKey, factory)`. The factory parses the connection string, sets `AbortOnConnectFail = false` and calls `ConnectionMultiplexer.Connect(cfg)`. The connection is lazy: nothing connects during `AddCoreSetup`.
- Add `TryAddSingleton<IConnectionMultiplexer>(sp => sp.GetRequiredKeyedService<IConnectionMultiplexer>(MultiplexerServiceKey))` for backward compatibility. A consumer's own registration wins.
- Point `AddStackExchangeRedisCache` at the keyed multiplexer through `services.AddOptions<RedisCacheOptions>().Configure<IServiceProvider>(…ConnectionMultiplexerFactory…)`.
- `AddCoreSetup` calls `AddRedisCache` only when `Enabled && !string.IsNullOrWhiteSpace(ConnectionString)`. With an empty string, the validator reports the friendly error at start.
- `LogRedisConfiguration` adds two Warnings:
  - Empty `PrefixKey`: `CoreSetup: Redis PrefixKey is empty. Cache keys share the database namespace with other apps and DeleteAllAsync is disabled. Set CoreSetup:Redis:PrefixKey.`
  - `IsInsecureRemote`: `CoreSetup: Redis connection to a non-loopback host does not use TLS (ssl=true). Credentials and cached responses travel in plain text.` Never log the connection string itself.

- [ ] **Step 1: Write failing tests** (`[Collection("Redis")]`, `[Trait("Category","Integration")]`). The test host uses `IntegrationTestBase` config plus `CoreSetup:Redis:Enabled=true`, `ConnectionString=redis.ConnectionString` and `PrefixKey=it::{Guid}::`. Map `GET /items/{id}` with `.AddEndpointFilter(new CacheAttribute(60))` and a hit counter.

```csharp
[Fact] public async Task CacheAttribute_SecondCall_IsServedFromRedis()          // counter == 1 after 2 calls; key exists in Redis under prefix
[Fact] public async Task CacheAttribute_EntryExpiresAfterTtl()                   // CacheAttribute(1); wait 1.5 s; counter == 2
[Fact] public async Task DeleteAllAsync_RemovesOnlyPrefixedKeys()                // foreign key "other:x" survives
[Fact] public async Task DeleteCascadeAsync_GlobSegment_DeletesOnlyLiteral()      // keys "U:1:a", "U:2:a"; DeleteCascadeAsync("*") leaves both
[Fact] public async Task App_StartsAndServes_WhenRedisUnreachable()              // ConnectionString "localhost:1,connectTimeout=200"; GET returns 200 twice, counter == 2
[Fact] public async Task ConsumerMultiplexer_RegisteredAfterCoreSetup_IsNotReplacedAndLibraryUsesKeyed()
```

`LoggingIntegrationTests`, unit level with no container:

```csharp
[Fact] public void UseCoreSetup_EmptyPrefixKey_LogsWarning()
[Fact] public void UseCoreSetup_RemoteRedisWithoutSsl_LogsWarning()    // "redis.example.com:6379"
[Fact] public void UseCoreSetup_LocalhostRedis_NoTlsWarning()
```

For the warning tests the connection is lazy, so a host that doesn't exist is fine.

- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~Redis|FullyQualifiedName~LoggingIntegration"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `dotnet test` (all, including Integration). Expected: all pass. Check coverage with `--collect:"XPlat Code Coverage"`: `RedisSetup` line coverage must be above 0.
- [ ] **Step 5:** Commit: `fix(redis): lazy keyed connection, TLS and prefix startup warnings, real Redis tests (N7, #7)`.

---

### Task 6: Swagger RoutePrefix fix and production warning

Closes the architect's RoutePrefix bug and #2 (warning only).

**Files:**
- Modify: `Extensions/ApplicationBuilderExtensions.cs:36-53`
- Test: `Tests/Integration/CoreSetupIntegrationTests.cs`, `Tests/Integration/LoggingIntegrationTests.cs`

**Interfaces:**
- Produces: `internal static string ApplicationBuilderExtensions.BuildSwaggerJsonPath(string routePrefix, string version)`. It returns `"/{version}/swagger.json"` when the prefix is empty or whitespace, and `"/{prefix.Trim('/')}/{version}/swagger.json"` otherwise.

**Rules:**
- `app.UseSwagger(c => c.RouteTemplate = BuildSwaggerJsonPath(prefix, "{documentName}").TrimStart('/'))`.
- `c.SwaggerEndpoint(BuildSwaggerJsonPath(prefix, version), …)`.
- When Swagger is enabled and `!app.Environment.IsDevelopment()`, log a Warning: `CoreSetup: Swagger UI is enabled in the '{Environment}' environment and exposes the full API surface. It will be disabled by default outside Development in 2.0.0. Set CoreSetup:Swagger:Enabled explicitly.`

- [ ] **Step 1: Write failing tests:**

```csharp
[Fact] public void BuildSwaggerJsonPath_EmptyPrefix() => Assert.Equal("/v1/swagger.json", ApplicationBuilderExtensions.BuildSwaggerJsonPath("", "v1"));
[Fact] public void BuildSwaggerJsonPath_CustomPrefix() => Assert.Equal("/api-docs/v1/swagger.json", ApplicationBuilderExtensions.BuildSwaggerJsonPath("api-docs", "v1"));
[Fact] public async Task SwaggerJson_CustomRoutePrefix_IsServedUnderPrefix()   // RoutePrefix "api-docs": GET /api-docs/v1/swagger.json → 200; /api-docs/index.html → 200
[Fact] public void UseCoreSetup_SwaggerInProduction_LogsWarning()               // builder.Environment.EnvironmentName = "Production"
[Fact] public void UseCoreSetup_SwaggerInDevelopment_NoProductionWarning()
```

- [ ] **Step 2:** Run the tests. Expected: FAIL. The custom-prefix JSON test returns 404 today.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `dotnet test --filter "Category!=Integration"`. Expected: all pass, and the existing `/swagger` tests still pass.
- [ ] **Step 5:** Commit: `fix(swagger): serve JSON under custom RoutePrefix and warn outside Development (#2)`.

---

### Task 7: JWT hardening

Closes #5 (opt-in), #6 and N4 (warnings), and stops the setup from replacing the default token validation parameters.

**Files:**
- Modify: `Options/JwtOptions.cs`, `Setup/JwtAuthenticationSetup.cs:22-39`, `Extensions/ApplicationBuilderExtensions.cs` (`LogJwtConfiguration`)
- Test: `Tests/Integration/TokenValidationIntegrationTests.cs`, `Tests/Integration/LoggingIntegrationTests.cs`, `Tests/Options/JwtOptionsTests.cs`

**Interfaces:**
- Produces: `public string[] ValidAlgorithms { get; init; } = [];` on `JwtOptions`, with an XML doc and the example `["RS256","PS256","ES256"]`. An empty array keeps the handler default.

**Rules:**
- No more `new TokenValidationParameters`. Set only the properties that were provided on the existing `jwt.TokenValidationParameters`: `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime` and `ClockSkew`.
- When `ValidAlgorithms.Length > 0`, set `jwt.TokenValidationParameters.ValidAlgorithms`, whether or not other overrides are present.
- New Warnings in `LogJwtConfiguration`, one per condition:
  - Authority empty: `CoreSetup: Jwt:Authority is not set; every bearer token will be rejected.`
  - Audience equals `account` (OrdinalIgnoreCase): `CoreSetup: Jwt:Audience is 'account'. Keycloak adds this audience to tokens of most clients in the realm, so tokens issued for other applications will be accepted. Configure an audience mapper with this API's client id.`
  - Each of `ValidateIssuer`, `ValidateAudience` or `ValidateLifetime` set to `false`: `CoreSetup: Jwt:TokenValidation:{Name} is disabled. Do not use this setting in production.`

- [ ] **Step 1: Write failing tests:**

```csharp
[Fact] public async Task ValidAlgorithms_WithoutOtherOverrides_AreApplied()        // IOptionsMonitor<JwtBearerOptions>.Get("Bearer").TokenValidationParameters.ValidAlgorithms == ["RS256"]
[Fact] public async Task ValidateIssuerOverride_KeepsValidAlgorithms()
[Fact] public async Task ClockSkewOverride_DoesNotReplaceParametersInstance()       // other non-default property set by PostConfigure survives
[Fact] public void UseCoreSetup_AudienceAccount_LogsWarning()
[Fact] public void UseCoreSetup_EmptyAuthority_LogsWarning()
[Fact] public void UseCoreSetup_ValidateAudienceFalse_LogsWarning()
[Fact] public void JwtOptions_BindsValidAlgorithmsArray()
```

Keep the existing 5 `TokenValidationIntegrationTests` green.

- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `dotnet test --filter "Category!=Integration"`. Expected: all pass.
- [ ] **Step 5:** Commit: `feat(jwt): opt-in ValidAlgorithms, keep default validation parameters, misconfiguration warnings (#5, #6, N4)`.

---

### Task 8: NuGet package quality

**Files:**
- Modify: `BoricuaCoder.API.CoreSetup.csproj`

**Rules:**
- Confirm the repository URL with `git remote get-url origin`. The expected value is `https://github.com/nemesis312/BoricuaCoder.API.CoreSetup`.
- Add these properties:
  - `PackageProjectUrl` and `RepositoryUrl` (that URL).
  - `PublishRepositoryUrl=true`, `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`, `EmbedUntrackedSources=true`.
  - `ContinuousIntegrationBuild=true` only when `'$(GITHUB_ACTIONS)'=='true'`.
  - `EnablePackageValidation=true`, `PackageValidationBaselineVersion=1.4.2`.
  - `NuGetAudit=true`, `NuGetAuditMode=all`.
  - `PackageReleaseNotes` = `https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/blob/main/CHANGELOG.md`.
- Change `<Version>` from `1.0.1` to `0.0.0-local` and keep the CI comment.
- Do not add the `Microsoft.SourceLink.GitHub` package; the .NET 10 SDK includes SourceLink. Do not add an icon unless the owner supplies one.
- Update the `Description` to mention per-user response caching.

- [ ] **Step 1:** Run `dotnet pack BoricuaCoder.API.CoreSetup/BoricuaCoder.API.CoreSetup.csproj -c Release -o <scratchpad>/pkg`. Expected: succeeds with 0 warnings, and produces both `BoricuaCoder.API.CoreSetup.0.0.0-local.nupkg` and `.snupkg`.
- [ ] **Step 2:** `unzip -p <nupkg> BoricuaCoder.API.CoreSetup.nuspec`. Expected: `<repository type="git" url="https://github.com/nemesis312/…" commit="…"/>`, `<projectUrl>`, and dependency group net10.0 with the unchanged floors 10.0.2 / 10.0.2 / 1.6.23 / 7.3.2.
- [ ] **Step 3:** Package validation passing in Step 1 confirms that Tasks 2–7 changed the public API by addition only. If it reports CP0002 or a similar error, stop and report the exact member. Do not suppress it.
- [ ] **Step 4:** Commit: `chore(package): SourceLink metadata, symbols, package validation against 1.4.2`.

---

### Task 9: CI/CD hardening

**Files:**
- Modify: `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- Create: `.github/dependabot.yml`

**Rules:**
- **ci.yml:**
  - Add `workflow_call:` to `on`.
  - Top-level `permissions: contents: read`.
  - `concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: ${{ github.event_name == 'pull_request' }} }`.
  - Pin every action to a full commit SHA, with a `# vX.Y.Z` comment. Resolve SHAs with `gh api repos/<owner>/<repo>/git/ref/tags/<tag> --jq .object.sha` (dereference annotated tags).
  - Pin `dotnet-reportgenerator-globaltool` with `--version` to the current latest.
  - Integration tests run in CI; ubuntu-latest has Docker.
- **release.yml:**
  - Job `ci: uses: ./.github/workflows/ci.yml` with `secrets: inherit`.
  - Job `release` with `needs: ci`, `permissions: contents: write` (plus `id-token: write` only if the owner picks OIDC, see Open Decisions).
  - Checkout with `fetch-depth: 0`, then fail unless `git merge-base --is-ancestor "$GITHUB_SHA" origin/main`.
  - Extract the section `## [${VERSION}]` from `CHANGELOG.md` into `release-notes.md` with awk, and fail if it is empty.
  - Build, pack and push with the API key passed through `env: NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}` and referenced as `"$NUGET_API_KEY"` (not interpolated). `dotnet nuget push ./nupkg/*.nupkg` pushes the matching `.snupkg` automatically.
  - The GitHub Release uses `body_path: release-notes.md` and `files: ./nupkg/*.nupkg ./nupkg/*.snupkg` (newline-separated).
  - Pin actions by SHA here too.
- **dependabot.yml:**
  - Ecosystems `nuget` (directory `/`) and `github-actions`, both weekly.
  - For nuget, group `microsoft` = `Microsoft.*`.
  - Ignore `Swashbuckle.AspNetCore` and `Microsoft.OpenApi` version-update:semver-major, and all `Microsoft.*` ≥ 11.

- [ ] **Step 1:** Validate syntax with `actionlint` if installed (`brew list actionlint`). Otherwise run `python3 -c "import yaml,sys;[yaml.safe_load(open(f)) for f in sys.argv[1:]]" .github/workflows/*.yml .github/dependabot.yml`. Expected: no errors.
- [ ] **Step 2:** Run the awk extraction locally against `CHANGELOG.md` with `VERSION=1.4.2`. Expected: it prints the 1.4.2 section only.
- [ ] **Step 3:** Commit: `ci: least-privilege, SHA-pinned actions, gated release with changelog notes, dependabot`.

---

### Task 10: Documentation, sample and consumer verification

**Files:**
- Modify: `README.md`, `BoricuaCoder.API.CoreSetup/README.md` (the packed copy, kept in sync with the same content), `CHANGELOG.md`, `samples/SampleApi/Program.cs`, `samples/SampleApi/appsettings*.json`

**Rules:**
- **README, `[Cache]` section:**
  - Only GET/HEAD and 200 responses with a value are cached.
  - `UserScope`, with a table for Auto/PerUser/Shared and the rule that `Shared` is only for data that is identical for every caller.
  - The new key layout, and the corrected `UserInfo` example: keys `UserInfo:42:Users::Get:u-…` and `UserInfo:42:Users::GetPermissions:u-…`, both cleared by `DeleteCascadeAsync("UserInfo:42")`.
  - `PrefixKey` is required for `DeleteAllAsync`.
  - Remove the `FlushAllAsync` example.
- **README, JWT section:**
  - Replace `Audience: "account"` with a dedicated audience mapper explanation.
  - Document `ValidAlgorithms`.
  - Add an "Extending the defaults" subsection showing `services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, …)`, `services.Configure<SwaggerGenOptions>(…)` and `services.Configure<SwaggerUIOptions>(…)`.
- **Troubleshooting:** add the new warnings and what to do about each.
- **CHANGELOG:** add `## [1.5.0] - <date of release>` with these sections:
  - `### Security`: #1, N1, #3, #8, N2, N5, #7, N4.
  - `### ⚠ Behavior changes`: per-user cache scope by default; cache key format changed, so the cache starts cold after deploy; only GET/HEAD with 200 are cached; `DeleteAllAsync` is a no-op with an empty prefix; Redis connects lazily and no longer crashes startup.
  - `### Added`: `CacheUserScope`, `ValidAlgorithms`, warnings, SourceLink/snupkg.
  - `### Fixed`: RoutePrefix, `ActionResult<T>` caching.
- **Sample:**
  - Audience becomes the sample client id instead of `account`.
  - Any `[Cache]`/`CacheAttribute` usage matches the README.
  - Add one `Shared` example on a public endpoint.

- [ ] **Step 1:** Run `dotnet build samples/SampleApi -c Release`. Expected: 0 errors, 0 warnings.
- [ ] **Step 2: Consumer check.** Run `dotnet build -c Release` in `/Users/lestherruiz/Documents/development/bc-nugets/TestAPI-CoreSetup`, which uses a ProjectReference. Expected: succeeds with no NU1605 and no new warnings. Do **not** modify or commit anything in that repo; report its result only.
- [ ] **Step 3:** Run `dotnet test` (all) plus coverage. Expected: everything passes. Report the totals and the branch coverage, compared with the 81.1 % baseline.
- [ ] **Step 4:** Commit: `docs: document v1.5.0 cache scoping, JWT hardening and behavior changes`.

---

## Decisions (resolved by owner 2026-11-21)

1. **`DeleteAllAsync` with an empty `PrefixKey`:** no-op plus a Warning in 1.5.0, as implemented in Task 4. Validation failure at startup is deferred to 2.0.
2. **NuGet publishing auth: Trusted Publishing (OIDC).** In Task 9:
   - The `release` job gets `permissions: id-token: write` and a step `NuGet/login@v1` (pinned by SHA) with `user: ${{ vars.NUGET_USER }}`.
   - The push uses `--api-key "${{ steps.login.outputs.NUGET_API_KEY }}"`, passed through `env`.
   - Remove every use of `secrets.NUGET_API_KEY`.
   - Owner prerequisites, done before the first tag: create the trusted-publishing policy on nuget.org (repo `nemesis312/BoricuaCoder.API.CoreSetup`, workflow `release.yml`) and the repo variable `NUGET_USER`, set to the nuget.org profile name. List both in the task report.
3. **Package icon (Task 8):** skipped unless the owner provides a 128×128 PNG.
4. **Execution:** subagent-driven.
