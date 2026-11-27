"""Generate the tools documentation section of README.md from the ribbon definition.

    python tools/docs/build_readme.py

Source of truth:
  - ribbon layout      -> src/HCB.RevitAddin/Infrastructure/RibbonDefinitions.cs (ribbon_model.py)
  - Polish texts       -> tools/docs/tool_texts.py
  - screenshots        -> docs/images/ribbon/ (capture_ribbon.py)

Only the block between <!-- TOOLS:START --> and <!-- TOOLS:END --> is replaced.
"""
from __future__ import annotations

import re
import shutil
import sys
from pathlib import Path
from urllib.parse import quote

from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
from ribbon_model import REPO, Button, Pulldown, load  # noqa: E402
from tool_texts import PANELS, PULLDOWNS, T  # noqa: E402

README = REPO / "README.md"
IMAGES = REPO / "docs" / "images" / "ribbon"
START, END = "<!-- TOOLS:START -->", "<!-- TOOLS:END -->"

PANEL_COLORS = {
    "Modify": "2563eb",
    "HVAC": "0891b2",
    "MEP Modify": "7c3aed",
    "Views": "059669",
    "WithoutOpen": "d97706",
    "Manage": "475569",
}


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", text.lower().replace("&", "and")).strip("-")


ICONS = REPO / "docs" / "images" / "icons"


def rel(path: Path) -> str:
    """Repo-relative link. Icons of tools that exist only in another checkout (HCB_RIBBON_DEFINITIONS)
    are copied to docs/images/icons so the README has no broken images before that branch is merged."""
    path = path.resolve()
    try:
        return path.relative_to(REPO.resolve()).as_posix()
    except ValueError:
        parts = path.parts[path.parts.index("HCB.RevitAddin") + 1:]
        target = ICONS / "-".join(p for p in parts if p not in ("Features", "Resources"))
        ICONS.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        return target.relative_to(REPO.resolve()).as_posix()


def badge(text: str, color: str) -> str:
    label = quote(text.replace("-", "--").replace("_", "__"), safe="")
    return f'<img src="https://img.shields.io/badge/{label}-{color}?style=flat-square" alt="{text}">'


def shot(name: str) -> Path | None:
    p = IMAGES / f"{name}.png"
    return p if p.exists() else None


def tool_block(b: Button, panel: str, parent: Pulldown | None) -> str:
    t = T.get(b.id, {})
    color = PANEL_COLORS.get(panel, "64748b")
    where = f"{panel} › {parent.text} › {b.text}" if parent else f"{panel} › {b.text}"
    tags = [badge(where, color)] + [badge(x, "e2e8f0") for x in t.get("tags", [])]
    if b.min_version:
        tags.append(badge(f"Revit {b.min_version}+", "dc2626"))

    image = shot(slug(b.text))
    image_html = (
        f'<img src="{rel(image)}" alt="{b.text} – położenie na wstążce" width="{min(420, Image.open(image).width)}">'
        if image else f'<img src="{rel(b.icon(32))}" alt="{b.text}" width="64">'
    )

    lines = [
        f'<a id="{slug(b.text)}"></a>',
        "",
        f'### <img src="{rel(b.icon(32))}" width="28" align="top" alt=""> {b.text}',
        "",
        " ".join(tags),
        "",
        "<table>",
        "<tr>",
        f'<td width="440" align="center" valign="top">{image_html}</td>',
        '<td valign="top">',
        "",
        f"**Co robi**<br>{t.get('what', b.tooltip)}",
        "",
    ]
    if t.get("steps"):
        lines.append("**Jak użyć**")
        lines.append("")
        lines += [f"{i}. {s}" for i, s in enumerate(t["steps"], 1)]
        lines.append("")
    if t.get("scope"):
        lines += [f"**Zakres**<br>{t['scope']}", ""]
    lines += ["</td>", "</tr>", "</table>", ""]
    if t.get("note"):
        lines += [f"> [!{t.get('note_kind', 'NOTE')}]", f"> {t['note']}", ""]
    lines += ['<p align="right"><sub><a href="#narzedzia">↑ do spisu narzędzi</a></sub></p>', "", "<br>", ""]
    return "\n".join(lines)


def short(b: Button) -> str:
    """First sentence of the Polish description, falling back to the ribbon tooltip."""
    what = T.get(b.id, {}).get("what")
    if not what:
        return b.tooltip
    first = re.split(r"(?<=[.!?])\s(?=[A-ZĄĆĘŁŃÓŚŹŻ])", what, maxsplit=1)[0]
    # Markdown is not parsed inside a one-line HTML cell.
    first = re.sub(r"`([^`]+)`", r"<code>\1</code>", first)
    return re.sub(r"\*([^*]+)\*", r"<i>\1</i>", first)


def buttons_of(panel) -> list[tuple[Button, Pulldown | None]]:
    out = []
    for it in panel.items:
        if isinstance(it, Pulldown):
            out += [(b, it) for b in it.buttons]
        else:
            out.append((it, None))
    return out


def panel_block(panel) -> str:
    emoji, intro = PANELS.get(panel.name, ("", ""))
    color = PANEL_COLORS.get(panel.name, "64748b")
    pslug = slug(panel.name)
    tools = buttons_of(panel)
    image = shot(f"panel-{pslug}")

    lines = [
        f'<a id="panel-{pslug}"></a>',
        "",
        f"## {emoji} Panel {panel.name}",
        "",
        f"{badge(f'{len(tools)} narzędzi', color)}",
        "",
        f"> {intro}",
        "",
    ]
    if image:
        lines += [f'<p align="center"><img src="{rel(image)}" alt="Panel {panel.name}"></p>', ""]

    # Compact index of the panel: icon, name, one line.
    lines += ["<table>", "<tr><th></th><th align=\"left\">Narzędzie</th><th align=\"left\">W skrócie</th></tr>"]
    last_parent = None
    for b, parent in tools:
        if parent is not None and parent is not last_parent:
            lines.append(
                f'<tr><td><img src="{rel(parent.icon(16))}" width="16" alt=""></td>'
                f'<td colspan="2"><b>▾ {parent.text}</b> <sub>– {PULLDOWNS.get(parent.id, parent.tooltip)}</sub></td></tr>'
            )
        last_parent = parent
        indent = "&nbsp;&nbsp;&nbsp;&nbsp;" if parent else ""
        lines.append(
            f'<tr><td><img src="{rel(b.icon(16))}" width="16" alt=""></td>'
            f'<td>{indent}<a href="#{slug(b.text)}">{b.text}</a></td>'
            f"<td>{short(b)}</td></tr>"
        )
    lines += ["</table>", "", "<br>", ""]

    for b, parent in tools:
        lines.append(tool_block(b, panel.name, parent))
    lines += ["<br>", "", "---", "", "<br>", ""]
    return "\n".join(lines)


def build() -> str:
    panels = load()
    total = sum(len(buttons_of(p)) for p in panels)
    lines = [
        START,
        "<!-- Wygenerowane przez tools/docs/build_readme.py – nie edytuj ręcznie, zmień tool_texts.py -->",
        "",
        '<a id="narzedzia"></a>',
        "",
        "## 🧰 Narzędzia",
        "",
        f"Wtyczka dodaje kartę **HCB Tools** z **{len(panels)} panelami** i **{total} narzędziami**. "
        "Na każdym zrzucie **czerwona ramka** pokazuje, gdzie na wstążce znajduje się opisywane narzędzie.",
        "",
    ]
    # Panel navigation cards.
    lines += ["<table>", "<tr>"]
    for p in panels:
        emoji, _ = PANELS.get(p.name, ("", ""))
        n = len(buttons_of(p))
        lines.append(
            f'<td align="center" valign="top"><a href="#panel-{slug(p.name)}"><b>{emoji}<br>{p.name}</b></a><br><sub>{n} narzędzi</sub></td>'
        )
    lines += ["</tr>", "</table>", "", "> [!TIP]", "> Najedź kursorem na przycisk w Revicie. Podpowiedź pokazuje ten sam opis w skrócie, a po chwili pełną instrukcję.", "", "<br>", ""]

    for p in panels:
        lines.append(panel_block(p))
    lines.append(END)
    return "\n".join(lines)


HERO_START, HERO_END = "<!-- HERO:START -->", "<!-- HERO:END -->"
REVIT_VERSIONS = ("2025", "2026", "2027")


def build_hero() -> str:
    """Top of the README: title, badges, quick links, ribbon screenshot and a strip of all tool icons."""
    panels = load()
    tools = [b for p in panels for b, _ in buttons_of(p)]
    shields = [
        badge("Revit " + " | ".join(REVIT_VERSIONS), "0696d7"),
        badge(f"{len(tools)} narzędzi", "e11d48"),
        badge(f"{len(panels)} paneli", "475569"),
        badge(".NET 8 | 10", "512bd4"),
        badge("Windows x64", "0078d4"),
        badge("Instalator MSI", "16a34a"),
    ]
    icons = " ".join(
        f'<a href="#{slug(b.text)}" title="{b.text}"><img src="{rel(b.icon(32))}" width="32" alt="{b.text}"></a>'
        for b in tools
    )
    lines = [
        HERO_START,
        "<!-- Wygenerowane przez tools/docs/build_readme.py -->",
        '<div align="center">',
        "",
        "# HCB Tools dla Revit",
        "",
        "**Natywna wtyczka C# dla Autodesk Revit, stworzona dla projektantów instalacji HVAC i MEP.**<br>",
        "Numeracja, filtry, parametry, przestrzenie, edycja tras, zestawienia w Excelu i operacje wsadowe na plikach, zebrane na jednej karcie wstążki.",
        "",
        " ".join(shields),
        "",
        '<a href="#narzedzia"><b>Narzędzia</b></a> &nbsp;·&nbsp; '
        '<a href="#instalacja"><b>Instalacja</b></a> &nbsp;·&nbsp; '
        '<a href="#dla-deweloperow"><b>Dla deweloperów</b></a> &nbsp;·&nbsp; '
        '<a href="#dokumentacja"><b>Dokumentacja</b></a>',
        "",
        "<br>",
        "",
    ]
    overview = shot("ribbon-overview")
    if overview:
        lines += [f'<img src="{rel(overview)}" alt="Karta HCB Tools na wstążce Revita">', "", "<br>", ""]
    lines += [icons, "", "<sub>Kliknij ikonę, aby przejść do opisu narzędzia.</sub>", "", "</div>", HERO_END]
    return "\n".join(lines)


def replace_block(text: str, start: str, end: str, block: str) -> str:
    return re.sub(re.escape(start) + r".*?" + re.escape(end), lambda _: block, text, flags=re.S)


def main() -> None:
    text = README.read_text(encoding="utf-8")
    block = build()
    if START in text:
        text = replace_block(text, START, END, block)
    else:
        marker = "## Repository Overview"
        text = text.replace(marker, block + "\n\n" + marker, 1)
    if HERO_START in text:
        text = replace_block(text, HERO_START, HERO_END, build_hero())
    README.write_text(text, encoding="utf-8", newline="\n")
    print(f"README updated: {README}")


if __name__ == "__main__":
    main()
