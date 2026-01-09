# World Time
A social time zone reference tool!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/J3J65TW2E)

#### Documentation, help, resources
* [Main website, user documentation](https://noithecat.dev/bots/WorldTime)
* [Official server](https://discord.gg/JCRyFk7)

#### Running your own instance
You need:
* .NET 10 (https://dotnet.microsoft.com/en-us/)
* PostgreSQL (https://www.postgresql.org/)
* A Discord bot token (https://discord.com/developers/applications)

Edit `config.example.json` as needed and save it as `settings.json`.

Set up the database and dependencies:
```sh
$ dotnet restore
$ dotnet tool restore
$ dotnet ef database update -- -c path/to/settings.json
```

Build the executable:
```sh
$ dotnet publish src/WorldTime/WorldTime.csproj -c Release -o . \
    -p:PublishSingleFile=true -p:DebugType=None
```
This will produce `WorldTime.exe` (Windows) or `WorldTime` (Linux/macOS). Place it wherever you like along with your `settings.json`. Requires .NET 10 runtime.
