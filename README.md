# playwright-asb-csharp
[![tests](https://github.com/extend-it/playwright-asb-csharp/actions/workflows/tests.yml/badge.svg?branch=main)](https://github.com/extend-it/playwright-asb-csharp/actions/workflows/tests.yml)

Tests for an event-driven security-alert flow on [Azure Service Bus](https://learn.microsoft.com/azure/service-bus-messaging/): topic routing, dead-lettering and idempotent handling, plus browser and API tests with [Playwright for .NET](https://playwright.dev/dotnet/). A C# and NUnit port of [playwright-asb](https://github.com/extend-it/playwright-asb), originally written in TypeScript.

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

By default the Service Bus tests connect to the local [Azure Service Bus emulator](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator) on `localhost:5672`. The emulator's compose file lives in this repo under `docker/`. Start Docker Desktop, then from the repository root start the emulator and check that it's up:

```powershell
docker compose -f docker/docker-compose.yaml up -d
curl.exe http://localhost:5300/health
```

The Service Bus fixtures call this health endpoint before connecting. If the emulator isn't running, they fail fast with a clear message that includes the start command, instead of waiting for an AMQP timeout.

`docker\Config.json` creates the `security-alerts` topic (subscriptions `alerts-critical`, `alerts-informational`, `alerts-audit`) and the `incident-actions` queue. Stop the emulator with `docker compose -f docker/docker-compose.yaml down`.

The emulator needs host ports 5672 and 5300, so stop any other Service Bus emulator first.

`docker/docker-compose.yaml` uses the Compose project name `sb-emulator` and the fixed container names `servicebus-emulator` and `sqledge`. Docker allows only one container per name, so before starting it, remove any existing containers with those names that were created by another setup.

#### Running against a real Service Bus namespace

The connection string, topic and queue are test parameters in `PlaywrightAsb.Tests\.runsettings` (`SERVICEBUS_CONNECTION_STRING`, `SB_TOPIC`, `SB_QUEUE`). These take precedence over environment variables of the same name, so setting an environment variable has no effect while `.runsettings` defines them. To use a real namespace, either:

- edit the values in `.runsettings` (don't commit a real connection string), or
- override them for a single run (PowerShell 7):

  ```powershell
  dotnet test PlaywrightAsb.Tests -- 'TestRunParameters.Parameter(name="SERVICEBUS_CONNECTION_STRING", value="Endpoint=sb://<namespace>.servicebus.windows.net/%3BSharedAccessKeyName=<key-name>%3BSharedAccessKey=<key>")'
  ```

  Write every `;` in the connection string as `%3B`. `dotnet test` otherwise turns semicolons into spaces and rejects the parameter. In Windows PowerShell 5.1, write the inner quotes as `\"` instead of `"`.

The namespace needs the same entities as the emulator: the `security-alerts` topic with its three filtered subscriptions, and the `incident-actions` queue (see `docker\Config.json`).

Before connecting, the Service Bus fixtures always check the local emulator's health endpoint (`http://localhost:5300/health`), whatever connection string is configured. Against a real namespace they therefore fail unless a local emulator is also running.

### HTML report

Every test run also writes an HTML report to `PlaywrightAsb.Tests\TestResults\TestResults-yyyyMMdd-HHmmss.html`, listing each test's status, duration and error messages. Each run creates a new file, so earlier reports are kept. Open the latest one in a browser:

```powershell
Get-ChildItem PlaywrightAsb.Tests\TestResults\TestResults-*.html | Sort-Object Name | Select-Object -Last 1 | Invoke-Item
```

The report is configured by the `VSTestLogger` property in `PlaywrightAsb.Tests\PlaywrightAsb.Tests.csproj`. Passing your own `--logger` to `dotnet test` replaces it.

## Test overview

### UI: `TodoMvcTests` (`UI/TodoMvcTests.cs`)

Playwright browser tests against the TodoMVC demo app (`https://demo.playwright.dev/todomvc/`, override with `UI_BASE_URL`). The fixture inherits from `UiTestBase`, which keeps a Playwright trace for each failed test.

| Test | What it checks |
|---|---|
| `AddedTodosAppearInOrderAndAreCounted` | Two added todos are listed in the order they were added, and the counter shows "2 items left" |
| `CompletedTodoLeavesTheActiveFilter` | Completing a todo marks it completed and the counter shows "1 item left"; the Active filter then lists only the other todo, and the Completed filter only the completed one |

### Service Bus: `RoutingTests` (`ServiceBus/`)

Topic routing on `security-alerts`, based on the `severity` application property.

| Test | What it checks |
|---|---|
| `CriticalAlertReachesCriticalAndAuditOnly` | A critical alert reaches `alerts-critical` and `alerts-audit` but not `alerts-informational`, with its body and `severity` property intact |
| `InformationalAlertDoesNotReachCritical` | An informational alert reaches `alerts-informational` but not `alerts-critical` |

### Service Bus: `DeadLetterTests` (`ServiceBus/`)

Dead-lettering on the `alerts-audit` subscription.

| Test | What it checks |
|---|---|
| `AlertThatFailedValidationLandsInDlqWithReason` | A consumer dead-letters an alert that has no `DeviceId`; the dead-letter queue holds exactly one copy, with reason `SchemaValidationFailed`, a description mentioning `deviceId`, and the original body |
| `PoisonAlertAbandonedMaxDeliveryCountIsDeadLetteredByTheBroker` | An alert abandoned on every delivery is delivered twice (delivery counts 1 and 2); the broker then dead-letters it with reason `MaxDeliveryCountExceeded` |

### Service Bus: `IdempotencyTests` (`ServiceBus/`)

Duplicate detection and idempotent handling on the `incident-actions` queue.

| Test | What it checks |
|---|---|
| `BrokerDropsASecondSendWithTheSameMessageId` | Two sends with the same `MessageId` (a publisher retry) arrive only once |
| `BrokerAcceptsResendsWithADifferentMessageId` (control) | The same body sent with two different `MessageId`s arrives twice, because deduplication is keyed on `MessageId`, not on the body |
| `IdempotentHandlerIsolatesTheDeviceOnlyOnceAcrossARedelivery` | An isolate-device action is abandoned once and redelivered (delivery count 2); a handler guarded by a processed-ids store applies it once and skips the redelivery, so the device is isolated once |
| `NaiveHandlerIsolatesTheDeviceTwiceOnRedelivery` (control) | The same redelivery without the guard applies the action twice, so the device is isolated twice |

### API: `EmulatorHealthApiTests` (`Api/`)

Tests of the emulator's management API on port 5300. These use Playwright's `APIRequestContext` via `PlaywrightTest`, so no browser starts.

| Test | What it pins |
|---|---|
| `HealthEndpointAnswersOk` | `/health` → 200, JSON status `"healthy"` |
| `UnknownPathIsReadAsAnEntityLookupAndAnswersAnEmptyFeed` | unknown name → 200 with an empty Atom feed, not 404 |
| `ExistingEntityAnswersASingleEntry` (×2, parameterised) | queue and topic → a single Atom entry with `QueueDescription` / `TopicDescription` |
| `IncidentActionsQueueIsConfiguredAsTheIdempotencyTestsAssume` | the queue settings the idempotency tests rely on: duplicate detection (`PT2M`), `MaxDeliveryCount` 2 |

## Design notes

How the Service Bus tests stay reliable against a shared broker:

- **Each test only sees its own messages.** Every test sends with a unique correlation ID (`test-{Guid}`) and asserts only on messages carrying it, so leftovers from earlier runs can't affect the result. `MessageId`s are unique per run too, because the queue remembers them for the 2-minute duplicate-detection window.
- **No sleeps.** Receives run in loops bounded by a time budget and stop as soon as the expected messages have arrived. Asserting that something does *not* arrive uses a shorter budget (4 s instead of 10 s), so a passing negative check doesn't cost the full wait.
- **Known starting state.** Fixtures that share entities are `[NonParallelizable]` and drain their subscription or queue in `[SetUp]`.
- **Two receive contracts.** `ReceiveMatchingAsync` observes: it reads, settles everything, and returns the matches. `ReceiveOwnAsync` takes ownership: it returns the test's message still locked, so the test decides its fate (dead-letter or abandon).
- **Each message counted once.** A lock that expires mid-test means the broker delivers the same message again. `ReceiveMatchingAsync` therefore de-duplicates by `SequenceNumber`, which stays the same across redeliveries, and logs any redelivery it skips. Settling a message the test only observed is cleanup, not an assertion, so a lost lock there is logged instead of failing the test.
- **The dead-letter queue is read with `ReceiveAndDelete`.** A reader that only inspects messages has no reason to hold locks, and with no locks there are no expiring locks and no redeliveries.

The last two came out of a CI-only failure: on the slower runner, the 5-second lock on `alerts-audit` expired while dead-lettered messages were being read, and the same message was counted twice.

## Emulator notes

- The management API (port 5300) answers lookups for queues and topics with a single Atom entry, and for unknown names with an empty Atom feed (200, not 404).
- Every topic-subscription route (`/{topic}/Subscriptions`, collection or single entity, with or without `api-version`) returns 500 Internal Server Error, so subscription configuration is verified indirectly by the routing tests.

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

## CI

`.github/workflows/tests.yml` starts the emulator in Docker, installs Chromium, runs dotnet test, and uploads the HTML report and traces as artifacts.
Setup commands in this README are Windows paths, while CI runs on Linux.

## Project structure

```
playwright-asb-csharp/
├── docker/                     # Service Bus emulator used by the tests (Docker Compose + Config.json)
├── global.json                 # Pins the .NET SDK version
└── PlaywrightAsb.Tests/        # NUnit test project
```
