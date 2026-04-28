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

// Protected endpoint — example resource
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
.WithName("GetProducts")
.WithTags("Products");

app.Run();
