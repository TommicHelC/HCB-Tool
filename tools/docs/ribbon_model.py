"""Parse Infrastructure/RibbonDefinitions.cs into a plain tree (panels -> items -> buttons).

Used by the documentation generator and the screenshot script, so both follow the
ribbon exactly as the add-in builds it.
"""
from __future__ import annotations

import os
import re
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ADDIN = REPO / "src" / "HCB.RevitAddin"
# HCB_RIBBON_DEFINITIONS lets the image script follow a ribbon from another checkout (e.g. a feature branch).
DEFINITIONS = Path(os.environ.get("HCB_RIBBON_DEFINITIONS") or ADDIN / "Infrastructure" / "RibbonDefinitions.cs")


@dataclass
class Button:
    id: str
    text: str
    command: str
    tooltip: str
    long: str | None
    icon_dir: str | None
    min_version: int | None = None

    @property
    def feature(self) -> str:
        return self.command.split(".")[1]

    def icon(self, size: int = 32) -> Path:
        base = ADDIN / (self.icon_dir.replace("\\", "/") if self.icon_dir else f"Features/{self.feature}/Resources")
        return base / f"icon{size}.png"


@dataclass
class Pulldown:
    id: str
    text: str
    icon_dir: str | None
    command: str
    tooltip: str
    long: str
    buttons: list[Button] = field(default_factory=list)

    def icon(self, size: int = 32) -> Path:
        base = ADDIN / (self.icon_dir.replace("\\", "/") if self.icon_dir else f"Features/{self.command.split('.')[1]}/Resources")
        return base / f"icon{size}.png"


@dataclass
class Panel:
    name: str
    items: list  # Button | Pulldown, stacks flattened (stacked flag kept on item)


_STR = r'"((?:[^"\\]|\\.)*)"'
_NULLABLE = rf'(?:{_STR}|null)'


def _unescape(s: str | None) -> str | None:
    return None if s is None else s.replace(r'\\', '\\').replace(r'\"', '"')


def _parse_args(src: str, start: int) -> tuple[list[str], int]:
    """Split the top-level argument list that starts at src[start] == '('."""
    depth, i, cur, args, in_str = 0, start, [], [], False
    while i < len(src):
        c = src[i]
        if in_str:
            cur.append(c)
            if c == '\\':
                cur.append(src[i + 1]); i += 1
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True; cur.append(c)
        elif c in '([':
            depth += 1
            if depth > 1:
                cur.append(c)
        elif c in ')]':
            depth -= 1
            if depth == 0:
                if ''.join(cur).strip():
                    args.append(''.join(cur).strip())
                return args, i + 1
            cur.append(c)
        elif c == ',' and depth == 1:
            args.append(''.join(cur).strip()); cur = []
        else:
            cur.append(c)
        i += 1
    raise ValueError("unbalanced")


def _str(arg: str) -> str | None:
    arg = arg.strip()
    if arg == 'null':
        return None
    parts = re.findall(_STR, arg)
    return _unescape(''.join(parts)) if parts else None


def _typeof(arg: str) -> str:
    return re.search(r'typeof\(([\w.]+)\)', arg).group(1)


def _list_items(arg: str, src_cache: str) -> list:
    out, pos = [], 0
    pattern = re.compile(r'new (RibbonPushButtonDefinition|RibbonPulldownDefinition|RibbonStackDefinition)\s*\(')
    while True:
        m = pattern.search(arg, pos)
        if not m:
            return out
        args, end = _parse_args(arg, m.end() - 1)
        out.append(_build(m.group(1), args, src_cache))
        pos = end


def _build(kind: str, a: list[str], src: str):
    if kind == 'RibbonPushButtonDefinition':
        avail = a[5] if len(a) > 5 else 'null'
        icon = _str(a[6]) if len(a) > 6 else None
        return Button(_str(a[0]), _str(a[1]), _typeof(a[2]), _str(a[3]), _str(a[4]) if len(a) > 4 and not a[4].startswith('typeof') else None, icon)
    if kind == 'RibbonPulldownDefinition':
        if a[6].startswith('CreateProjectParameterButtons'):
            buttons = _project_parameter_buttons(src)
        else:
            buttons = _list_items(a[6], src)
        return Pulldown(_str(a[0]), _str(a[1]), _str(a[3]), _typeof(a[2]), _str(a[4]), _str(a[5]), buttons)
    stack = _list_items(a[0], src)
    for item in stack:
        item.stacked = True
    return ('stack', stack)


def _project_parameter_buttons(src: str) -> list[Button]:
    body = src[src.index('CreateProjectParameterButtons(int revitVersion)'):]
    body = body[:body.index('return buttons;')]
    buttons = _list_items(body, src)
    for b in buttons:
        if b.id == 'HcbParameterServiceButton':
            b.min_version = 2026
    return buttons


def load() -> list[Panel]:
    src = DEFINITIONS.read_text(encoding='utf-8-sig')
    src_nc = re.sub(r'^\s*//.*$', '', src, flags=re.M)
    panels = []
    for m in re.finditer(r'new RibbonPanelDefinition\s*\(', src_nc):
        args, _ = _parse_args(src_nc, m.end() - 1)
        items = []
        for it in _list_items(args[1], src_nc):
            if isinstance(it, tuple):
                items.extend(it[1])
            else:
                it.stacked = False
                items.append(it)
        panels.append(Panel(_str(args[0]), items))
    return panels


if __name__ == '__main__':
    for p in load():
        print(f'[{p.name}]')
        for it in p.items:
            if isinstance(it, Pulldown):
                print(f'  v {it.text}  ({it.icon().relative_to(REPO)} {it.icon().exists()})')
                for b in it.buttons:
                    print(f'      - {b.text}  [{b.id}] {b.icon().exists()} {b.min_version or ""}')
            else:
                print(f'  - {it.text}  [{it.id}] {it.icon().exists()} stacked={it.stacked}')
