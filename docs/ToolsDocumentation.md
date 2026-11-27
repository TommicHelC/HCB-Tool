# Tools documentation workflow

The **Narzędzia** section of the root `README.md` is generated. Do not edit the block between
`<!-- TOOLS:START -->` and `<!-- TOOLS:END -->` by hand.

| Piece | File | Source of truth |
| --- | --- | --- |
| Ribbon layout (panels, pulldowns, stacks, buttons) | `tools/docs/ribbon_model.py` | `src/HCB.RevitAddin/Infrastructure/RibbonDefinitions.cs` |
| Polish descriptions, steps, notes, badges | `tools/docs/tool_texts.py` | written by hand |
| Raw screenshots of the ribbon | `tools/docs/capture_ribbon.ps1` | running Revit |
| Outlined images | `tools/docs/make_images.py` | raw screenshots |
| README section | `tools/docs/build_readme.py` | all of the above |

## Text only (new tool, changed description)

1. Add or update the entry in `tools/docs/tool_texts.py`. The key is the button id from `RibbonDefinitions.cs`.
   A tool without an entry still appears in the README, using its ribbon tooltip.
2. Run:

```powershell
python tools/docs/build_readme.py
```

## Screenshots (ribbon changed)

1. Start Revit with the current build of the add-in and open the **HCB Tools** tab.
   Close dialogs and keep nothing over the ribbon.
2. Capture. Do not touch the mouse while it runs: the script clicks each pulldown button
   (this only opens the menu, never runs a command) and closes the menu with ESC.

```powershell
powershell -ExecutionPolicy Bypass -File tools\docs\capture_ribbon.ps1 -Pulldowns
# repeat single menus if one did not open, e.g. the first one:
powershell -ExecutionPolicy Bypass -File tools\docs\capture_ribbon.ps1 -Pulldowns -Only 1
```

3. Build the images and the README:

```powershell
python tools/docs/make_images.py
python tools/docs/build_readme.py
```

4. Check the images in `docs/images/ribbon/` before committing.

Notes:

- Raw captures go to `.scrape/ribbon/` (ignored by git).
- Everything below the ribbon is painted over in the images, so the open project's content never
  ends up in the repository. The ribbon tab row is cut off as well.
- Pulldown menus are not exposed to UI Automation. `make_images.py` finds each menu from the
  before/after difference and its 1 px frame (`MENU_FRAME`, Revit 2025 dark theme). After a theme
  change, update that colour.
- Buttons available only in newer Revit (`min_version`, e.g. Param Service in 2026+) have no image
  when captured in an older Revit. The README then shows the icon instead.
- `HCB_RIBBON_DEFINITIONS=<path to RibbonDefinitions.cs>` makes the scripts follow the ribbon of
  another checkout, e.g. to prepare images for a feature branch before it is merged.
  Icons of such tools are copied to `docs/images/icons/`, because they do not exist in this checkout yet.
  Once the branch is merged, regenerate without the variable and delete `docs/images/icons/`
  (the README then links the icons in `src/` directly).
