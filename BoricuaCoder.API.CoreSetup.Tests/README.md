# BoricuaCoder.API.CoreSetup.Tests

Unit tests for the BoricuaCoder.API.CoreSetup library.

## Running Tests

```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --verbosity normal
```

## Code Coverage

### Generate Coverage Report

```bash
# Run tests with coverage collection
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML report (requires ReportGenerator)
dotnet tool install -g dotnet-reportgenerator-globaltool

reportgenerator \
  -reports:"./TestResults/**/coverage.cobertura.xml" \
  -targetdir:"./coverage-report" \
  -reporttypes:Html

# Open the report
open ./coverage-report/index.html
```

### One-liner

```bash
dotnet test --collect:"XPlat Code Coverage" && \
reportgenerator -reports:"./TestResults/**/coverage.cobertura.xml" -targetdir:"./coverage-report" -reporttypes:Html && \
open ./coverage-report/index.html
```

## Test Structure

```
BoricuaCoder.API.CoreSetup.Tests/
├── Options/
│   ├── JwtOptionsTests.cs
│   ├── SwaggerOptionsTests.cs
│   └── CoreSetupOptionsTests.cs
└── Extensions/
    └── ServiceCollectionExtensionsTests.cs
```

## CI/CD

Tests run automatically on every push and pull request via GitHub Actions. The workflow:

1. Builds the solution
2. Runs all tests with code coverage
3. Generates an HTML coverage report
4. Uploads the report as an artifact
5. Shows coverage summary in the PR

See [.github/workflows/ci.yml](../.github/workflows/ci.yml) for details.
