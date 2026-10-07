# playwright-asb-csharp

Automated tests in C# using [Playwright for .NET](https://playwright.dev/dotnet/) and [Azure Service Bus](https://learn.microsoft.com/azure/service-bus-messaging/), built on NUnit.

## Tech stack

- .NET 10 (`net10.0`)
- NUnit 4
- Microsoft.Playwright.NUnit
- Azure.Messaging.ServiceBus

## Prerequisites

- **.NET SDK 10.0.401 or later 10.0.x.** global.json sets 10.0.401 as the minimum and rolls forward to the newest installed 10.0.x SDK (`"rollForward": "latestFeature"`).
- **PowerShell 7 (`pwsh`)**, used to run the Playwright browser install script. Windows only ships with Windows PowerShell 5.1, so install PowerShell 7 if needed:

  ```powershell
  winget install --id Microsoft.PowerShell --source winget
  ```

- **A NuGet package source.** Check that nuget.org is configured:

  ```powershell
  dotnet nuget list source
  ```

  If it says `No sources found`, add it. Otherwise restore fails with `NU1100: Unable to resolve ...`.

  ```powershell
  dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
  ```

## Setup

From the repository root:

```powershell
cd PlaywrightAsb.Tests
dotnet build
pwsh bin\Debug\net10.0\playwright.ps1 install
```

The last command downloads the Playwright browsers to `%USERPROFILE%\AppData\Local\ms-playwright`. Run it again after upgrading the `Microsoft.Playwright.NUnit` package.

## Running tests

```powershell
dotnet test PlaywrightAsb.Tests
```

### HTML report

Every test run also writes an HTML report to `PlaywrightAsb.Tests\TestResults\TestResults-yyyyMMdd-HHmmss.html`, listing each test's status, duration and error messages. Each run creates a new file, so earlier reports are kept. Open the latest one in a browser:

```powershell
Get-ChildItem PlaywrightAsb.Tests\TestResults\TestResults-*.html | Sort-Object Name | Select-Object -Last 1 | Invoke-Item
```

The report is configured by the `VSTestLogger` property in `PlaywrightAsb.Tests\PlaywrightAsb.Tests.csproj`. Passing your own `--logger` to `dotnet test` replaces it.

## Project structure

```
playwright-asb-csharp/
├── global.json                 # Pins the .NET SDK version
└── PlaywrightAsb.Tests/        # NUnit test project
```
