# Schedule ↔ Excel — analiza black-box MLabs "Export/Import Excel" 6.0.0.5

Data: 2026-11-25. Źródło: wyłącznie pliki wyjściowe, pliki ustawień i manifesty (bez dekompilacji).
Model testowy: `ARCH_MOKATE_F_Pakownia`, zestawienie "Zestawienie słupów konstrukcyjnych" (grupowane, nie-itemized).

## Stack (z bundla)
- `ApplicationPlugins\MlabsScheduleToExcel.bundle`, warianty NET48 (2022–24) / NET8 (2025–26) / NET10 (2027).
- ClosedXML + DocumentFormat.OpenXml (potwierdza output: prefiks `x:`, domyślne marginesy ClosedXML, `psmdcp`).
- Brak Office Interop → nie steruje Excelem, operuje wyłącznie na pliku .xlsx.
- Ustawienia per user: `%LOCALAPPDATA%\MLabs\ExportImportExcel\*.json` (ostatnia ścieżka, styl, kolory).

## 1. Zwykły eksport (one-way)
- Arkusz zbiorczy ("All schedules") + arkusz per zestawienie (`1.<nazwa>`).
- Układ 1:1 z `TableData`: tytuł scalony (`A1:C1`), nagłówki, pusty wiersz-separator, grupy, "Suma ogólna".
- **Wszystkie wartości jako tekst** (`t="s"`, także liczby) → `GetCellText`. Excel nie zsumuje liczb.
  Opcja `TryNumberFormat` istnieje w ustawieniach (domyślnie off).
- Szerokości kolumn i wysokości wierszy przeniesione z Revit; nagłówek bold + kolor; ramki Thin; wyrównanie center.
- Brak ID/metadanych → nie da się zsynchronizować z powrotem.

## 2. Dynamic Data (plik do synchronizacji) — format
Jeden arkusz, kolumna **A = kolumna techniczna** (ukryta), wiersze 1–7 = **nagłówek metadanych** (ukryte poza wierszem 3):

| Wiersz | A (etykieta) | Zawartość per kolumna pola | Widoczny |
|---|---|---|---|
| 1 | `MLabs` | nazwa parametru (EN/wewn.) np. `Base Level` | ukryty |
| 2 | `ParameterId` | `ElementId` parametru (`-1002063` = BuiltInParameter) | ukryty |
| 3 | `DataType` | np. `Type Name (Text)`, `ElementId`, `<Read Only>` | **widoczny** |
| 4 | `Unit` | jednostka (puste dla tekstu) | ukryty |
| 5 | `StorageType` | `None` / `ElementId` / `String` / `Double` / `Integer` | ukryty |
| 6 | `Modifiable?` | `Unmodifiable` / `ElementId` (= zmiana typu) / … | ukryty |
| 7 | `UnitId` | `Default` lub ForgeTypeId jednostki | ukryty |
| 8 | `ElementId` | nagłówki zestawienia (nazwy lokalne) | widoczny |
| 9+ | **lista ID elementów `id1|id2|…`** | wartości wiersza zestawienia | widoczny |

- Wiersz grupowany = wiele elementów, ID sklejone `|` w kolumnie A. Edycja wiersza → zapis do wszystkich elementów grupy.
- Kolumna `Comment` + kolumna `{ID} - {Element name}`: `(327356)-400 x 400 mm|…` — pomoc dla użytkownika.
- Legenda kolorów pod tabelą:
  - default = modyfikowalny, czerwony font `FFFF0000` = read only,
  - niebieski = parametr typu + modyfikowalny, szary = notatka, szary wiersz = niemodyfikowalny,
  - fioletowy font `FF800080` = pole pozwala zmienić typ elementu (Type → ElementId).
- Brak ochrony arkusza i walidacji danych — read-only tylko sygnalizowany kolorem, egzekwowany przy imporcie.
- Komentarz-wizytówka w komórce A1 (`comments1.xml`).
- Ścieżka ostatniego pliku zapamiętana w `DynamicDataSettingsPath.json` → sync czyta plik ponownie z dysku.

## Wnioski dla naszego narzędzia (HCB)
Bierzemy ideę (to są zasady, nie kod):
1. Metadane pola w pliku: ParameterId, StorageType, jednostka (ForgeTypeId), typ/egzemplarz, edytowalność.
2. Wiersz ↔ lista elementów (obsługa zestawień grupowanych).
3. Kolorowanie edytowalności + legenda.

Robimy lepiej:
- Metadane w **ukrytym arkuszu `_hcb_meta`** (very hidden) zamiast ukrytych wierszy — użytkownik nie rozjedzie ich sortowaniem/usuwaniem wierszy.
- ID elementów jako **UniqueId** (stabilne między sesjami/worksharing), nie ElementId.
- **Migawka wartości z eksportu** w `_hcb_meta` → wykrycie konfliktu (zmieniono w Revit po eksporcie) i import tylko faktycznych zmian (diff).
- Liczby zapisywane jako liczby (w jednostkach wyświetlanych) — Excel może liczyć.
- **Ochrona arkusza**: komórki read-only zablokowane, edytowalne odblokowane; dropdown (data validation) dla Yes/No, poziomów, typów.
- GUID dokumentu + UniqueId zestawienia w metadanych → odmowa syncu do złego modelu.
- Tryb linku: FileSystemWatcher + ExternalEvent (auto po zapisie w Excelu) jako opcja, obok przycisku "Synchronizuj".
- Raport po synchronizacji: zmienione / pominięte (read-only, konflikt, zajęte w worksharingu) / błędy.

## 3. Przebieg syncu (obserwacja użytkownika)
1. Wybór zestawienia → okno zapisu (ścieżka + nazwa) → eksport pliku Dynamic Data.
2. Excel otwiera się automatycznie (`Process.Start` na pliku).
3. Nad oknem Excela pojawia się **półprzezroczysta nakładka "tryb sync"** (okno WPF topmost, bez Interop —
   pozycjonowane względem okna Excela przez Win32, np. `GetWindowRect`).
4. **Każde Ctrl+S w Excelu = sync do Revit** → praktycznie na pewno `FileSystemWatcher` na pliku
   + `ExternalEvent` (bundle nie zawiera Office Interop).
5. Sesja trwa do zamknięcia nakładki/Excela.
6. Revit przełącza aktywny widok na eksportowane zestawienie (`UIDocument.ActiveView`/`RequestViewChange`) —
   użytkownik widzi zmiany na żywo obok Excela.

Pomiar okien (EnumWindows, sesja aktywna):
- Nakładka = okno WPF `NoteBox` w procesie **Revit**, owner = główne okno Revit,
  exStyle `0x80028` = `WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED` → zawsze na wierzchu,
  półprzezroczysta i **przepuszcza kliknięcia** do Excela. Ustawiona na monitorze Excela (604×265 px).
- Plik w momencie startu sesji jest **generowany ponownie przez ClosedXML** (nowy `psmdcp`), potem otwierany w Excelu.
- Excel trzyma plik z blokadą zapisu: `Get-FileHash` (FileShare.Read) → błąd "używany przez inny proces";
  odczyt działa tylko przez `File.Open(path, Open, Read, FileShare.ReadWrite)` → nasz reader musi tak otwierać / kopiować.

Nasza implementacja: identyczny model (watcher + ExternalEvent + nakładka/okno statusu), plus:
debounce zapisów Excela (temp-file + rename), odczyt kopii z `FileShare.ReadWrite`, diff vs migawka,
licznik/raport ostatniego syncu w nakładce, automatyczne zakończenie przy zamknięciu Excela lub dokumentu.

## 4. Incydent: zawieszenie Revit podczas sesji MLabs (2026-11-25)
- Model: `ARCH_MOKATE_F_Pakownia` — **model chmurowy ACC** (`CLD:\\EMEA\...`), Revit 2025.4, otwarte 3 modele chmurowe.
- 11:14:32 `Jrn.Activate` zestawienia, 11:14:35 ostatni wpis journala (start sesji sync / ponowny eksport pliku).
- 11:17:27 Windows: `AppHangB1` Revit.exe 25.4.60.9 → użytkownik musiał zabić proces.
- Kod MLabs nieznany → przyczyna niepotwierdzona. Hipotezy: blokujące operacje na wątku UI Revit
  (czekanie na plik/Excel, pętla odpytująca) lub synchroniczne wypożyczanie elementów w modelu chmurowym.
- Wniosek: nasza implementacja musi zakładać model chmurowy + Excel blokujący plik + wiele zapisów Ctrl+S.

## 5. Zasady niezawodności (obowiązkowe dla HCB ScheduleExcel)
Wątki
1. **Zero pracy na wątku UI Revit poza krótką transakcją.** Watcher → wątek tła: odczyt pliku, parsowanie, diff.
   Do Revit trafia gotowa lista zmian przez `ExternalEvent.Raise()`. Handler: tylko zapis parametrów + commit.
2. **Każdy callback tła w `try/catch` + log.** Nieobsłużony wyjątek na wątku tła zabija cały proces Revit.
3. Nakładka na **własnym wątku STA z własnym Dispatcherem** — nie zależy od zajętości Revit, nie blokuje go.
   Flagi okna: `WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`
   (bez kradzieży fokusu, bez wpisu na pasku zadań, przepuszcza kliknięcia). Przycisk Stop → osobne małe okno
   bez `TRANSPARENT` albo pozycja w ribbon/oknie statusu.
4. Nigdy `Thread.Sleep`/`Wait()`/`.Result` na wątku Revit. Nigdy modalny dialog z wątku tła.

Plik
5. Odczyt: `FileStream(path, Open, Read, FileShare.ReadWrite | FileShare.Delete)` → `MemoryStream` → ClosedXML.
   Retry 5× z backoffem 200–1000 ms na `IOException`. Nie trzymać uchwytu do pliku.
6. **Debounce 1,5 s** + koalescencja (Excel: temp-file → rename → kilka zdarzeń). Single-flight: gdy sync trwa,
   zapamiętaj max 1 oczekujący. Porównanie hasha pliku — ten sam hash = brak akcji.
7. Watcher na katalogu z filtrem nazwy, zdarzenia `Changed|Renamed|Created`; obsługa `Error` (overflow) → restart.
8. **Nigdy nie zapisujemy pliku, gdy Excel go ma otwartego** (brak „odświeżania” pliku w trakcie sesji).

Revit
9. Przed transakcją: dokument nadal otwarty, `!IsReadOnly`, `IsModifiable == false` (brak otwartej transakcji),
   GUID dokumentu i UniqueId zestawienia zgodne z `_hcb_meta`. Inaczej: odrzuć + komunikat w nakładce.
10. Worksharing / ACC: jedno zbiorcze `WorksharingUtils.CheckoutElements` dla zmienianych elementów;
    elementy zajęte przez innych → pomiń i raportuj. Komunikat „sprawdzanie własności (chmura)…” w nakładce.
11. Jedna `Transaction` na zapis, nazwa `HCB Excel Sync HH:mm:ss` (Ctrl+Z cofa cały sync);
    `IFailuresPreprocessor`: ostrzeżenia usuwane, błędy → `RollBack`, raport.
12. Bez `Regenerate()` per element; zero aktywacji widoków w trakcie syncu (aktywacja zestawienia tylko raz na start).
13. Limit bezpieczeństwa: > 500 zmian w jednym zapisie → pytanie w nakładce przed zapisem.
14. Wartości: `UnitFormatUtils.TryParse` dla Double; niepoprawne → pomiń + raport, nigdy wyjątek.

Cykl życia
15. Koniec sesji: Stop, zamknięcie Excela (proces/okno z nazwą pliku znika), `DocumentClosing`,
    `ApplicationClosing`, bezczynność > 2 h. Sprzątanie: watcher `Dispose`, ExternalEvent `Dispose`, nakładka zamknięta.
16. Max 1 aktywna sesja na plik; max kilka sesji równolegle (lista w oknie statusu).
17. Log sesji: `%LOCALAPPDATA%\HCB\ScheduleExcel\logs\yyyyMMdd_HHmmss.log` (każdy sync: hash, liczba zmian, czas, błędy).

Zależności
18. MLabs ładuje do Revit 2025 **ClosedXML 0.105.0 + DocumentFormat.OpenXml 3.1.1** (wspólny kontekst ładowania .NET 8
    — pierwsza załadowana wersja wygrywa). Używamy **tych samych wersji** albo izolujemy warstwę Excel
    we własnym `AssemblyLoadContext`. Test: Revit z włączonym MLabs + nasz dodatek.

## Otwarte
- Zachowanie dla parametru typu w wierszu z mieszanymi typami, dla jednostek (Double) — potrzebny test na zestawieniu itemized z liczbami.
