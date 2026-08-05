# AI Family Builder Prompt Template

Ten prompt sluzy do generowania inputu JSON dla narzedzia `AI Family Builder`.

- **`hcb.ai_family_builder.v3`** — zalecany, nowy format builder-engine. Ma twardy walidator po stronie C# (`RequestValidator`), wiec generowany JSON musi byc scisle zgodny z ponizszymi regulami. Wymaga sekcji `builder`.
- `hcb.ai_family_builder.v2` — format starszy (sekcja "Prompt v2" nizej). Nadal dziala, ale jest luzniejszy i latwiej o niespojny JSON.

---

## Prompt v3 (zalecany)

```text
Wygeneruj wylacznie poprawny JSON. Nie dodawaj markdown, komentarzy ani opisu. Zwracasz tylko JSON.

Masz przygotowac JSON wykonawczy dla narzedzia Revit "AI Family Builder" w formacie hcb.ai_family_builder.v3.
Uzytkownik ma juz otwarta rodzine na poprawnym szablonie Revit. Nie tworzysz pliku rodziny.

Twarde reguly (walidator je egzekwuje, niezgodnosc = odrzucenie):
- schema = "hcb.ai_family_builder.v3"
- sekcja "builder" jest OBOWIAZKOWA; builder.name musi byc jednym z obslugiwanych builderow.
  Obecnie dostepny builder: "GenericBoxBuilder" (prostopadloscian, okragla ekstruzja + parametry/typy).
- dlugosci podawaj w milimetrach (liczby), katy w stopniach.
- parametr: { "name", "source", "data_type", "group", "scope" } oraz opcjonalnie "formula".
  - source: "family" albo "shared"
  - data_type: text | yesno | integer | number | length | area | volume | angle | material
  - group: Data | Text | IdentityData | Construction | Geometry | Materials | Mechanical | Electrical | Plumbing
  - scope: "type" albo "instance"
- types[].values mapuje nazwe parametru na wartosc. Kazdy uzyty parametr MUSI byc zadeklarowany w parameters.
  Kazdy typ ma unikalna nazwe.
- geometry[]: { "kind", "name", "solid", "width", "height", "depth", "radius", "placement", "offsets" }.
  - dla GenericBoxBuilder uzywaj kind = "rectangular_prism" albo "circular_extrusion".
  - dla rectangular_prism: width/height/depth podawaj jako odwolanie do parametru: { "ref": "Width" }.
  - dla circular_extrusion: radius i depth podawaj jako { "ref": "Param" } albo { "expr": "D1 / 2" }; placement.work_plane = "xy", "xz" albo "yz".
    Dozwolone formy wartosci: { "ref": "NazwaParametru" } | { "value": 200 } | { "expr": "Width - 2*10" }.
    W "expr" wolno uzywac tylko: liczb, nazw zadeklarowanych parametrow, operatorow + - * / oraz nawiasow.
- NIE zwracaj null. Pomijaj pola, ktorych nie uzywasz. Nie wymyslaj kindow ani builderow spoza listy.
- Nazwy parametrow musza byc identyczne w parameters, types.values i geometry.

Szkielet:
{
  "schema": "hcb.ai_family_builder.v3",
  "meta": {
    "schema_version": "3.0",
    "family_name": "NAZWA_RODZINY",
    "category_hint": "OST_GenericModel",
    "template_hint": "Metric Generic Model"
  },
  "builder": { "name": "GenericBoxBuilder", "domain": "generic" },
  "parameters": [],
  "types": [],
  "geometry": []
}

Zadanie:
[TU WSTAW OPIS RODZINY, TYPY, WYMIARY, PARAMETRY I ZALOZENIA]
```

### Minimalny przyklad v3 (przechodzi walidacje i buduje bryle)

```json
{
  "schema": "hcb.ai_family_builder.v3",
  "meta": {
    "schema_version": "3.0",
    "family_name": "Box_600x300x200",
    "category_hint": "OST_GenericModel",
    "template_hint": "Metric Generic Model"
  },
  "builder": { "name": "GenericBoxBuilder", "domain": "generic" },
  "parameters": [
    { "name": "Width", "source": "family", "data_type": "length", "group": "Geometry", "scope": "type" },
    { "name": "Height", "source": "family", "data_type": "length", "group": "Geometry", "scope": "type" },
    { "name": "Depth", "source": "family", "data_type": "length", "group": "Geometry", "scope": "type" }
  ],
  "types": [
    { "name": "600x300x200", "values": { "Width": 600, "Height": 300, "Depth": 200 } }
  ],
  "geometry": [
    {
      "kind": "rectangular_prism",
      "name": "Body",
      "solid": true,
      "width": { "ref": "Width" },
      "height": { "ref": "Height" },
      "depth": { "ref": "Depth" }
    }
  ]
}
```

> Uwaga (Faza 5): `GenericBoxBuilder` tworzy parametry, typy, wartosci oraz geometrie przez executory: parametryczna bryle prostopadloscienna (`RectangularPrismExecutor`) i okragla ekstruzje (`CircularExtrusionExecutor`). Prostopadloscian dostaje reference planes + etykietowane wymiary Width/Height + powiazanie glebokosci; okragla ekstruzja tworzy profil na plaszczyznie `xy`/`xz`/`yz` i wiaze glebokosc, gdy `depth` jest referencja do parametru. Etapy biegna w osobnych transakcjach objetych `TransactionGroup`. `connectors` dla `domain = "hvac"` sa wykonywane jako `ConnectorElement.CreateDuctConnector` hostowany na nazwanej geometrii i scianie (`host.geometry`, `host.face`) oraz ustawiaja rozmiar z sekcji `size`. `flex_tests` sa **wykonywane**: framework flex po parametrach/typach (flaga `validation.run_framework_flex`) oraz flex koncowy po geometrii (`validation.run_final_flex`) — kazdy przypadek ustawia wartosci lub aktywuje typ, regeneruje i jest wycofywany (nie modyfikuje rodziny); wynik trafia do raportu. `constraints`, `references` i `parameter_rules` sa walidowane, ale jeszcze nie wykonywane.

---

## Prompt v2 (starszy format)

```text
Wygeneruj wylacznie poprawny JSON. Nie dodawaj markdown, komentarzy ani opisu.

Masz przygotowac JSON wykonawczy dla narzedzia Revit "AI Family Builder".
Uzytkownik ma juz otwarta rodzine na poprawnym szablonie Revit.
Nie tworzysz pliku rodziny. Nie opisujesz krokow. Zwracasz tylko JSON.

Obowiazkowy format:
- schema = "hcb.ai_family_builder.v2"
- meta.schema_version = "2.0"
- dlugosci podawaj w milimetrach
- parametry rodzinne: source = "family"
- parametry wspoldzielone: source = "shared"
- zakres parametru: scope = "type" albo "instance"
- typ danych parametru: data_type

Dozwolone data_type:
- text
- yesno
- integer
- number
- length
- area
- volume
- angle
- material

Dozwolone group:
- Data
- Text
- IdentityData
- Construction
- Geometry
- Materials
- Mechanical
- Electrical
- Plumbing

Najpierw preferuj operacje geometry:
- rectangular_prism

Dla rectangular_prism uzywaj referencji do parametrow:
- "width": { "ref": "Width" }
- "height": { "ref": "Height" }
- "depth": { "ref": "Depth" }

Nie uzywaj golego stringa dla wymiaru parametrycznego, chyba ze musisz zachowac zgodnosc wsteczna.
Nie uzywaj operacji spoza listy.
Nie zwracaj null, jesli pole mozna pominac.
Nazwy parametrow musza byc spojne w parameters, types.values i geometry.
Kazdy typ w types musi miec unikalna nazwe.
Jesli parametr jest uzywany w geometry, musi byc zadeklarowany w parameters.

Szkielet:
{
  "schema": "hcb.ai_family_builder.v2",
  "meta": {
    "schema_version": "2.0",
    "family_name": "NAZWA_RODZINY",
    "category_hint": "OST_GenericModel",
    "template_hint": "Metric Generic Model"
  },
  "parameters": [],
  "types": [],
  "reference_planes": [],
  "constraints": [],
  "geometry": []
}

Zadanie:
[TU WSTAW OPIS RODZINY, TYPY, WYMIARY, PARAMETRY I ZALOZENIA]
```

## Minimalny przyklad

```json
{
  "schema": "hcb.ai_family_builder.v2",
  "meta": {
    "schema_version": "2.0",
    "family_name": "Damper_600x300",
    "category_hint": "OST_DuctAccessory",
    "template_hint": "Metric Generic Model"
  },
  "parameters": [
    {
      "name": "Width",
      "source": "family",
      "data_type": "length",
      "group": "Geometry",
      "scope": "type"
    },
    {
      "name": "Height",
      "source": "family",
      "data_type": "length",
      "group": "Geometry",
      "scope": "type"
    },
    {
      "name": "Depth",
      "source": "family",
      "data_type": "length",
      "group": "Geometry",
      "scope": "type"
    }
  ],
  "types": [
    {
      "name": "600x300",
      "values": {
        "Width": 600,
        "Height": 300,
        "Depth": 200
      }
    }
  ],
  "geometry": [
    {
      "kind": "rectangular_prism",
      "name": "Body",
      "solid": true,
      "width": { "ref": "Width" },
      "height": { "ref": "Height" },
      "depth": { "ref": "Depth" }
    }
  ]
}
```

## Co podawac w zadaniu

- kategorie rodziny, np. `OST_DuctAccessory`
- nazwy typow i tabele wymiarow
- ktore parametry sa `type`, a ktore `instance`
- ktore parametry maja byc shared
- prosty opis bryly bazowej
- oczekiwane formuly, jesli sa potrzebne
