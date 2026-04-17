# Messenger

Project purpose
---------------
Messenger is a lightweight, modular multi-channel messaging service that centralizes sending of SMS, WhatsApp, Email and Push Notifications. It is split into a small Web API, a core library of contracts and DTOs, and an infrastructure project with concrete provider implementations.

Projects
--------
- Messenger.Api — ASP.NET Core Web API exposing endpoints for sending messages
- Messenger.Core — Interfaces, DTOs, options and core service logic (contracts only)
- Messenger.Infrastructure — Implementations for providers (Twilio, Corvass, SMTP, FCM, WhatsApp, console, etc.)

How to run
----------
Visual Studio
1. Open the solution (Messenger.sln) in Visual Studio.
2. Set `Messenger.Api` as the startup project.
3. Build (Ctrl+Shift+B) and Run (F5 or Ctrl+F5).

CLI
1. Open a terminal in the solution root (PowerShell recommended).
2. Build: `dotnet build`
3. Run the API project: `dotnet run --project Messenger.Api\Messenger.Api.csproj`
4. The app listens on the configured URL in Program.cs / appsettings.json.

Environment variables & configuration
-------------------------------------
This project uses the Options pattern. Provide required secrets via environment variables, `dotnet user-secrets` (development), or a secret store in production.
Common keys (examples only):
- TWILIO_ACCOUNT_SID, TWILIO_AUTH_TOKEN, TWILIO_FROM
- CORVASS_API_KEY, CORVASS_BASE_URL
- SMTP_HOST, SMTP_PORT, SMTP_USERNAME, SMTP_PASSWORD
- FCM_SERVER_KEY
- WHATSAPP_PHONE_NUMBER_ID, WHATSAPP_ACCESS_TOKEN

Security notes
--------------
- Never commit API keys, credentials, or user secrets to source control.
- Use `dotnet user-secrets` during development or a managed secret store (Key Vault, AWS Secrets Manager, etc.) in production.
- Treat all external responses as untrusted and validate before use.

Architecture overview
---------------------
- Separation of concerns: Core defines contracts and DTOs; Infrastructure contains provider implementations; API composes and exposes endpoints.
- Dependency Injection is used to register concrete senders against core interfaces.
- Add new providers by implementing the appropriate I* interface in Messenger.Core and registering it in DI within Messenger.Infrastructure.

Developer notes
---------------
- Add unit and integration tests (xUnit/NUnit) and configure CI to run them.
- Add Swagger (OpenAPI) to Messenger.Api for easier integration testing.
- Remove any generated build artifacts from source control (`bin/`, `obj/`).
