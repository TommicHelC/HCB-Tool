# Deploying the add-in to Revit (instructions for agents and developers)

This is the procedure an AI agent (Claude Code, Codex, ...) or a developer follows to put the current
code into a locally installed Revit. For distributing to end users, build the MSI instead
(see [Installer.md](Installer.md)).

## Rules

1. **Deploy only when the user asks for it.** Deploying replaces the add-in the user's Revit loads.
   A plain build deploys too (see "Build without deploying"), so verification builds from branches
   or worktrees must disable deployment.
2. **Never close or kill Revit yourself.** It may hold unsaved work. If Revit is running, ask the
   user to close it (or to save and close) and wait.
3. Deploy only to the Revit version the user named. Default: 2025.
4. After deploying, tell the user which branch / commit was deployed and to which Revit version.

## Prerequisites

| Revit | Configuration | Target framework | SDK |
| --- | --- | --- | --- |
| 2025 | `Debug R25 Deploy` / `Release R25` | `net8.0-windows` | .NET 8 |
| 2026 | `Debug R26 Deploy` / `Release R26` | `net8.0-windows` | .NET 8 |
| 2027 | `Debug R27 Deploy` / `Release R27` | `net10.0-windows` | .NET 10 |

Revit is expected in `C:\Program Files\Autodesk\Revit <year>`. Check with `dotnet --list-sdks`.

## Procedure

1. Check that Revit of that version is not running. The running Revit locks
   `HCB.RevitAddin.dll` and the copy step fails.

   ```powershell
   Get-Process Revit -ErrorAction SilentlyContinue | Select-Object Id, Path, MainWindowTitle
   ```

   If a process with `Revit <year>` in `Path` is listed, stop and ask the user to close it.

2. Check what you are about to deploy: branch, uncommitted changes.

   ```powershell
   git status --short; git log --oneline -1
   ```

3. For Revit 2027 only, restore first (otherwise `NETSDK1005`):

   ```powershell
   dotnet restore .\src\HCB.RevitAddin\HCB.RevitAddin.csproj -p:Configuration="Debug R27 Deploy" -p:Platform=x64
   ```

4. Build with deployment, from the repository root:

   ```powershell
   dotnet build .\Revit_Codex_HCB.sln -c "Debug R25 Deploy"   # or R26 / R27
   ```

   The build (Nice3point.Revit.Build.Tasks) copies the output to
   `%AppData%\Autodesk\Revit\Addins\<year>\`:

   - `HCB.RevitAddin.addin` - the manifest
   - `HCB.RevitAddin\` - `HCB.RevitAddin.dll`, dependencies, icons and `kodyfikacja\Systemy HCB.csv`

   It also removes the old `HCB.Addin.addin` / `HCB.Addin\` deployment if present.

5. Verify the deployment:

   ```powershell
   $dir = "$env:APPDATA\Autodesk\Revit\Addins\2025"
   Get-Item "$dir\HCB.RevitAddin.addin", "$dir\HCB.RevitAddin\HCB.RevitAddin.dll" | Select-Object FullName, LastWriteTime
   ```

   Both timestamps must be from this build.

6. Tell the user to start Revit. The **HCB Tools** tab must appear. If Revit reports that the
   add-in failed to load, check the journal in `%LocalAppData%\Autodesk\Revit\Autodesk Revit <year>\Journals`.

## Build without deploying

`DeployOnBuild` is `true` by default, so even `-c "Debug R25"` deploys. For compile checks, tests
and builds from branches or worktrees:

```powershell
dotnet build .\Revit_Codex_HCB.sln -c "Debug R25" -p:DeployOnBuild=false -p:DeployRevitAddin=false
```

## Rolling back

Deploy again from `main`:

```powershell
git switch main; git pull
dotnet build .\Revit_Codex_HCB.sln -c "Debug R25 Deploy"
```

or reinstall the latest MSI from `dist\<version>\`.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `MSB3027` / `MSB3021` "file is being used by another process" | Revit is running | Ask the user to close Revit, build again |
| `NETSDK1005` for R27 | No restore for `net10.0-windows` | Run the restore in step 3 |
| Tab **HCB Tools** missing after start | Manifest or DLL not deployed, or load error | Repeat step 5; read the Revit journal |
| Old tools still visible | Revit started before the build finished | Restart Revit |
