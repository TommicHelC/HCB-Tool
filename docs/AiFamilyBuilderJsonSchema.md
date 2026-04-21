# AI Family Builder JSON Schema

`AI Family Builder` dziala w juz otwartym dokumencie rodziny Revit. Uzytkownik najpierw otwiera odpowiedni szablon rodziny, a dopiero potem wkleja JSON wygenerowany przez AI.

## Cel formatu

JSON ma byc kontraktem wykonawczym, a nie luźnym opisem. Powinien byc deterministyczny, jednoznaczny i gotowy do walidacji.

Sekcje glowne:

- `meta`
- `parameters`
- `types`
- `reference_planes`
- `dimensions`
- `geometry`

## Przykladowy JSON v1.1

```json
{
  "meta": {
    "schema_version": "1.1",
    "family_name": "Damper_600x300",
    "category_hint": "OST_DuctAccessory",
    "template_hint": "Generic Model"
  },
  "parameters": [
    {
      "name": "Width",
      "kind": "family",
      "spec_type": "length",
      "group": "Geometry",
      "is_instance": false
    },
    {
      "name": "Height",
      "kind": "family",
      "spec_type": "length",
      "group": "Geometry",
      "is_instance": false
    },
    {
      "name": "Depth",
      "kind": "family",
      "spec_type": "length",
      "group": "Geometry",
      "is_instance": false
    },
    {
      "name": "HC_Code",
      "kind": "shared",
      "group": "IdentityData",
      "is_instance": false
    }
  ],
  "types": [
    {
      "name": "600x300",
      "values": {
        "Width": 600,
        "Height": 300,
        "Depth": 200,
        "HC_Code": "DAM-600x300"
      }
    }
  ],
  "reference_planes": [
    { "name": "Left", "orientation": "vertical", "offset": -300 },
    { "name": "Right", "orientation": "vertical", "offset": 300 },
    { "name": "Bottom", "orientation": "horizontal", "offset": -150 },
    { "name": "Top", "orientation": "horizontal", "offset": 150 }
  ],
  "dimensions": [
    {
      "name": "WidthDim",
      "orientation": "horizontal",
      "reference_a": "Left",
      "reference_b": "Right",
      "parameter": "Width",
      "offset": -250
    },
    {
      "name": "HeightDim",
      "orientation": "vertical",
      "reference_a": "Bottom",
      "reference_b": "Top",
      "parameter": "Height",
      "offset": 450
    }
  ],
  "geometry": [
    {
      "op": "extrusion_rectangle",
      "name": "Body",
      "width": "Width",
      "height": "Height",
      "depth": "Depth"
    }
  ]
}
```

## `parameters`

Obslugiwane pola:

- `name`
- `kind`
- `spec_type`
- `group`
- `is_instance`
- `formula`

Obslugiwane `kind`:

- `family`
- `shared`

Obslugiwane `spec_type`:

- `text`
- `yesno`
- `integer`
- `number`
- `length`
- `area`
- `volume`
- `angle`
- `material`

Obslugiwane `group`:

- `Data`
- `Text`
- `IdentityData`
- `Construction`
- `Geometry`
- `Materials`
- `Mechanical`
- `Electrical`
- `Plumbing`

## `types`

Kazdy wpis w `values` mapuje nazwe parametru na wartosc.

Jednostki:

- `length` podawaj w milimetrach
- `area` podawaj w metrach kwadratowych
- `volume` podawaj w metrach szesciennych
- `angle` podawaj w stopniach

## `reference_planes`

Sluza do pierwszego kroku prawdziwej parametrycznosci.

Pola:

- `name`
- `orientation`: `vertical` albo `horizontal`
- `offset`: pozycja w mm wzgledem zera widoku rodziny

## `dimensions`

Wiazanie wymiaru do parametru rodzinnego.

Pola:

- `name`
- `orientation`
- `reference_a`
- `reference_b`
- `parameter`
- `offset`

## `geometry`

Obslugiwane operacje:

- `extrusion_rectangle`
- `void_rectangle`
- `extrusion_circle`
- `void_circle`
- `blend_rectangle`

### `extrusion_rectangle` / `void_rectangle`

Pola:

- `width`
- `height`
- `depth`

### `extrusion_circle` / `void_circle`

Pola:

- `radius`
- `depth`

### `blend_rectangle`

Pola:

- `top_width`
- `top_height`
- `bottom_width`
- `bottom_height`
- `depth`

## Ograniczenia obecnej wersji

- Geometria jest juz szersza niz MVP, ale nadal opiera sie na kontrolowanym zestawie operacji.
- `reference_planes` i `dimensions` wspieraja poczatek parametrycznosci, ale nie pokrywaja jeszcze wszystkich zaleznosci rodzin Revit.
- Jesli JSON zawiera `shared`, Revit musi miec ustawiony poprawny plik `Shared Parameters`.
- Narzedzie nie zmienia kategorii rodziny ani nie tworzy nowego dokumentu `.rfa`.
