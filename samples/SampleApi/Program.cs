using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Extensions;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// CoreSetup:Jwt:Audience is "sample-api", the client id of this API in Keycloak. Tokens carry it
// through an audience mapper on a client scope assigned to the Swagger client (see the README,
// "Keycloak Configuration", step 4). Do not use "account": Keycloak adds it to the tokens of most
// clients in the realm, so it would accept tokens issued for other applications.
builder.Services.AddCoreSetup(builder.Configuration);

var app = builder.Build();

app.UseCoreSetup();

// Public endpoint — no authentication required
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .AllowAnonymous()
   .WithName("HealthCheck")
   .WithTags("System");

// Protected endpoint — requires a valid JWT
app.MapGet("/me", (HttpContext context) =>
{
    var claims = context.User.Claims
        .Select(c => new { c.Type, c.Value });

    return Results.Ok(new { claims });
})
.RequireAuthorization()
.WithName("GetCurrentUser")
.WithTags("Identity");

// Cached per user (the endpoint requires authorization) — key: "sample:api::GetProducts%i…:%u…"
// Re-hydrates from Redis on subsequent requests within the 300 s TTL.
app.MapGet("/products", [Authorize] () =>
{
    var products = new[]
    {
        new { Id = 1, Name = "Widget A", Price = 9.99 },
        new { Id = 2, Name = "Widget B", Price = 19.99 },
        new { Id = 3, Name = "Widget C", Price = 29.99 }
    };

    return Results.Ok(products);
})
.AddEndpointFilter(new CacheAttribute(300))
.WithName("GetProducts")
.WithTags("Products");

// Public data, identical for every caller: one shared entry — key: "sample:api::GetCatalog%i…:%s"
// Use Shared only when the response never depends on who is calling.
app.MapGet("/catalog", () =>
{
    return Results.Ok(new[] { "Widget A", "Widget B", "Widget C" });
})
.AllowAnonymous()
.AddEndpointFilter(new CacheAttribute(300) { UserScope = CacheUserScope.Shared })
.WithName("GetCatalog")
.WithTags("Products");

// Cached per user with a custom key — key: "sample:api::UserInfo:{userId}:GetUser%i…:%u…"
app.MapGet("/users/{userId}", [Authorize] (int userId) =>
{
    return Results.Ok(new { userId, name = $"User {userId}", email = $"user{userId}@example.com" });
})
.AddEndpointFilter(new CacheAttribute(60, "UserInfo"))
.WithName("GetUser")
.WithTags("Users");

// Same custom key — key: "sample:api::UserInfo:{userId}:GetUserPermissions%i…:%u…"
// ICacheService.DeleteCascadeAsync("UserInfo:{userId}") removes this entry and the one above, for every user.
app.MapGet("/users/{userId}/permissions", [Authorize] (int userId) =>
{
    return Results.Ok(new { userId, permissions = new[] { "read", "write" } });
})
.AddEndpointFilter(new CacheAttribute(120, "UserInfo"))
.WithName("GetUserPermissions")
.WithTags("Users");

app.Run();
