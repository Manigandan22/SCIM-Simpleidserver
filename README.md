# SCIM 2.0 Solution (.NET 8)

This repository contains a production-oriented SCIM 2.0 implementation using `SimpleIdServer.Scim` and `MassTransit` with Azure Service Bus.

## Architecture

*   **src/Scim.Api**: ASP.NET Core API hosting the SCIM endpoints.
    *   Decorates `ISCIMRepresentationCommandRepository` to publish integration events.
    *   Validates requests using `UserValidator`.
    *   Exposes Health Checks (`/health/live`, `/health/ready`) and OpenTelemetry metrics/traces.
*   **src/Scim.Worker**: Background Worker Service.
    *   Consumes SCIM integration events (`RepresentationAdded`, `Updated`, `Removed`) from Azure Service Bus.
    *   Implements Idempotency using Redis (`IIdempotencyService`).
    *   Handles Retries and Resilience.
*   **src/Scim.Domain**: Contains Custom Schema definitions and Domain Contracts.
*   **src/Scim.Infrastructure**: Persistence (EF Core), Messaging (MassTransit), and Caching (Redis) implementations.

## Prerequisites

*   .NET 8 SDK
*   Docker (for Redis and SQL Server)
*   Azure Service Bus Namespace (or emulator/alternative transport for local dev if configured)

## Running Locally

1.  **Start Infrastructure**:
    ```bash
    docker-compose up -d
    ```

2.  **Configure**:
    Update `src/Scim.Api/appsettings.json` and `src/Scim.Worker/appsettings.json` with your Azure Service Bus connection string.
    *   `ConnectionStrings:AzureServiceBus`

3.  **Run API**:
    ```bash
    dotnet run --project src/Scim.Api/Scim.Api.csproj
    ```
    The API will run on `https://localhost:5001` (or http port 5000).

4.  **Run Worker**:
    ```bash
    dotnet run --project src/Scim.Worker/Scim.Worker.csproj
    ```

## Testing

Use `requests.http` with VS Code REST Client or similar tool to test endpoints.

## Production Notes

*   **Persistence**: The solution uses `SimpleIdServer.Scim.Persistence.EF`. Ensure database migrations are applied (uncomment migration code in `Program.cs` or run externally).
*   **Scaling**: The API is stateless and can be scaled horizontally. The Worker can be scaled horizontally; MassTransit handles competing consumers on the subscription.
*   **Resilience**: Retry policies are configured in MassTransit. Idempotency is enforced via Redis locks on event IDs.
*   **Observability**: OpenTelemetry is configured for console export. Point it to OTLP collector in production.

## Design Decisions

*   **Integration Events**: Instead of using internal SimpleIdServer events which might be in-process, we intercept the Repository commands to ensure reliable publishing to the Bus *after* (or during) transaction. Note: For true transactional outbox, use MassTransit Transactional Outbox with EF Core.
*   **Custom Schema**: Defined in `Scim.Domain`.
