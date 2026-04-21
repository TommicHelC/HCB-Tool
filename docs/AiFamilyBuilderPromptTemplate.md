# AI Family Builder Prompt Template

Poniżej jest gotowy template promptu dla dowolnego AI, ktore ma wygenerowac JSON dla `AI Family Builder`.

## Prompt

```text
Wygeneruj wyłącznie poprawny JSON bez komentarzy, bez markdown i bez dodatkowego opisu.

Masz przygotować JSON wykonawczy dla narzędzia Revit "AI Family Builder".
Użytkownik ma już otwartą nową rodzinę na poprawnym szablonie Revit.
Nie tworzysz nowego pliku rodziny. Nie opisujesz kroków. Zwracasz tylko JSON.

Zasady:
1. JSON musi zawierać sekcje:
   - meta
   - parameters
   - types
   - reference_planes
   - dimensions
   - geometry
2. Dla parametrów rodzinnych używaj:
   - kind = "family"
3. Dla parametrów współdzielonych używaj:
   - kind = "shared"
4. Dozwolone spec_type:
   - text
   - yesno
   - integer
   - number
   - length
   - area
   - volume
   - angle
   - material
5. Dozwolone group:
   - Data
   - Text
   - IdentityData
   - Construction
   - Geometry
   - Materials
   - Mechanical
   - Electrical
   - Plumbing
6. Wszystkie długości podawaj w milimetrach.
7. Jeśli tworzysz constraints, pole dimensions ma łączyć reference planes z parametrami Width, Height, Depth itd.
8. Dozwolone operacje geometry:
   - extrusion_rectangle
   - void_rectangle
   - extrusion_circle
   - void_circle
   - blend_rectangle
9. Jeśli nie masz pewności, wybierz prostszą geometrię zamiast bardziej złożonej.
10. Nie używaj operacji spoza listy.
11. Nazwy parametrów mają być spójne z values, dimensions i geometry.
12. Jeśli parametr jest używany w geometry, najpierw zadeklaruj go w parameters.
13. Jeśli tworzysz typy, każdy typ musi mieć unikalną nazwę.
14. Nie zwracaj null, jeśli pole można po prostu pominąć.

Format wyjścia ma być zgodny z tym szkieletem:

{
  "meta": {
    "schema_version": "1.1",
    "family_name": "NAZWA_RODZINY",
    "category_hint": "OST_GenericModel",
    "template_hint": "Generic Model"
  },
  "parameters": [],
  "types": [],
  "reference_planes": [],
  "dimensions": [],
  "geometry": []
}

Zadanie:
[TU WSTAW OPIS RODZINY, TABELĘ DTR, WYMIARY, TYPY I ZAŁOŻENIA]
```

## Dobra praktyka dla promptu wejściowego

Do sekcji `Zadanie` najlepiej podawać:

- kategoria rodziny
- nazwy typów
- tabelę wymiarów
- które parametry mają być type, a które instance
- prosty opis geometrii
- które parametry firmowe mają być shared

## Przykład krótkiego opisu wejściowego

```text
Rodzina przepustnicy prostokątnej.
Kategoria: OST_DuctAccessory.
Typy:
- 400x200, Width=400, Height=200, Depth=180
- 600x300, Width=600, Height=300, Depth=220
Dodaj shared parameter HC_Code jako type.
Geometria ma być prostokątną bryłą o Width x Height i głębokości Depth.
Dodaj reference planes Left, Right, Bottom, Top oraz constraints dla Width i Height.
```
