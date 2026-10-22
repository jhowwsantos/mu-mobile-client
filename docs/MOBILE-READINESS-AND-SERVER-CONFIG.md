# Mobile readiness and server configuration

## Current status

This repository contains a native .NET/MonoGame client that expects a MU Connect Server TCP endpoint and uses MU Season 6 packet handling. The separately deployed `jhowwsantos/mu-mobile-server` service is a Node.js/Fastify web API with Socket.IO. A web API using Socket.IO is not automatically compatible with the native client's MU TCP protocol.

The Render service currently identified for `mu-mobile-server` is a web service at `https://mu-mobile-server.onrender.com`. Its Render configuration does not list an additional raw TCP port. Do not enter that HTTPS URL into `ConnectServerHost`, and do not treat the web service's HTTP port as a MU Connect Server port.

## Deliberately unset endpoint

The checked-in `Client.Main/appsettings.json` leaves `MuOnlineSettings.ConnectServerHost` empty and `ConnectServerPort` set to `0`. This is intentional: no compatible native MU server endpoint has been verified. The client validates the endpoint and stops startup rather than silently trying a private LAN IP.

When a compatible server is identified, set these values in `appsettings.json` for a release build or in `appsettings.local.json` for local development:

- `MuOnlineSettings:ConnectServerHost`: DNS name or public IP of the actual MU Connect Server.
- `MuOnlineSettings:ConnectServerPort`: the TCP port documented by that server.
- `MuOnlineSettings:ProtocolVersion`, `ClientVersion`, and `ClientSerial`: values confirmed by the server operator. Do not guess these.
- Confirm the server's advertised game-server address is reachable by clients outside its private network as well.

The repository's `appsettings.local.json` is optional and is loaded after `appsettings.json`, so it overrides matching values during local development. Do not commit credentials or private deployment secrets.

## Game data

The hard-coded `http://192.168.55.220/Data.zip` URL has been removed from `Constants.DataPathUrl`. That address was private to a local network and could not be a reliable public download URL. The loader no longer silently falls back to a Webzen archive: if local assets are missing and no verified HTTPS data URL is configured, it stops with a clear configuration error. Before shipping, decide whether the required data is bundled with the app or hosted at a verified HTTPS location. Verify that the data archive matches this client and that you have rights to distribute it. Do not automatically substitute a random MU data archive.

## CI and build artifacts

The build workflow now assigns Windows builds to Windows runners, Linux to Ubuntu, and macOS/iOS builds to macOS. Android build output is checked for an APK before it is uploaded. GitHub Pages publication runs only on pushes to `main`, after all platform jobs pass.

The iOS job currently builds an **unsigned iOS Simulator artifact** for CI validation. It is not an installable iPhone release. Physical-device distribution still requires a device-target build, Apple signing credentials/provisioning, and a deliberate release workflow. Do not label the simulator zip as an iPhone IPA.

## Client adaptation in progress

`Client.Main/Networking/MobileBackendClient.cs` now provides a separate HTTP/Socket.IO transport foundation for the existing web backend: account registration/login, character listing/creation, authenticated realtime connection, character selection, movement, and attack events. `MuOnlineSettings.BackendBaseUrl` points at the identified Render service. The legacy MU Season 6 TCP transport remains separate and is not a substitute for this adapter.

This transport layer is **not yet wired into the login/character/game scenes** and has not been build-tested in CI. Therefore it must not be described as a playable integration yet. The next implementation step is to route the native client flow through this adapter, map the server character/world state to the existing renderer, and handle the server events in the game loop. The current backend only has a starter world/combat model, so it will not provide full classic MU functionality without additional server work.

## Remaining compatibility gate

These changes remove an unverified private endpoint, validate endpoint configuration, and begin adapting the client to the actual HTTP/Socket.IO backend rather than pretending it is a native MU TCP server. Before calling the game playable on Android/iPhone, wire the adapter into the UI/game scenes, test account/character/realtime flows against the deployed backend, run platform builds, and verify gameplay end-to-end. A successful compilation alone does not prove server compatibility.
