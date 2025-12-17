# Bring back the Address bar

Windows address bar application built with .NET 8 WinForms.

## Build & Publish

After making changes, publish a self-contained single-file executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The executable will be at `publish/AddressBar.exe`.

## Architecture

- Single file: `Program.cs` contains all code
- Docks to top or bottom of screen as an AppBar
- Stores history in registry: `HKEY_CURRENT_USER\Software\AddressBar\TypedPaths`
- Supports URLs, local paths, and shell commands
