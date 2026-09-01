# Audyt narzędzi HCB Tools

Stan na 2026-09-01, gałąź `cleanup-split-worktree-20260805`.

Zakres: wszystkie 46 folderów w `src/HCB.RevitAddin/Features` (273 pliki `.cs`, ~25 kLOC),
warstwa `Infrastructure` oraz konfiguracja builda.

## Metoda

1. Weryfikacja spójności projektu — `csproj` ma `EnableDefaultCompileItems=false`, więc plik
   pominięty we wpisach `<Compile>` po prostu nie trafia do builda.
2. Statyczny przegląd wszystkich plików pod kątem antywzorców Revit API: transakcje i kolektory
   w pętlach, `Regenerate()` w pętli, `TaskDialog` w pętli, kolektory bez filtra kategorii/klasy,
   połykanie wyjątków.
3. Ręczna analiza każdego zgłoszonego miejsca i poprawki tam, gdzie zysk był realny.
4. Weryfikacja wiązania wstążka ↔ komendy oraz atrybutów `[Transaction]`.
5. Build wszystkich trzech konfiguracji (R25 / R26 / R27).

## Stan wyjściowy

| Sprawdzenie | Wynik |
| --- | --- |
| Pliki `.cs` na dysku bez wpisu w `csproj` | tylko `Features/_Template` (celowo) |
| Wpisy `csproj` bez pliku na dysku | brak |
| Duplikaty wpisów `Compile` | brak |
| Komendy `IExternalCommand` bez `[Transaction]` | brak |
| Duplikaty nazw przycisków wstążki | brak |
| Transakcja tworzona w pętli | brak |
| `FilteredElementCollector` w pętli | brak |
| `Regenerate()` w pętli | brak |
| `catch` zwracający `Result.Succeeded` | brak |
| Kolektory bez filtra kategorii/klasy | 12 |
| Puste `catch` | 8 |
| Ostrzeżenia builda | 20 (R25/R26), 23 (R27) |

Higiena transakcji i struktura projektu są w porządku. Problemem była wydajność: kilka narzędzi
przeglądało cały model w LINQ zamiast oddać filtrowanie silnikowi Revita.

## Co zostało naprawione

### Wydajność (commit `perf: stop scanning the whole model in the hot tools`)

| Narzędzie | Było | Jest |
| --- | --- | --- |
| Duct Acc Num | kolektor po całym modelu, `IsSupported` w LINQ (2 miejsca) | `OfCategory(OST_DuctAccessory)` |
| Duct&Fit Num | kolektor po całym modelu + skan „ostatni numer" z `LookupParameter` na każdym elemencie modelu | `ElementMulticategoryFilter` zależny od zakresu numeracji, w obu ścieżkach |
| Manual Num | skan „ostatni numer" wołał `LookupParameter` na każdym elemencie modelu | id parametru rozwiązywane z zaznaczenia, potem `ElementParameterFilter(HasValueFilterRule)` — Revit sam pomija elementy bez parametru |
| HVAC Tagging | kolektor widoku + porównanie kategorii w LINQ | `ElementMulticategoryFilter` |
| Param Clear / Param Calculator | katalog parametrów iterował `element.Parameters` dla **każdego** elementu modelu | jedna próbka na parę (kategoria, typ) — instancje tego samego typu mają identyczny zestaw parametrów |
| Param Clear, zakres „Category" | skan całego modelu i porównanie nazwy kategorii | kategoria rozwiązywana raz, filtr po jej id; fallback do starego skanu, gdy nazwy nie da się rozwiązać |
| Purge Annotations | `.ToElements()` materializował listę całego modelu przed odczytem pierwszego `GetTypeId()` | strumieniowanie kolektora |
| Unhide All | do `View.UnhideElements` szło **wszystko** z modelu; licznik „Kandydaci" pokazywał liczbę elementów modelu, nie ukrytych | tylko elementy faktycznie ukryte w widoku, liczniki zgodne z prawdą |
| ribbon (`App.cs`) | handler `Idling` nigdy nie odpinany — chodził przy każdym bezczynnym cyklu Revita przez całą sesję | odpinany po zbudowaniu wstążki i w `OnShutdown` |
| Duct&Fit Num, lista parametrów długości | `OfCategory(OST_DuctCurves).Cast<Duct>()` — `InvalidCastException` przy elemencie nie-`Duct` | `OfClass(typeof(Duct))`, szybki filtr i brak ryzyka rzutowania |

Skala zysku rośnie z modelem. Na modelu instalacyjnym z setkami tysięcy elementów skan
`LookupParameter` po całym modelu to najdroższa operacja w całym zestawie — teraz filtr wykonuje
silnik Revita w kodzie natywnym.

### Higiena builda (commit `fix: clear the standing build warnings`)

- `Splitter`: `TryGetOrientedLine` dostał `[NotNullWhen(true)]`, wywołania trzymają `Line?`.
  To była realna ścieżka dereferencji bez sprawdzenia, nie tylko szum kompilatora.
- `WithoutOpen`: WinForms `FolderBrowserDialog` → WPF `OpenFolderDialog`. Znika 6 ostrzeżeń CA1416
  i jedyna zależność od WinForms; z `csproj` wypadło `UseWindowsForms`.

Debug R25 i R26 budują się z **0 ostrzeżeń**. R27 zostaje z 3.

### Porządek w repo

- `ProjectParameterAudit` leżał w `Features/SharedParameters/`, choć ma własny namespace —
  przeniesiony do `Features/ProjectParameterAudit/`.
- Instalator: wersja 1.0.2, poprawione ścieżki wyjściowe w `docs/Installer.md`, opisana procedura
  wydania, `dist/` w `.gitignore`.

## Co zostaje — uszeregowane

### 1. Dwa narzędzia są kompilowane, ale niedostępne z wstążki

`ColorVentSystemsCommand` (249 LOC + XAML + ikony) i `NumberingSystemElementsCommand` (280 LOC)
mają przyciski usunięte z `RibbonDefinitions` w commitach `7130e16` / `2efeef6`, ale kod nadal
jedzie w DLL. Albo wracają na wstążkę, albo do kosza — decyzja biznesowa, nie techniczna.

### 2. Revit 2027: przestarzałe API Parameter Service

`ParameterServiceService.cs:72,142,143` — `ParameterUtils.DownloadParameterOptions` i
`DownloadParameter` są deprecated w 2027 („may be removed in a later version"). Nowy overload
przyjmuje argument regionu, więc podmiana to decyzja funkcjonalna (który region Autodesk),
a nie mechaniczna. Wymaga też pierwszego w tym repo `#if` i stałej kompilacji per wersja Revita.

### 3. Brak strażnika „to jest projekt, nie rodzina"

Jest `FamilyDocumentCommandAvailability` (używa go tylko AI Family Builder) i
`AlwaysAvailableCommandAvailability` (6 narzędzi batch, które słusznie działają bez otwartego
dokumentu). Pozostałe ~40 przycisków nie ma strażnika — w dokumencie rodziny są klikalne i
zachowają się losowo (pusty wynik albo wyjątek). Warto dodać `ProjectDocumentCommandAvailability`
i wpiąć je hurtem.

### 4. Trzy kopie tej samej logiki „następny numer w projekcie"

`AccessoryTerminalNumbering`, `DuctFittingNumbering` i `ManualNumbering` mają własne
`GetNextProjectNumber` + `ExtractLastNumber`. Po optymalizacji różnią się już tylko sposobem
zawężania zbioru. Kandydat na wspólny `Infrastructure/Numbering/ProjectNumberScanner`.

### 5. Osiem pustych `catch`

`RibbonIconResolver` (3, typowane — fallback na ikonę osadzoną), `EstimateOptionsWindow`,
`Estimate`, `MassOfDuctsFittings`, `VelocitySizer`, `DuctFittingNumbering`. Wszystkie są
świadomymi fallbackami, ale pięć z nich to gołe `catch`, które połknie też `NullReferenceException`
z własnego kodu. Zawężenie do `Autodesk.Revit.Exceptions.ApplicationException` odsłoniłoby realne
błędy — kosztem tego, że część cichych degradacji zamieni się w komunikat błędu.

### 6. Param Clear, zakres „Model"

Została jedna ścieżka skanująca cały model z `LookupParameter` na element
(`ParameterClearService.cs`, `ParameterClearScope.Model`). Tu nie ma zaznaczenia, z którego można
rozwiązać id parametru, więc sztuczka z `HasValueFilterRule` wymagałaby sięgnięcia do
`document.ParameterBindings`. Wykonalne, ale to osobna robota.

### 7. AiFamilyBuilder v3 — geometria i flex testy nieprzetestowane

Największe narzędzie w repo (5334 LOC, 37 plików). Zgodnie z notatkami projektowymi geometria
parametryczna i flex testy nigdy nie zostały przeklikane w żywym Revicie. Poza zakresem tego
audytu, ale to największe skupisko ryzyka w zestawie.

## Inwentarz narzędzi

47 przycisków wstążki, 50 klas `IExternalCommand` (w tym `_Template`, niekompilowany).
Największe: AiFamilyBuilder 5334, VelocitySizer 1513, Estimate 1069, DuctFittingNumbering 982,
Splitter 960, SharedParameters 864, HvacTagging 686, CopyFilters 669, ViewFiltersLegend 656,
DuctFittingsArea 650 LOC.

## Weryfikacja

Wszystkie zmiany zbudowane w `Debug R25`, `Debug R26` i `Debug R27`. Zmiany są statyczne
(zawężenie filtrów, brak zmian w logice zapisu) — poza `Unhide All`, gdzie zmieniło się
zachowanie: narzędzie odsłania teraz tylko to, co było ukryte, i tyle raportuje. Żadna zmiana nie
została uruchomiona w żywym Revicie; przed wydaniem należy przeklikać narzędzia numeracji,
`Param Clear`, `Purge Annotations` i `Unhide All`.
