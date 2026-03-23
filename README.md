## Nat Puncher Master Server

This is the bundled replacement for the old hardcoded multiplayer bootstrap service.

It does two simple jobs:

1. Register a host session and return an advertised `address:port` plus a short join code.
2. Resolve a join code back to the advertised `address:port`.

It is intentionally lightweight so players can run their own copy if the original infrastructure disappears again.

### Run

1. Install the .NET 8 SDK.
2. Copy `appsettings.example.json` to `appsettings.json`.
3. Run:

```bash
dotnet run --project Tools/NatPuncherMasterServer
```

### Configure the game

Edit [master_server.json](/c:/Users/Alex/Documents/potential-adventure/Assets/Config/Multiplayer/master_server.json):

- `baseUrl`: the URL for the master server players should use
- `preparePath`: endpoint used when hosting
- `resolveLobbyPathFormat`: endpoint used when joining with a lobby code
- `timeoutMs`: how long the game waits before falling back to plain direct connect
- `showFailurePopup`: whether to show the OS warning popup on master failure

### Notes

- If the master server is unreachable, the game falls back to direct connect and warns the player that port forwarding may be required for WAN play.
- This server is a discovery/bootstrap service, not a full relay. Hosts still need reachable ports for true WAN play unless you extend it with relay or UDP hole-punch coordination.
