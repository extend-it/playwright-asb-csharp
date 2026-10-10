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

### Service Bus tests

By default the Service Bus tests connect to the local [Azure Service Bus emulator](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator) on `localhost:5672`. To run the emulator, start Docker Desktop, then:

```powershell
cd emulator
copy .env.example .env   # then set ACCEPT_EULA=Y and a MSSQL_SA_PASSWORD
docker compose up -d
```

`emulator\Config.json` creates the `security-alerts` topic (subscriptions `alerts-critical`, `alerts-informational`, `alerts-audit`) and the `incident-actions` queue. Stop the emulator with `docker compose down`, and start it again later with `docker compose up -d` (from the `emulator` folder).

The emulator needs host ports 5672 and 5300, so stop any other Service Bus emulator first.

#### Running against a real Service Bus namespace

The connection string, topic and queue are test parameters in `PlaywrightAsb.Tests\.runsettings` (`SERVICEBUS_CONNECTION_STRING`, `SB_TOPIC`, `SB_QUEUE`). These take precedence over environment variables of the same name, so setting an environment variable has no effect while `.runsettings` defines them. To use a real namespace, either:

- edit the values in `.runsettings` (don't commit a real connection string), or
- override them for a single run (PowerShell 7):

  ```powershell
  dotnet test PlaywrightAsb.Tests -- 'TestRunParameters.Parameter(name="SERVICEBUS_CONNECTION_STRING", value="Endpoint=sb://<namespace>.servicebus.windows.net/%3BSharedAccessKeyName=<key-name>%3BSharedAccessKey=<key>")'
  ```

  Write every `;` in the connection string as `%3B`. `dotnet test` otherwise turns semicolons into spaces and rejects the parameter. In Windows PowerShell 5.1, write the inner quotes as `\"` instead of `"`.

The namespace needs the same entities as the emulator: the `security-alerts` topic with its three filtered subscriptions, and the `incident-actions` queue (see `emulator\Config.json`).

### HTML report

Every test run also writes an HTML report to `PlaywrightAsb.Tests\TestResults\TestResults-yyyyMMdd-HHmmss.html`, listing each test's status, duration and error messages. Each run creates a new file, so earlier reports are kept. Open the latest one in a browser:

```powershell
Get-ChildItem PlaywrightAsb.Tests\TestResults\TestResults-*.html | Sort-Object Name | Select-Object -Last 1 | Invoke-Item
```

The report is configured by the `VSTestLogger` property in `PlaywrightAsb.Tests\PlaywrightAsb.Tests.csproj`. Passing your own `--logger` to `dotnet test` replaces it.

## Mutation checks

Each test was verified to fail for the reason its name claims: the code it guards was deliberately broken, and the expected failure confirmed.

| Test | Mutation | Expected failure |
|---|---|---|
| `AlertThatFailedValidationLandsInDlqWithReason` | Remove the `DeadLetterMessageAsync` call | DLQ empty: count 0, expected 1 |
| `PoisonAlertAbandonedMaxDeliveryCountIsDeadLetteredByTheBroker` | Replace `AbandonMessageAsync` with `CompleteMessageAsync` | Delivery counts `[1]`, expected `[1, 2]` |
| `BrokerDropsASecondSendWithTheSameMessageId` | Give the second send a different `MessageId` | Two actions received, expected one |
| `IdempotentHandlerIsolatesTheDeviceOnlyOnceAcrossARedelivery` | Drop the `processed.Add` guard | Outcomes `["applied", "applied"]`, device isolated twice |

The last two mutations are also kept as permanent **control tests** that run on every build: `BrokerAcceptsResendsWithADifferentMessageId` and `NaiveHandlerIsolatesTheDeviceTwiceOnRedelivery`. Each shows the condition under which the protection deliberately does not apply.

These checks were done by hand; [Stryker.NET](https://stryker-mutator.io) would automate them.

## Project structure

```
playwright-asb-csharp/
├── emulator/                   # Local Service Bus emulator (Docker Compose)
├── global.json                 # Pins the .NET SDK version
└── PlaywrightAsb.Tests/        # NUnit test project
```
