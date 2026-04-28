# BoricuaCoder.API.CoreSetup

[![CI](https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/actions/workflows/ci.yml/badge.svg)](https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/actions/workflows/ci.yml)
[![codecov](https://codecov.io/github/nemesis312/BoricuaCoder.API.CoreSetup/branch/main/graph/badge.svg?token=F8OSYQGVCA)](https://codecov.io/github/nemesis312/BoricuaCoder.API.CoreSetup)
[![Changelog](https://img.shields.io/badge/changelog-CHANGELOG.md-blue)](CHANGELOG.md)

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

| Property               | Type   | Default | Description                                                       |
| ---------------------- | ------ | ------- | ----------------------------------------------------------------- |
| `Authority`            | string | `""`    | The URL of your identity provider (e.g., Keycloak realm URL)      |
| `Audience`             | string | `""`    | The expected audience claim in the JWT token                      |
| `RequireHttpsMetadata` | bool   | `true`  | Set to `false` for local development with HTTP identity providers |
| `TokenValidation`      | object |         | Fine-grained token validation overrides (see below)              |

### TokenValidationOptions

All properties are optional. Set only the ones you need to override — `null` keeps the JWT Bearer handler's default behavior.

| Property            | Type  | Default | Description                                                                          |
| ------------------- | ----- | ------- | ------------------------------------------------------------------------------------ |
| `ValidateIssuer`    | bool? | `null`  | Override issuer validation. Set to `false` when using multiple issuers              |
| `ValidateAudience`  | bool? | `null`  | Override audience validation                                                         |
| `ValidateLifetime`  | bool? | `null`  | Override lifetime/expiration validation                                              |
| `ClockSkewSeconds`  | int?  | `null`  | Override clock skew tolerance. Default is 300 (5 min). Set to `0` for exact expiry  |

### SwaggerOptions

| Property      | Type   | Default     | Description                                     |
| ------------- | ------ | ----------- | ----------------------------------------------- |
| `Enabled`     | bool   | `true`      | Enable or disable Swagger UI                    |
| `Title`       | string | `"API"`     | The title displayed in Swagger UI               |
| `Version`     | string | `"v1"`      | API version for the Swagger document            |
| `RoutePrefix` | string | `"swagger"` | URL prefix for Swagger UI (e.g., `/swagger`)    |
| `OAuth`       | object |             | OAuth2 configuration for Swagger authentication |

### SwaggerOAuthOptions

| Property           | Type   | Default                        | Description                                            |
| ------------------ | ------ | ------------------------------ | ------------------------------------------------------ |
| `AuthorizationUrl` | string | `""`                           | OAuth2 authorization endpoint (Keycloak auth URL)      |
| `TokenUrl`         | string | `""`                           | OAuth2 token endpoint (Keycloak token URL)             |
| `ClientId`         | string | `""`                           | OAuth2 client ID registered in Keycloak                |
| `Scopes`           | object | `{"openid": "OpenID Connect"}` | Available scopes (key: scope name, value: description) |

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

## Troubleshooting

### Swagger UI returns 404

**Symptoms:** Navigating to `/swagger` returns a 404 Not Found.

**Causes and fixes:**

1. `Swagger:Enabled` is `false` — check your `appsettings.json` (or the environment-specific override that is active).
2. The `RoutePrefix` is not what you expect — the default is `swagger`, so the URL is `/swagger`. If you set `RoutePrefix: "api-docs"`, the URL becomes `/api-docs`.
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
2. **`Audience` mismatch** — The `aud` claim in the token must match `Audience`. Common Keycloak values are `account`, the client ID itself, or a custom audience you configured.
3. **`RequireHttpsMetadata: true` in local dev** — If your local Keycloak runs on HTTP, set this to `false` in `appsettings.Development.json`.

```json
"CoreSetup": {
  "Jwt": {
    "Authority": "https://keycloak.example.com/realms/your-realm",
    "Audience": "account",
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
- A protected `/products` endpoint as a realistic resource example
- Full `appsettings.json` and `appsettings.Development.json` with all available options

## Requirements

- .NET 10.0 or later
- ASP.NET Core application
- Keycloak or any OpenID Connect provider

## License

MIT
