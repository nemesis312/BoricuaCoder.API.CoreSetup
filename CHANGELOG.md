# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/nemesis312/BoricuaCoder.API.CoreSetup/releases/tag/v1.0.0
