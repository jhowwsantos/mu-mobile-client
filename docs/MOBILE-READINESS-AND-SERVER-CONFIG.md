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

The hard-coded `http://192.168.55.220/Data.zip` URL has been removed from `Constants.DataPathUrl`. That address was private to a local network and could not be a reliable public download URL. Before shipping, decide whether the required data is bundled with the app or hosted at a verified HTTPS location. Verify that the data archive matches this client and that you have rights to distribute it. Do not automatically substitute a random MU data archive.

## CI and build artifacts

The build workflow now assigns Windows builds to Windows runners, Linux to Ubuntu, and macOS/iOS builds to macOS. Android build output is checked for an APK before it is uploaded. GitHub Pages publication runs only on pushes to `main`, after all platform jobs pass.

The iOS job currently builds an **unsigned iOS Simulator artifact** for CI validation. It is not an installable iPhone release. Physical-device distribution still requires a device-target build, Apple signing credentials/provisioning, and a deliberate release workflow. Do not label the simulator zip as an iPhone IPA.

## Remaining compatibility gate

These repository changes remove an unverified private endpoint, validate endpoint configuration, and fix runner selection in CI. They do not implement a MU TCP server and do not make the Socket.IO web backend speak MU Season 6. Before calling the game playable on Android/iPhone, either:

1. identify and verify an existing server that supports the exact packet protocol/version this client expects, or
2. implement/adapt a backend with that protocol and test login, server list, character selection, and gameplay end-to-end.

A successful compilation alone does not prove server compatibility.
