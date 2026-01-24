# BoricuaCoder.API.CoreSetup

[![CI](https://github.com/YOUR_USERNAME/BoricuaCoder.API.CoreSetup/actions/workflows/ci.yml/badge.svg)](https://github.com/YOUR_USERNAME/BoricuaCoder.API.CoreSetup/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/YOUR_USERNAME/BoricuaCoder.API.CoreSetup/graph/badge.svg)](https://codecov.io/gh/YOUR_USERNAME/BoricuaCoder.API.CoreSetup)

A lightweight library that simplifies ASP.NET Core API setup with pre-configured **JWT Bearer authentication** and **Swagger/OpenAPI documentation** with OAuth2 support for Keycloak (or any OpenID Connect provider).

## Purpose

This package eliminates boilerplate code when setting up new ASP.NET Core APIs. Instead of manually configuring JWT authentication and Swagger with OAuth security schemes, you can configure everything via `appsettings.json` with just **two lines of code**.

## Features

- **JWT Bearer Authentication** - Pre-configured with Authority, Audience, and HTTPS metadata settings
- **Swagger/OpenAPI with OAuth2** - Auto-configured with Authorization Code flow + PKCE
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
      "Audience": "account",
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
    }
  }
}
```

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

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Authority` | string | `""` | The URL of your identity provider (e.g., Keycloak realm URL) |
| `Audience` | string | `""` | The expected audience claim in the JWT token |
| `RequireHttpsMetadata` | bool | `true` | Set to `false` for local development with HTTP identity providers |

### SwaggerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | bool | `true` | Enable or disable Swagger UI |
| `Title` | string | `"API"` | The title displayed in Swagger UI |
| `Version` | string | `"v1"` | API version for the Swagger document |
| `RoutePrefix` | string | `"swagger"` | URL prefix for Swagger UI (e.g., `/swagger`) |
| `OAuth` | object | | OAuth2 configuration for Swagger authentication |

### SwaggerOAuthOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AuthorizationUrl` | string | `""` | OAuth2 authorization endpoint (Keycloak auth URL) |
| `TokenUrl` | string | `""` | OAuth2 token endpoint (Keycloak token URL) |
| `ClientId` | string | `""` | OAuth2 client ID registered in Keycloak |
| `Scopes` | object | `{"openid": "OpenID Connect"}` | Available scopes (key: scope name, value: description) |

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

| Setting | Value |
|---------|-------|
| **Root URL** | `https://your-api.com` |
| **Valid redirect URIs** | `https://your-api.com/swagger/oauth2-redirect.html` |
| **Valid post logout redirect URIs** | `https://your-api.com/*` |
| **Web origins** | `https://your-api.com` |

### 3. Get the URLs

Your Keycloak URLs follow this pattern:
- **Authorization URL**: `https://{keycloak-host}/realms/{realm}/protocol/openid-connect/auth`
- **Token URL**: `https://{keycloak-host}/realms/{realm}/protocol/openid-connect/token`
- **Authority (for JWT)**: `https://{keycloak-host}/realms/{realm}`

## Environment-Specific Configuration

### appsettings.Development.json

```json
{
  "CoreSetup": {
    "Jwt": {
      "Authority": "http://localhost:8080/realms/dev-realm",
      "Audience": "account",
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
    }
  }
}
```

### appsettings.Production.json

```json
{
  "CoreSetup": {
    "Jwt": {
      "Authority": "https://auth.mycompany.com/realms/prod-realm",
      "Audience": "account",
      "RequireHttpsMetadata": true
    },
    "Swagger": {
      "Enabled": false
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

- OpenAPI document generation
- OAuth2 Authorization Code flow with PKCE
- Configurable scopes for user selection
- Swagger UI with OAuth client configuration

## Requirements

- .NET 10.0 or later
- ASP.NET Core application
- Keycloak or any OpenID Connect provider

## License

MIT
