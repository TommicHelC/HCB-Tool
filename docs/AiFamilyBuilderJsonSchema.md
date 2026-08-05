# AI Family Builder JSON Schema

`AI Family Builder` dziala w juz otwartym dokumencie rodziny Revit. Narzedzie nie tworzy nowego pliku `.rfa` i nie zmienia szablonu rodziny.

## Zalecany Format

Nowy format wykonawczy to `hcb.ai_family_builder.v2`. Jest kompatybilny z dotychczasowymi sekcjami, ale wymaga jawnych referencji do parametrow w geometrii parametrycznej.

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
    { "name": "Width", "source": "family", "data_type": "length", "group": "Geometry", "scope": "type" },
    { "name": "Height", "source": "family", "data_type": "length", "group": "Geometry", "scope": "type" },
    { "name": "Depth", "source": "family", "data_type": "length", "group": "Geometry", "scope": "type" }
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
  "reference_planes": [],
  "constraints": [],
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

## Jednostki

Wartosci liczbowe w `types.values` i geometrii sa interpretowane tak:

- `length`: milimetry
- `area`: metry kwadratowe
- `volume`: metry szescienne
- `angle`: stopnie
- `number` i `integer`: wartosc bez konwersji

## Parameters

Zalecane pola v2:

- `name`: nazwa parametru
- `source`: `family` albo `shared`
- `data_type`: typ danych
- `group`: grupa parametru w Revit
- `scope`: `type` albo `instance`
- `formula`: opcjonalna formula Revit

Wspierane `data_type`:

- `text`
- `yesno`
- `integer`
- `number`
- `length`
- `area`
- `volume`
- `angle`
- `material`

Stare aliasy nadal dzialaja:

- `kind` = `source`
- `spec_type` = `data_type`
- `is_instance` = odwrotnosc `scope: "type"`

## Types

`types[].values` mapuje nazwe parametru na wartosc dla danego typu. Kazdy typ musi miec unikalna nazwe.

## Geometry

Preferowana operacja v2:

- `rectangular_prism`

`rectangular_prism` wymaga:

- `width`: referencja do parametru length, np. `{ "ref": "Width" }`
- `height`: referencja do parametru length
- `depth`: referencja do parametru length albo stala wartosc

Ta operacja tworzy:

- cztery reference planes profilu w widoku planu
- dwa constraints opisane parametrami `Width` i `Height`
- extrusion
- alignment krawedzi szkicu do reference planes, jesli Revit pozwoli na takie wiazanie w aktywnym szablonie
- powiazanie glebokosci extrusion z parametrem `Depth`, jesli `depth` jest referencja

Starsze operacje nadal sa obslugiwane, ale sa traktowane jako prosta geometria:

- `extrusion_rectangle`
- `void_rectangle`
- `extrusion_circle`
- `void_circle`
- `blend_rectangle`

Te operacje nie gwarantuja pelnej parametrycznosci bryly.

## Reference Planes I Constraints

Sekcje `reference_planes` i `constraints` moga byc podawane recznie, ale dla prostego prostopadloscianu zalecane jest uzycie `rectangular_prism`, bo narzedzie wygeneruje bazowe planes i constraints automatycznie.

`constraints` jest nowsza nazwa sekcji. Stary klucz `dimensions` nadal dziala.

## Plik JSON Schema

Formalny schemat pomocniczy jest w [AiFamilyBuilder.v2.schema.json](AiFamilyBuilder.v2.schema.json).
