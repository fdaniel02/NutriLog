# NutriLog

[![CI](https://github.com/fdaniel02/NutriLog/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/fdaniel02/NutriLog/actions/workflows/ci.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=fdaniel02_NutriLog&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=fdaniel02_NutriLog)

Food tracker with macros, micros and targets that follow my runs.

## Project layout

```
NutriLog.slnx
src/NutriLog/                the app (Blazor Server)
tests/NutriLog.Tests/        tests (xUnit v3)
.github/workflows/ci.yml     build and test on every pull request and push to main; on main, publish the image
.github/dependabot.yml       weekly update pull requests for NuGet packages, the .NET SDK and GitHub Actions
```

NuGet versions are locked in `packages.lock.json` (enabled in `Directory.Build.props`); CI restores in locked mode.
After changing a package, commit the updated lock file too.

The lock file also holds a package the SDK adds implicitly, whose version follows the SDK. So when you change the
SDK version in `global.json`, regenerate the lock files in the same pull request:

```powershell
dotnet restore --force-evaluate
```

The Docker image builds with the newest .NET 10 SDK and runtime (`sdk:10.0` and `aspnet:10.0`) and restores without
locked mode, so it picks up .NET patches on every build without depending on `global.json`.

## Build and test

```powershell
dotnet build
dotnet run --project src/NutriLog
dotnet test --coverlet
```

Tests run on Microsoft Testing Platform (set in `global.json`). `--coverlet` writes an OpenCover report
to `TestResults/coverage.opencover.*.xml`; the settings are in `tests/NutriLog.Tests/testconfig.json`.

## CI

`Build and test` builds, runs the tests and sends the analysis to SonarCloud (project `fdaniel02_NutriLog`). The
`main` ruleset requires it by name. `Docker image` builds the image and scans it with Trivy; on `main`,
`Publish image` pushes `ghcr.io/fdaniel02/nutrilog` with the tags `sha-<commit>` (for Argo CD Image Updater) and
`latest`.

The SonarCloud project and its token (the secret `SONAR_TOKEN`, for Actions and Dependabot) are managed in
[homelab-tofu](https://github.com/fdaniel02/homelab-tofu) (`sonarcloud.tf`). Without the secret, the analysis is
skipped.
