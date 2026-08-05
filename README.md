# HCB Revit Add-in

Native C# Revit add-in for Autodesk Revit 2025, 2026, and 2027, developed to replace and extend workflows previously maintained in `HCB_Tools`.

The repository contains two deliverables:

- `HCB.RevitAddin` - the production add-in loaded by Revit
- `HCB.RevitInstaller` - a WiX-based MSI installer that packages the add-in for end users

## Repository Overview

- `src/HCB.RevitAddin/` - main add-in project targeting `net8.0-windows` for R25/R26 and `net10.0-windows` for R27
- `src/HCB.RevitInstaller/` - per-user MSI installer project for Revit 2025, 2026, and 2027
- `docs/GitHubSolutionOverview.md` - high-level solution description for GitHub publication
- `docs/Installer.md` - MSI build and versioning notes
- `docs/WithoutOpenPlan.md` - design notes for batch workflows without opening files
- `HCB_Tools/` - Python reference implementation used during porting
- `geeWiz/` - architecture reference used as a structural baseline
- `kodyfikacja/` - static project data distributed with the add-in

## What The Add-in Includes

The add-in exposes a ribbon tab named `HCB Tools` and groups business workflows into isolated feature folders under `src/HCB.RevitAddin/Features/`.

Current functional areas include:

- view and filter workflows such as `CopyFilters`, `ViewFiltersBulkEdit`, `ViewFiltersLegend`, and `TransferViewTemplates`
- renaming and reporting workflows such as `RenameViews`, `RenameSheets`, `RenameMaterials`, `RenameFamilyContent`, and `FamilyParameterReport`
- MEP-focused utilities such as `FlowChanger`, `HCWireSize`, `SpaceToElement`, `SpaceParamLinked`, `SystemAssigner`, `DuctFittingsArea`, and `MassOfDuctsFittings`
- batch and offline workflows such as `BatchFileScan`, `UnloadLinks`, and `UpgradeAndCopy`

## Build

Build the solution for the target Revit version:

```powershell
dotnet build .\Revit_Codex_HCB.sln -c "Debug R25"
dotnet build .\Revit_Codex_HCB.sln -c "Debug R26"
dotnet build .\Revit_Codex_HCB.sln -c "Debug R27"
dotnet build .\Revit_Codex_HCB.sln -c "Release R25"
dotnet build .\Revit_Codex_HCB.sln -c "Release R26"
dotnet build .\Revit_Codex_HCB.sln -c "Release R27"
```

`R27` builds target `net10.0-windows` and require a .NET 10 SDK. `R25` and `R26` remain on `net8.0-windows`.

To deploy the add-in directly into the local Revit addins folder during build:

```powershell
dotnet build .\Revit_Codex_HCB.sln -c "Debug R25 Deploy"
dotnet build .\Revit_Codex_HCB.sln -c "Debug R26 Deploy"
dotnet build .\Revit_Codex_HCB.sln -c "Debug R27 Deploy"
```

## MSI Packaging

Build the MSI packages from the repository root:

```powershell
dotnet build .\src\HCB.RevitInstaller\HCB.RevitInstaller.wixproj -c "Release R25"
dotnet build .\src\HCB.RevitInstaller\HCB.RevitInstaller.wixproj -c "Release R26"
dotnet build .\src\HCB.RevitInstaller\HCB.RevitInstaller.wixproj -c "Release R27"
```

The current default MSI product version is `1.0.1`.

Generated installers are written to:

- `src/HCB.RevitInstaller/bin/Release R25/HCB.RevitInstaller.msi`
- `src/HCB.RevitInstaller/bin/Release R26/HCB.RevitInstaller.msi`
- `src/HCB.RevitInstaller/bin/Release R27/HCB.RevitInstaller.msi`

## Documentation

- [Solution overview](docs/GitHubSolutionOverview.md)
- [Installer notes](docs/Installer.md)
- [Without-open workflow plan](docs/WithoutOpenPlan.md)
- [Porting plan](docs/PortingPlan.md)
- [Icon workflow](docs/IconWorkflow.md)
