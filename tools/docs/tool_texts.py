"""Polish user-facing texts for the tools documentation (README).

Key = button / pulldown id from RibbonDefinitions.cs.
A tool without an entry here still appears in the README, using its ribbon tooltip.

Fields:
  what   - one or two sentences: what the tool does
  steps  - how to use it, in order
  scope  - what it works on (selection / view / model / files)
  note   - optional extra (rendered as a GitHub alert), kind in note_kind: NOTE | TIP | WARNING | IMPORTANT
  tags   - short badges shown under the title
"""

PANELS = {
    "Modify": ("✏️", "Filtry widoków, poziomy, seryjna zmiana nazw i numeracja elementów."),
    "HVAC": ("🌬️", "Narzędzia instalacyjne: przestrzenie, przepływy, dobór średnic, tagowanie, masy, powierzchnie i kosztorys."),
    "MEP Modify": ("🔧", "Szybka edycja tras: obracanie kształtek, łączenie, przedłużanie i wyrównywanie elementów."),
    "Views": ("🖼️", "Praca z widokami: odkrywanie, duplikowanie, szablony, legendy filtrów, zestawienia w Excelu i grafika linków CAD."),
    "WithoutOpen": ("📦", "Operacje wsadowe na plikach .rvt / .rfa bez otwierania ich w interfejsie Revita."),
    "Manage": ("⚙️", "Parametry projektu, kopiowanie między projektami, porządki i zarządzanie wstążką."),
}

PULLDOWNS = {
    "HcbFiltersPulldown": "Lista rozwijana z narzędziami do filtrów widoków i szablonów.",
    "HcbRenamePulldown": "Lista rozwijana z narzędziami do seryjnej zmiany nazw widoków i arkuszy.",
    "HcbNumberingPulldown": "Lista rozwijana z narzędziami do numeracji. Z zaznaczeniem pracują tylko na nim, bez zaznaczenia zbierają elementy z modelu.",
    "HcbSpacesPulldown": "Lista rozwijana z narzędziami do przestrzeni MEP i parametrów LIN_ROOM_*.",
    "HcbHvacToolsPulldown": "Lista rozwijana z pomocniczymi narzędziami HVAC: kąty, poziomy, systemy i masa.",
    "HcbAreaPulldown": "Lista rozwijana z obliczaniem powierzchni kształtek (HC_Area) dla rodzin LINEAR i MagiCAD.",
    "HcbScheduleExcelPulldown": "Lista rozwijana z eksportem zestawień do Excela, synchronizacją na żywo i importem zmian z pliku.",
    "HcbTemplatesPulldown":"Lista rozwijana z transferem szablonów widoków i zestawień między otwartymi projektami.",
    "HcbWithoutOpenPulldown": "Lista rozwijana z narzędziami wsadowymi. Pliki są otwierane w tle albo wcale, bez pokazywania w UI.",
    "HcbParametersPulldown": "Lista rozwijana z narzędziami do sprawdzania, uzupełniania i przeliczania parametrów.",
}

T = {
    # ---------------- Modify ----------------
    "HcbCopyFiltersButton": dict(
        what="Kopiuje wybrane filtry z jednego widoku lub szablonu widoku do wielu innych widoków i szablonów, razem z ich nadpisaniami grafiki.",
        steps=["Wskaż widok lub szablon źródłowy.", "Zaznacz filtry i widoki docelowe.", "Potwierdź kopiowanie i rozstrzygnij konflikty."],
        scope="Widoki i view templates w bieżącym projekcie.",
        tags=["Widoki", "View Templates"],
    ),
    "HcbViewFiltersBulkEditButton": dict(
        what="Masowo zmienia stan wspólnych filtrów na wielu widokach naraz: włącza, wyłącza albo ukrywa.",
        steps=["Wybierz widoki.", "Zaznacz filtry wspólne dla wybranych widoków.", "Ustaw akcję (ON, OFF, ukrycie) i zatwierdź."],
        scope="Wiele widoków i szablonów jednocześnie.",
        tags=["Widoki"],
    ),
    "HcbLevelsButton": dict(
        what="Dopisuje do nazw poziomów ich rzędną w metrach w nawiasie, np. `Poziom 1 (+3.20)`, i aktualizuje już istniejące oznaczenia.",
        steps=["Uruchom polecenie w projekcie z poziomami.", "Ustaw liczbę miejsc po przecinku i znak `+` dla wartości dodatnich, sprawdź podgląd.", "Kliknij *Aktualizuj poziomy*."],
        scope="Wszystkie poziomy w projekcie.",
        note="Po zmianie wysokości poziomu uruchom narzędzie ponownie, aby odświeżyć rzędne w nazwach.",
        note_kind="TIP",
        tags=["Model", "Podgląd zmian"],
    ),
    "HcbRenameViewsButton": dict(
        what="Seryjnie zmienia nazwy wybranych widoków: prefiks, zamiana tekstu i sufiks, z podglądem przed zapisem.",
        steps=["Wybierz widoki.", "Ustaw prefiks, zamianę tekstu i/lub sufiks.", "Sprawdź podgląd i zatwierdź."],
        scope="Tylko wybrane widoki.",
        tags=["Podgląd zmian"],
    ),
    "HcbRenameSheetsButton": dict(
        what="Seryjnie zmienia nazwy i numery arkuszy, z osobnymi regułami dla numeru i dla nazwy.",
        steps=["Wybierz arkusze.", "Ustaw zasady osobno dla numeru i nazwy.", "Sprawdź podgląd i zatwierdź."],
        scope="Tylko wybrane arkusze.",
        tags=["Podgląd zmian"],
    ),
    "HcbManualNumberingButton": dict(
        what="Numeruje elementy ręcznie, w kolejności, w jakiej je klikasz. Daje pełną kontrolę nad kolejnością numerów.",
        steps=["Zaznacz elementy.", "Wybierz parametr docelowy i format numeru.", "Klikaj elementy w zadanej kolejności."],
        scope="Wskazane elementy.",
        tags=["Zaznaczenie"],
    ),
    "HcbDuctFittingNumberingButton": dict(
        what="Numeruje kanały i kształtki kanałowe według kluczy grupujących i System Abbreviation. Identyczne elementy dostają wspólny numer.",
        steps=["Wybierz parametr docelowy.", "Ustaw numer startowy i opcjonalnie parametr długości.", "Zatwierdź numerację."],
        scope="Zaznaczone ducts i fittings, a bez zaznaczenia wszystkie z modelu.",
        note="Grupy z różnymi System Abbreviation dostają numer bez prefiksu systemu.",
        tags=["Zaznaczenie / model", "Ducts", "Duct Fittings"],
    ),
    "HcbDuctNumberingButton": dict(
        what="Numeruje tylko kanały (ducts) według kluczy grupujących i System Abbreviation.",
        steps=["Wybierz parametr docelowy.", "Ustaw numer startowy i sposób grupowania.", "Zatwierdź numerację."],
        scope="Zaznaczone ducts, a bez zaznaczenia wszystkie obsługiwane ducts z projektu.",
        note="Grupy z różnymi System Abbreviation dostają numer bez prefiksu systemu.",
        tags=["Zaznaczenie / model", "Ducts"],
    ),
    "HcbDuctFittingsOnlyNumberingButton": dict(
        what="Numeruje tylko kształtki kanałowe. Grupuje je po wymiarach LIN_VE, kącie i System Abbreviation.",
        steps=["Wybierz parametr docelowy.", "Ustaw numer startowy.", "Zatwierdź numerację."],
        scope="Zaznaczone duct fittings, a bez zaznaczenia wszystkie obsługiwane kształtki z projektu.",
        note="Grupy mieszane systemowo dostają numer bez prefiksu systemu.",
        tags=["Zaznaczenie / model", "Duct Fittings"],
    ),
    "HcbAccessoryTerminalNumberingButton": dict(
        what="Numeruje Duct Accessory w formacie `TypeMark.HC_System.KolejnyNumer`. Elementy o tym samym układzie w obrębie jednego HC_System dostają wspólny numer.",
        steps=["Wybierz parametr docelowy.", "Ustaw numer startowy albo kontynuuj od ostatniego numeru w projekcie.", "Zatwierdź numerację."],
        scope="Zaznaczone Duct Accessory, a bez zaznaczenia wszystkie z projektu.",
        note="Typy FabricAir są pomijane.",
        tags=["Zaznaczenie / model", "Duct Accessories"],
    ),
    # ---------------- HVAC ----------------
    "HcbSpaceParamLinkedButton": dict(
        what="Uzupełnia parametry `LIN_ROOM_*` elementów na podstawie pomieszczeń (rooms) z podlinkowanego modelu architektury.",
        steps=["Uruchom narzędzie w projekcie z linkiem architektury.", "Wskaż lub potwierdź link.", "Zapisz wynik."],
        scope="Głównie Mechanical Equipment.",
        tags=["Link RVT", "LIN_ROOM_*"],
    ),
    "HcbSpaceToElementButton": dict(
        what="Uzupełnia parametry `LIN_ROOM_*` na podstawie lokalnych przestrzeni MEP (spaces) w modelu.",
        steps=["Otwórz odpowiedni widok.", "Uruchom narzędzie.", "Zapisz wynik."],
        scope="Wszystkie spaces w modelu oraz elementy obsługiwanych kategorii.",
        note="Alternatywa dla Space Params, gdy pracujesz na spaces, a nie na rooms z linku.",
        tags=["Spaces", "LIN_ROOM_*"],
    ),
    "HcbSpaceCleanupButton": dict(
        what="Pokazuje listę wszystkich przestrzeni MEP z nazwą, numerem, poziomem i statusem. Usuwa wybrane przestrzenie razem z ich znacznikami.",
        steps=["Wpisz frazę z nazwy, np. `Wolny`.", "Ustaw filtr statusu (nieumieszczona, nieobwiedziona, nadmiarowa) i ewentualnie poziom.", "Kliknij *Zaznacz wszystkie*, potem *Usuń wybrane* i potwierdź."],
        scope="Lokalne spaces w modelu.",
        note="Całość wykonuje się w jednej transakcji, więc możesz ją cofnąć przez `Ctrl+Z`.",
        note_kind="TIP",
        tags=["Spaces", "Usuwanie"],
    ),
    "HcbColorUniqueSystemsButton": dict(
        what="Nadaje kolory wybranym systemom wentylacyjnym i rurowym jako nadpisania grafiki, według konfiguracji z pliku CSV.",
        steps=["Uruchom narzędzie w aktywnym widoku.", "Wybierz systemy z listy (plik CSV).", "Zatwierdź nadpisania."],
        scope="Tylko aktywny widok.",
        tags=["Aktywny widok", "CSV"],
    ),
    "HcbFlowChangerButton": dict(
        what="Losowo różnicuje przepływy na terminalach w aktywnym widoku. Służy do szybkiego testowania wariantów przepływu.",
        steps=["Uruchom narzędzie w aktywnym widoku.", "Wybierz parametr docelowy i zakres zmiany.", "Zatwierdź."],
        scope="Terminale w aktywnym widoku.",
        tags=["Aktywny widok", "Air Terminals"],
    ),
    "HcbVelocitySizerButton": dict(
        what="Dobiera średnice okrągłych kanałów i rur do przepływu. Pokazuje tabelę standardowych średnic z projektu i prędkości dla każdej z nich.",
        steps=["Zaznacz okrągłe kanały lub rury.", "Wpisz przepływ dla każdego wiersza.", "Wybierz średnicę z tabeli i zatwierdź."],
        scope="Zmienia średnicę elementu i zapisuje `LIN_VE_VELOCITY` oraz `LIN_VE_FLOWRATE_SUM`.",
        note="Gdy parametry mają już wartości, narzędzie pyta o nadpisanie.",
        tags=["Zaznaczenie", "Ducts", "Pipes"],
    ),
    "HcbHvacTaggingButton": dict(
        what="Wyszukuje brakujące tagi elementów HVAC i tworzy je po zatwierdzeniu. Wcześniej pokazuje podgląd tagów brakujących, istniejących i zduplikowanych.",
        steps=["Otwórz widok albo arkusz.", "Uruchom narzędzie i przejrzyj podgląd.", "Zatwierdź tworzenie brakujących tagów."],
        scope="Aktywny widok albo viewporty aktywnego arkusza. Obejmuje ducts, flex ducts, duct fittings, duct accessories i duct terminals.",
        note="Używa tylko typów tagów wczytanych już do projektu.",
        tags=["Aktywny widok / arkusz", "Tagi"],
    ),
    "HcbFittingsAngleButton": dict(
        what="Kopiuje kąty kształtek do parametru `HC_Kat` i je zaokrągla.",
        steps=["Zaznacz kształtki albo pozwól narzędziu pobrać obsługiwane elementy.", "Uruchom polecenie.", "Zapisz wynik."],
        scope="Prostokątne kształtki kanałowe zaokrągla do 1°, pozostałe do 5°.",
        tags=["Zaznaczenie / model", "HC_Kat"],
    ),
    "HcbLevelFromHvacButton": dict(
        what="Kopiuje Level albo Reference Level elementu do wybranego parametru instancji.",
        steps=["Zaznacz elementy HVAC.", "Wybierz parametr docelowy.", "Zatwierdź."],
        scope="Tylko zaznaczenie. Obsługuje parametry typu String i ElementId.",
        tags=["Zaznaczenie"],
    ),
    "HcbSystemAssignerButton": dict(
        what="Przenosi wartość parametru (np. `HC_System`) z urządzenia na wszystkie elementy przypisanych do niego systemów.",
        steps=["Zaznacz Mechanical Equipment.", "Wybierz parametr do przeniesienia.", "Zatwierdź."],
        scope="Poprawnie połączone systemy przypisane do wskazanych urządzeń.",
        tags=["Zaznaczenie", "HC_System"],
    ),
    "HcbMassOfDuctsFittingsButton": dict(
        what="Oblicza masę (`HC_Masa`) kanałów, kształtek i akcesoriów wentylacyjnych.",
        steps=["Wybierz parametr docelowy, jeśli narzędzie o to poprosi.", "Uruchom obliczenie.", "Zapisz wynik."],
        scope="Zaznaczenie, a bez zaznaczenia obsługiwane elementy wentylacyjne z projektu.",
        tags=["Zaznaczenie / model", "HC_Masa"],
    ),
    "HcbEstimateButton": dict(
        what="Wylicza ceny jednostkowe i koszty elementów wentylacji według pliku cennika.",
        steps=["Wskaż plik cennika albo użyj domyślnej konfiguracji.", "Uruchom przeliczenie.", "Sprawdź raport dopasowań."],
        scope="Zapisuje `HC_Cena_Jednostkowa` i `HC_Koszt` dla obsługiwanych elementów wentylacji.",
        tags=["Model", "Kosztorys"],
    ),
    "HcbSplitterButton": dict(
        what="Dzieli proste kanały i rury na odcinki produkcyjne o zadanej długości i wstawia między nimi łączniki.",
        steps=["Wpisz długość produkcyjną i wybierz ducts, pipes albo oba typy.", "Klikaj elementy bliżej strony, od której ma zacząć się podział.", "`ESC` kończy."],
        scope="Podział zaczyna się od wskazanej strony, reszta zostaje na przeciwnym końcu.",
        note="Kliknięcie już pociętego ciągu przebudowuje go od najbliższego fittingu lub accessory i przelicza rozstaw od nowa.",
        note_kind="TIP",
        tags=["Wskazywanie", "Ducts", "Pipes"],
    ),
    "HcbDuctFittingsAreaLinearButton": dict(
        what="Oblicza powierzchnię kształtek wentylacyjnych z rodzin LINEAR i zapisuje ją do `HC_Area`.",
        steps=["Uruchom narzędzie dla rodzin przygotowanych pod LINEAR.", "Wybierz zakres albo ustawienia.", "Zapisz wynik."],
        scope="Raport i zapis do `HC_Area`.",
        tags=["LINEAR", "HC_Area"],
    ),
    "HcbDuctFittingsAreaMagicadButton": dict(
        what="Oblicza powierzchnię kształtek wentylacyjnych z rodzin MagiCAD i zapisuje ją do `HC_Area`.",
        steps=["Uruchom narzędzie dla rodzin przygotowanych pod MagiCAD.", "Wybierz zakres albo ustawienia.", "Zapisz wynik."],
        scope="Raport i zapis do `HC_Area`.",
        tags=["MagiCAD", "HC_Area"],
    ),
    # ---------------- MEP Modify ----------------
    "HcbRotateFittingsButton": dict(
        what="Obraca klikane kształtki o wybrany kąt wokół osi podłączonego konektora albo wokół osi pionowej. Okno zostaje otwarte obok Revita.",
        steps=["Kliknij kąt (±45°, 90°, 135°, 180°) albo wpisz własny i naciśnij `Enter`.", "Klikaj kształtki. Każde kliknięcie obraca o zadany kąt.", "`ESC` wstrzymuje wskazywanie, *Zamknij* kończy pracę."],
        scope="Duct, Pipe i Conduit Fittings.",
        note="Połączenia na obracanych końcach są rozłączane.",
        note_kind="WARNING",
        tags=["Okno niemodalne", "Fittings"],
    ),
    "HcbConnectElementsButton": dict(
        what="Dosuwa i obraca drugi element razem ze wszystkim, co jest do niego podłączone, tak aby jego wolny konektor pokrył się z konektorem pierwszego. Następnie je łączy.",
        steps=["Kliknij element, **do którego** łączysz (blisko konektora).", "Kliknij element, **który** ma zostać dosunięty (blisko wolnego konektora).", "Powtarzaj pary, `ESC` kończy."],
        scope="Elementy z konektorami tej samej dziedziny. Rozmiary i typy nie są dopasowywane.",
        note="Przesuwana grupa może mieć do 500 elementów i nie może być już połączona z elementem docelowym.",
        tags=["Wskazywanie", "Konektory"],
    ),
    "HcbConnectorExtendButton": dict(
        what="Z klikniętego wolnego konektora wstawia prosty odcinek 500 mm w jego kierunku i rozmiarze, od razu podłączony.",
        steps=["Klikaj elementy blisko wolnych konektorów (kolano, trójnik, koniec kanału, urządzenie).", "`ESC` kończy."],
        scope="Ducts, pipes, conduits. Typ i system pochodzą z sąsiedniego odcinka, a gdy go brak, z pierwszego pasującego typu w projekcie.",
        tags=["Wskazywanie", "500 mm"],
    ),
    "HcbAlignToAxisButton": dict(
        what="Przesuwa rurę, kanał albo kształtkę tak, aby jej oś przecięła oś odcinka odniesienia. Elementy równoległe stają się współliniowe.",
        steps=["Kliknij prosty odcinek odniesienia.", "Klikaj dochodzące elementy, np. prostopadłą rurę.", "`ESC` kończy."],
        scope="Ducts, pipes, conduits i elementy z konektorami.",
        note="Przesunięcie odbywa się tylko w poprzek obu osi, więc istniejące połączenia zostają.",
        note_kind="TIP",
        tags=["Wskazywanie", "Bez rozłączania"],
    ),
    "HcbElbowUpButton": dict(
        what="Na klikniętym wolnym konektorze wstawia kolano i prosty odcinek 500 mm skierowany w górę (globalna oś Z).",
        steps=["Klikaj elementy blisko wolnych konektorów.", "`ESC` kończy.", "Kierunek możesz potem zmienić narzędziem Rotate Fittings."],
        scope="Ducts, pipes, conduits. Kolano pochodzi z routing preferences typu odcinka.",
        note="Na końcu kanału kolano zajmuje jego końcówkę. Na kształtce siada bezpośrednio na konektorze.",
        tags=["Wskazywanie", "Kolano 90°"],
    ),
    "HcbElbow45Button": dict(
        what="Na klikniętym wolnym konektorze wstawia kolano 45° i odcinek 500 mm odchylony o 45° w prawo (patrząc wzdłuż konektora z góry).",
        steps=["Klikaj elementy blisko wolnych konektorów.", "`ESC` kończy.", "Inny kierunek ustawisz narzędziem Rotate Fittings."],
        scope="Ducts, pipes, conduits. Dla pionowego konektora odchylenie jest w stronę globalnej osi X.",
        tags=["Wskazywanie", "Kolano 45°"],
    ),
    # ---------------- Views ----------------
    "HcbUnhideAllElementsButton": dict(
        what="Odkrywa jednym kliknięciem wszystkie elementy ukryte w aktywnym widoku.",
        steps=["Uruchom polecenie w bieżącym widoku."],
        scope="Tylko elementy ukryte poleceniem *Hide in View* w aktywnym widoku.",
        tags=["Jedno kliknięcie", "Aktywny widok"],
    ),
    "HcbViewsDuplicateButton": dict(
        what="Duplikuje wiele widoków naraz, w wybranej liczbie kopii i trybie duplikacji.",
        steps=["Wybierz widoki.", "Ustaw liczbę kopii i tryb: Duplicate, WithDetailing albo AsDependent.", "Zatwierdź."],
        scope="Tworzy serie kopii wybranych widoków.",
        tags=["Widoki"],
    ),
    "HcbTransferViewTemplatesButton": dict(
        what="Kopiuje wybrane szablony widoków (view templates) między otwartymi projektami.",
        steps=["Otwórz projekt źródłowy i docelowy.", "Wybierz view templates do skopiowania.", "Zdecyduj o nadpisaniu istniejących pozycji."],
        scope="Synchronizacja standardu widoków, bez szablonów zestawień.",
        tags=["2 projekty", "View Templates"],
    ),
    "HcbTransferScheduleTemplatesButton": dict(
        what="Kopiuje wybrane szablony zestawień między otwartymi projektami.",
        steps=["Otwórz projekt źródłowy i docelowy.", "Wybierz szablony zestawień do skopiowania.", "Zdecyduj o nadpisaniu istniejących pozycji."],
        scope="Synchronizacja standardu zestawień między modelami.",
        tags=["2 projekty", "Schedules"],
    ),
    "HcbScheduleExcelSyncButton": dict(
        what="Otwiera jedno lub kilka zestawień w Excelu (każde na osobnym arkuszu) i utrzymuje synchronizację na żywo. Każde `Ctrl+S` w Excelu zapisuje do Revita tylko zmienione komórki.",
        steps=["Zaznacz zestawienia i ustaw opcje edycji: blokada komórek tylko do odczytu, każdy element w osobnym wierszu, kolumna ID.", "Edytuj wartości w Excelu. Typ, poziom czy materiał wybierzesz z listy rozwijanej.", "Naciśnij `Ctrl+S`. Zmiany trafiają do Revita od razu."],
        scope="Zestawienia elementów: parametry egzemplarza i typu, zmiana typu elementu, liczby w jednostkach projektu. Każdy zapis to jedna transakcja, którą cofa jedno `Ctrl+Z`.",
        note="Konflikty (wartość zmieniona w Revicie po eksporcie) i elementy zajęte przez innych użytkowników są pomijane i raportowane. Sesja kończy się po zamknięciu pliku, modelu albo przyciskiem *Stop*.",
        tags=["Excel", "Synchronizacja na żywo"],
    ),
    "HcbScheduleExcelExportButton": dict(
        what="Eksportuje zestawienia do pliku `.xlsx` z tytułem, grupami, kolorami i sumami dokładnie tak, jak wyglądają w Revicie. Liczby trafiają do Excela jako liczby w jednostkach projektu.",
        steps=["Zaznacz jedno lub kilka zestawień (domyślnie aktywne albo zaznaczone).", "Wskaż folder. Opcjonalnie: każde zestawienie do osobnego pliku, otwórz plik po eksporcie.", "Kliknij *Eksportuj*."],
        scope="Kilka zestawień trafia na osobne arkusze jednego pliku albo do osobnych plików. Ukryte arkusze pamiętają, które elementy tworzą każdy wiersz, i wartości z chwili eksportu.",
        note="Plik możesz później edytować i wczytać z powrotem poleceniem Import from Excel.",
        note_kind="TIP",
        tags=["Excel", ".xlsx"],
    ),
    "HcbScheduleExcelImportButton": dict(
        what="Wczytuje zmiany z wcześniej wyeksportowanego pliku Excel. Porównuje plik z wartościami z chwili eksportu i zapisuje do Revita tylko zmienione komórki.",
        steps=["Wskaż plik `.xlsx` wyeksportowany z tego modelu.", "Sprawdź listę zmian.", "Potwierdź zapis."],
        scope="Tylko kolumny edytowalne, wszystkie zestawienia z pliku naraz, w jednej transakcji.",
        note="Konflikty i elementy zajęte przez innych użytkowników są pomijane i raportowane.",
        tags=["Excel", "Podgląd zmian"],
    ),
    "HcbViewFiltersLegendButton": dict(
        what="Tworzy widok legendy z próbkami grafiki i opisami filtrów przypisanych do wybranych widoków i szablonów.",
        steps=["Wybierz widoki albo view templates.", "Uruchom polecenie.", "Narzędzie utworzy nową legendę."],
        scope="Generuje nowy widok legendy.",
        tags=["Legenda"],
    ),
    "HcbCadLinkGraphicsCopyButton": dict(
        what="Kopiuje ustawienia widoczności i nadpisania warstw z jednego linku CAD do drugiego.",
        steps=["Uruchom narzędzie w aktywnym widoku.", "Wybierz link źródłowy i docelowy.", "Zatwierdź kopiowanie."],
        scope="Aktywny widok. Warstwy są dopasowywane po nazwach subkategorii.",
        tags=["Aktywny widok", "DWG"],
    ),
    # ---------------- WithoutOpen ----------------
    "HcbBatchFileScanButton": dict(
        what="Skanuje pliki .rvt i .rfa bez otwierania ich w UI i tworzy raport: wersja Revita, typ pliku, worksharing i przydatność do operacji wsadowych.",
        steps=["Wybierz pliki albo folder.", "Uruchom skanowanie.", "Sprawdź raport."],
        scope="Dowolne pliki .rvt / .rfa. Działa także bez otwartego projektu.",
        tags=["Bez otwierania", ".rvt", ".rfa"],
    ),
    "HcbUnloadLinksButton": dict(
        what="Ustawia linki zewnętrzne lokalnych projektów jako niezaładowane przy następnym otwarciu, bez otwierania modelu.",
        steps=["Wybierz lokalne pliki .rvt.", "Uruchom polecenie.", "Zapisz wynik."],
        scope="Wykorzystuje TransmissionData, projekt nie jest otwierany w UI.",
        note="Przydaje się, gdy ciężkie linki spowalniają otwieranie modelu.",
        note_kind="TIP",
        tags=["Bez otwierania", ".rvt"],
    ),
    "HcbFamilyParameterReportButton": dict(
        what="Odczytuje parametry wybranych rodzin .rfa, otwierając je w tle, i tworzy raport CSV.",
        steps=["Wybierz rodziny.", "Uruchom raport.", "Odbierz plik CSV."],
        scope="Szybka analiza wielu rodzin bez ręcznego otwierania każdej z nich.",
        tags=["W tle", ".rfa", "CSV"],
    ),
    "HcbViewerExportButton": dict(
        what="Zapisuje wybrane modele ACC razem z ich linkami RVT do lokalnego folderu, gotowe do Autodesk Viewer. Otwiera w trybie detach, robi Purge Unused w pętli i przepina linki na lokalne kopie.",
        steps=["Wybierz modele główne z listy (cache Revita).", "Wskaż folder docelowy, np. OneDrive.", "Sprawdź raport."],
        scope="Modele ACC otwierane wcześniej w tej wersji Revita. Niczego nie zapisuje do ACC.",
        note="Istniejące pliki w folderze docelowym są nadpisywane.",
        note_kind="WARNING",
        tags=["W tle", "ACC", "Viewer"],
    ),
    "HcbUpgradeAndCopyButton": dict(
        what="Aktualizuje pliki .rvt i .rfa do wersji uruchomionego Revita i zapisuje kopie we wskazanym folderze.",
        steps=["Wybierz pliki i folder docelowy.", "Uruchom upgrade.", "Sprawdź wynik."],
        scope="Starsze pliki są aktualizowane, a pliki już aktualne tylko kopiowane. Oryginały zostają nietknięte.",
        tags=["W tle", "Upgrade"],
    ),
    "HcbBatchAddSharedFamilyParametersButton": dict(
        what="Dodaje wybrane shared parameters z pliku do wielu rodzin .rfa naraz. Rodziny są otwierane w tle.",
        steps=["Wybierz plik Shared Parameters i rodziny.", "Zaznacz definicje, ustaw Instance albo Type i grupę.", "Zapisz zmiany."],
        scope="Wsadowo, bez otwierania rodzin w UI.",
        tags=["W tle", ".rfa", "Shared Parameters"],
    ),
    "HcbRenameFamilyContentButton": dict(
        what="Zmienia nazwy edytowalnych parametrów rodzinnych w wielu plikach .rfa według wspólnych reguł.",
        steps=["Wybierz rodziny .rfa.", "Ustaw reguły zmiany nazw.", "Zapisz wynik."],
        scope="Działa w tle. Pomija parametry shared, systemowe i kolizje nazw.",
        tags=["W tle", ".rfa"],
    ),
    # ---------------- Manage ----------------
    "HcbSharedParametersButton": dict(
        what="Sprawdza, czy w projekcie są wymagane parametry współdzielone `HC_*`, i dodaje brakujące do odpowiednich kategorii.",
        steps=["Ustaw poprawny plik Shared Parameters.", "Uruchom polecenie.", "Zapisz wynik."],
        scope="Odpowiednie kategorie projektu.",
        tags=["HC_*", "Shared Parameters"],
    ),
    "HcbProjectParameterAuditButton": dict(
        what="Diagnozuje konfliktowe parametry projektu BimObject (`Nominal width`, `Nominal height`, `Weight Net (Kg)`): typ danych, GUID, bindingi, kategorie, wartości, zestawienia i filtry.",
        steps=["Uruchom w projekcie.", "Przefiltruj raport.", "Wyeksportuj CSV przed decyzją o usunięciu albo migracji."],
        scope="Parametry projektu i ich użycie w modelu.",
        tags=["Raport", "CSV"],
    ),
    "HcbParameterServiceButton": dict(
        what="Importuje predefiniowane parametry z Autodesk Parameter Service. Brakujące parametry dodaje z domyślnymi kategoriami, bindingiem Instance/Type i grupą.",
        steps=["Uzupełnij listę identyfikatorów parametrów w kodzie.", "Uruchom polecenie.", "Brakujące parametry zostaną dodane automatycznie."],
        scope="Dokument projektu. Wymaga zalogowania do Autodesk i dostępu do parametrów.",
        note="Dostępne tylko w Revit 2026 i nowszych.",
        note_kind="IMPORTANT",
        tags=["Revit 2026+", "Autodesk Cloud"],
    ),
    "HcbParameterCalculatorButton": dict(
        what="Wykonuje działania matematyczne na co najmniej dwóch parametrach liczbowych (Double / Integer) i zapisuje wynik do wybranego parametru.",
        steps=["Opcjonalnie zaznacz elementy.", "Wybierz parametry źródłowe, operator i parametr docelowy.", "Ustaw liczbę miejsc po przecinku i uruchom obliczenie."],
        scope="Zaznaczenie, a bez zaznaczenia cały model. Elementy bez wszystkich wybranych parametrów są pomijane.",
        tags=["Zaznaczenie / model"],
    ),
    "HcbParameterClearButton": dict(
        what="Czyści wartości wybranego parametru tekstowego.",
        steps=["Opcjonalnie zaznacz elementy.", "Wybierz zakres, parametr i ewentualnie kategorię.", "Potwierdź wyczyszczenie."],
        scope="Zaznaczenie, wybrana kategoria albo cały model, dla elementów z tym parametrem.",
        tags=["Zaznaczenie / kategoria / model"],
    ),
    "HcbParameterCopyButton": dict(
        what="Kopiuje wartość z jednego parametru do drugiego na elementach instalacji.",
        steps=["Opcjonalnie zaznacz elementy instalacji.", "Wybierz zakres, parametr źródłowy i docelowy.", "Potwierdź nadpisanie."],
        scope="Zaznaczenie albo wszystkie obsługiwane elementy instalacji. Lista docelowa pokazuje tylko parametry o zgodnym typie danych.",
        tags=["Zaznaczenie / model", "MEP"],
    ),
    "HcbCopyBetweenProjectsButton": dict(
        what="Kopiuje Level Type, Grid Type, Family i Family Type między otwartymi projektami.",
        steps=["Wybierz projekt źródłowy i docelowy.", "Przefiltruj listę i zaznacz pozycje.", "Uruchom kopiowanie."],
        scope="Wymaga co najmniej dwóch otwartych projektów. Przy konflikcie nazw zostają typy z projektu docelowego.",
        tags=["2 projekty"],
    ),
    "HcbPurgeAnnotationsButton": dict(
        what="Wyszukuje i usuwa nieużywane style adnotacji.",
        steps=["Uruchom narzędzie.", "Sprawdź listę nieużywanych stylów.", "Zatwierdź usunięcie."],
        scope="Typy kategorii Annotation bez instancji w modelu.",
        tags=["Porządki"],
    ),
    "HcbHCWireSizeButton": dict(
        what="Generuje skrócony opis przewodu dla obwodów elektrycznych typu Power w formacie `liczba żył x przekrój mm2`.",
        steps=["Uruchom polecenie w projekcie z obwodami."],
        scope="Nadpisuje `HC_WireSize` na podstawie liczby żył i przekroju z Wire Size.",
        tags=["Jedno kliknięcie", "Elektryka"],
    ),
    "HcbRenameMaterialsButton": dict(
        what="Seryjnie zmienia nazwy materiałów: prefiks, zamiana tekstu i sufiks, z podglądem.",
        steps=["Wybierz materiały.", "Ustaw prefiks, zamianę tekstu i/lub sufiks.", "Sprawdź podgląd i zatwierdź."],
        scope="Tylko wybrane materiały.",
        tags=["Podgląd zmian"],
    ),
    "HcbRibbonManagerButton": dict(
        what="Porządkuje wstążkę Revita: ukrywa, łączy, przenosi i przemianowuje karty oraz panele, także innych wtyczek.",
        steps=["Zaznacz kartę i odznacz niepotrzebne karty lub panele.", "Utwórz własną kartę i przenieś do niej panele wtyczek albo połącz karty.", "Zmień nazwy i zapisz."],
        scope="Panele natywne można ukrywać i przemianowywać, panele wtyczek także przenosić.",
        note="Układ zapisuje się w `%AppData%\\HCB\\RibbonLayout.json` i można go wyeksportować dla zespołu.",
        note_kind="TIP",
        tags=["Wstążka", "Działa bez projektu"],
    ),
    "HcbRibbonShortNamesButton": dict(
        what="Jednym kliknięciem skraca nazwy wszystkich kart i paneli, także natywnych (np. *Architecture* na *Arch*, *Work Plane* na *WPlane*). Na wstążce mieści się wtedy więcej kart.",
        steps=["Kliknij, aby skrócić.", "Kliknij ponownie, aby przywrócić."],
        scope="Wszystkie karty i panele. Własne nazwy z Ribbon Manager mają pierwszeństwo.",
        note="Ustawienie jest zapamiętywane. Osobno dla kart i paneli włączysz je w Ribbon Manager.",
        tags=["Przełącznik", "Działa bez projektu"],
    ),
}
