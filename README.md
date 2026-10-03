# GamePivot

GamePivot is a Windows diagnostic and guided-repair tool for older Steam games.

It currently:

- discovers installed games across all configured Steam libraries;
- parses each top-level executable's PE import table;
- reports missing local, Windows, Visual C++, and legacy DirectX dependencies;
- reads Steam manifests and recent `content_log.txt` failures;
- checks Windows Security history for detections in the selected game folder;
- uses a JSON rule base for split-title and shared-depot problems;
- delegates downloads to Steam or Microsoft instead of downloading loose DLL files;
- exports a machine-readable JSON report.

## Build and run

```powershell
dotnet build
dotnet run
```

Headless diagnosis:

```powershell
dotnet run -- --diagnose 393080 --output diagnosis.json
```

Publish a standalone Windows executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Scope

GamePivot automates diagnosis and opens the correct official repair flow. Every repair runs
through Steam or Microsoft's own installers, so Steam manifests and ownership checks stay
intact and every DLL comes from its publisher. The same diagnostic model extends to GOG,
Epic, EA, Ubisoft, and publisher-specific repair APIs through adapters.

## Bundled rule

The bundled rule covers Call of Duty: Modern Warfare Remastered:

- single-player app: `393080`;
- companion multiplayer app: `393100`;
- shared common depots: `393103` and `393117`;
- shared language depot: `393104` through `393116`, selected from the installed language.

When Steam installs only the single-player depots, GamePivot identifies the missing
shared content and opens the companion install through Steam.
