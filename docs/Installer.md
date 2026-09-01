# MSI Installer

This repository includes a WiX-based MSI project for distributing the add-in to end users.

## What It Installs

The installer copies:

- `HCB.RevitAddin.addin`
- the full `HCB.RevitAddin` payload folder

to:

- `%AppData%\Autodesk\Revit\Addins\2025\` for `R25`
- `%AppData%\Autodesk\Revit\Addins\2026\` for `R26`
- `%AppData%\Autodesk\Revit\Addins\2027\` for `R27`

The package is per-user and does not require administrator rights.

## Build Commands

Build the MSI from the repository root:

- `dotnet build .\src\HCB.RevitInstaller\HCB.RevitInstaller.wixproj -c "Release R25"`
- `dotnet build .\src\HCB.RevitInstaller\HCB.RevitInstaller.wixproj -c "Release R26"`
- `dotnet build .\src\HCB.RevitInstaller\HCB.RevitInstaller.wixproj -c "Release R27"`

The installer project first builds `HCB.RevitAddin` for the same configuration, then packages the published add-in output.

## Output

The MSI is generated under:

- `src\HCB.RevitInstaller\bin\Release R25\HCB.RevitInstaller.msi`
- `src\HCB.RevitInstaller\bin\Release R26\HCB.RevitInstaller.msi`
- `src\HCB.RevitInstaller\bin\Release R27\HCB.RevitInstaller.msi`

## Versioning

The MSI product version is currently controlled in:

- `src/HCB.RevitInstaller/HCB.RevitInstaller.wixproj`

Update `InstallerProductVersion` before shipping a new release.

## Release Procedure

1. Bump `InstallerProductVersion` in `src/HCB.RevitInstaller/HCB.RevitInstaller.wixproj`. Windows Installer only replaces an existing install when the new version is higher; shipping twice with the same number leaves users on the old build.
2. Build all three MSIs (see Build Commands above). Switching to `Release R27` for the first time in a session requires a restore for the `net10.0-windows` target first, otherwise the build fails with `NETSDK1005`:

   ```powershell
   dotnet restore .\src\HCB.RevitAddin\HCB.RevitAddin.csproj -p:Configuration="Release R27" -p:Platform=x64
   ```

3. Copy the three MSIs into `dist\<version>\` and rename them per Revit year - every configuration produces the same `HCB.RevitInstaller.msi` file name:

   ```powershell
   $out = ".\dist\1.0.2"
   New-Item -ItemType Directory -Force -Path $out | Out-Null
   foreach ($c in @('R25','R26','R27')) {
     Copy-Item ".\src\HCB.RevitInstaller\bin\Release $c\HCB.RevitInstaller.msi" "$out\HCB_Tools_1.0.2_$c.msi" -Force
   }
   ```

4. Share the folder (network drive, SharePoint, Teams). `dist/` is git-ignored on purpose - MSIs are build artifacts, not repository content.

## Notes For Users

- Each user installs the MSI matching their Revit year; an R26 build will not load in R25.
- No .NET runtime prerequisite: Revit 2025/2026 ship .NET 8, Revit 2027 ships .NET 10.
- Each Revit year has its own `UpgradeCode`, so R25/R26/R27 can be installed side by side.
- Users who once had the legacy `HCB.Addin` deployment must delete `%AppData%\Autodesk\Revit\Addins\<year>\HCB.Addin.addin` and the `HCB.Addin` folder once. The MSI does not remove it, and Revit would otherwise load both add-ins.