using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Extensions;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

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

// Cached endpoint — auto key: "sample:api::GetProducts"
// Re-hydrates from Redis on subsequent requests within DefaultTTL (60 s in dev).
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

// Cached endpoint with custom key — key: "sample:api::UserInfo:{userId}"
// All /users/{id}/* variants can be cascade-deleted via ICacheService.DeleteCascadeAsync("UserInfo:{id}")
app.MapGet("/users/{userId}", [Authorize] (int userId) =>
{
    return Results.Ok(new { userId, name = $"User {userId}", email = $"user{userId}@example.com" });
})
.AddEndpointFilter(new CacheAttribute(60, "UserInfo"))
.WithName("GetUser")
.WithTags("Users");

// Cached endpoint with custom key and parameter — key: "sample:api::UserInfo:{userId}:permissions"
app.MapGet("/users/{userId}/permissions", [Authorize] (int userId) =>
{
    return Results.Ok(new { userId, permissions = new[] { "read", "write" } });
})
.AddEndpointFilter(new CacheAttribute(120, "UserInfo"))
.WithName("GetUserPermissions")
.WithTags("Users");

app.Run();
