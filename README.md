# NutriLog

[![CI](https://github.com/fdaniel02/NutriLog/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/fdaniel02/NutriLog/actions/workflows/ci.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=fdaniel02_NutriLog&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=fdaniel02_NutriLog)

Food tracker with macros, micros and targets that follow my runs.

## Project layout

```
NutriLog.slnx
src/NutriLog/                the app (Blazor Server)
tests/NutriLog.Tests/        unit, component (bUnit) and PostgreSQL tests (xUnit v3)
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

Some tests run against a real PostgreSQL in a container (Testcontainers, image `postgres:18`), so Docker must be running.

## Data

PostgreSQL, through EF Core. The app applies its migrations at startup. After changing the model, add a migration;
a test fails if the model has changes without one:

```powershell
dotnet ef migrations add <Name> --project src/NutriLog
```

- **Nutrients**: a fixed list (energy, macros, fatty acids, vitamins, minerals), each with its unit, seeded by the
  migrations (`Data/Nutrients.cs`). The ids never change; adding a nutrient takes a migration.
- **Foods**: English name and Dutch name, source (`Nevo`, `UsdaFoundation`, `UsdaSrLegacy`, `Off`, `Own`) and the id
  there, barcode, and nutrient values per 100 g (or 100 ml). A nutrient without a value has no row: missing, never 0.
- **Portions** per food, e.g. "slice" = 35 g. Imported ones are marked; portions added by hand are never touched by an
  import.
- **Recipes**: foods with grams, and the number of servings it makes.
- **Diary entries**: date, time eaten, and either a food in grams or portions, or a recipe in servings. Check constraints
  keep these combinations valid. A food or recipe that's in a recipe or the diary can't be deleted, so past days keep
  their numbers. A recipe entry stores the recipe's nutrients per serving as they were when it was logged
  (`DiaryEntryNutrients`), so changing the recipe later only affects new entries, copies included.
- **Data imports**: one row per import run; the latest per source gives the version in the source reference at the
  bottom of every page.

## Food data

Foods come from two food composition databases, imported with the app itself. It applies the migrations, imports, and
exits without starting the web server:

```powershell
dotnet run --project src/NutriLog -- import nevo C:\Temp\NEVO\NEVO2025_v9.0.csv
dotnet run --project src/NutriLog -- import usda <folder>\FoodData_Central_foundation_food_json_2026-10-29.zip
dotnet run --project src/NutriLog -- import usda <folder>\FoodData_Central_sr_legacy_food_json_2018-04.zip
```

- **NEVO** (RIVM): the main source, for foods as sold in the Netherlands. Request the dataset at
  [rivm.nl](https://www.rivm.nl/en/dutch-food-composition-database/use-of-nevo-online/request-dataset) and import the
  main file, `NEVO<year>_v<version>.csv`. Its conditions of use don't allow passing the data on, so the file never goes
  into git or the image, and the tests use made-up rows in its format. NEVO's values are used unchanged; the app shows
  the reference the conditions require.
- **USDA FoodData Central** (public domain): Foundation Foods and SR Legacy, as the JSON zips from
  [fdc.nal.usda.gov](https://fdc.nal.usda.gov/download-datasets), under USDA's file names (the release date is read
  from them). Energy falls back to the Atwater factors where Foundation Foods has no plain energy value; folate is in
  dietary folate equivalents where available, like NEVO's. Only portions that count something are imported ("medium",
  "slice", "egg"), not measures like cups, spoons or ounces.

Running an import again (for a new release) updates foods by their id in the source, so diary entries keep pointing at
the same food. Foods missing from the new release are removed, unless a recipe or the diary uses them. Each run logs
its counts (event id 11) and is recorded in `DataImports`.

Packaged products come from **Open Food Facts** (ODbL), one at a time: scanning a barcode (`/scan`, with the browser's
`BarcodeDetector`, or typing it) looks in the database first and only asks the
[API](https://openfoodfacts.github.io/openfoodfacts-server/api/) for an unknown barcode. The product is saved as a food
(source `Off`), so the next scan works without the API, and it can be corrected when the label differs; own and scanned
foods can be edited, imported ones can't. A failed lookup is logged (event id 30). Once a product is saved, the footer
shows the attribution the license asks for. `OpenFoodFacts:BaseUrl` (`https://world.openfoodfacts.org/` in
`appsettings.json`) points the app at another instance, such as the staging server.

## Configuration

The app needs the connection string `ConnectionStrings:Default`. It is not stored in `appsettings.json`;
the app refuses to start without it.

Health checks: `/healthz/live` (the app runs) and `/healthz/ready` (the database answers too).

### Logging

Logs go to stdout as one JSON object per line, for a log collector (e.g. Grafana Alloy → Loki) to pick up.
Locally (`Development`) they use the plain console format. Each log message has a fixed event id (see `src/NutriLog/Log.cs`).

At startup the app applies database migrations. While PostgreSQL is unreachable it tries 15 times, 4 seconds apart,
and logs each failed attempt as a warning. Errors that waiting won't fix (wrong password, missing database, no
permission to connect) stop it at once. A startup failure is logged as `Critical` and the process exits with code 1.

Traces and metrics go to an OpenTelemetry collector when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

### Local development

Run PostgreSQL in Docker; the app creates the database and its tables on the first start:

```powershell
docker run -d --name nutrilog-db -p 5432:5432 -e POSTGRES_USER=nutrilog -e POSTGRES_PASSWORD=<password> postgres:18
```

The connection string lives in user-secrets (stored in `%APPDATA%\Microsoft\UserSecrets\`, outside the project):

```powershell
dotnet user-secrets set --project src/NutriLog "ConnectionStrings:Default" "Host=localhost;Database=nutrilog;Username=nutrilog;Password=<password>"
```

User-secrets are only loaded when `ASPNETCORE_ENVIRONMENT=Development` (the default in `launchSettings.json`).

## CI

`Build and test` builds, runs the tests and sends the analysis to SonarCloud (project `fdaniel02_NutriLog`). The
`main` ruleset requires it by name. `Docker image` builds the image and scans it with Trivy; on `main`,
`Publish image` pushes `ghcr.io/fdaniel02/nutrilog` with the tags `sha-<commit>` (for Argo CD Image Updater) and
`latest`.

The SonarCloud project and its token (the secret `SONAR_TOKEN`, for Actions and Dependabot) are managed in
[homelab-tofu](https://github.com/fdaniel02/homelab-tofu) (`sonarcloud.tf`). Without the secret, the analysis is
skipped.
