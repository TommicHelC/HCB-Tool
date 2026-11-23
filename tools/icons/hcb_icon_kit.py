"""HCB ribbon icon kit.

Shared palette and drawing primitives for the HCB Tools ribbon icons.
Every icon is authored on a 32x32 grid and returned as an SVG string.
The visual language follows the MEP Modify icons (Connect / Extend / Rotate):

* objects are flat shapes with a 1px darker outline and a soft cylindrical
  highlight band (light edge, mid body, darker shade edge),
* MEP objects are blue (ducts, terminals) or steel (pipes, hardware),
* documents / views are white paper with a steel outline,
* the *verb* of the tool is shown by an accent: a round badge in the
  bottom-right corner or an orange action glyph.

Accent colour = verb:
    green  -> create / add / connect / copy-to
    orange -> modify / edit / rename / rotate / transform
    red    -> delete / clear / purge / unload
    navy   -> inspect / analyse / report / search
"""

from __future__ import annotations

# --------------------------------------------------------------------------
# Palette
# --------------------------------------------------------------------------

BLUE_OUT = "#135491"
BLUE = "#3194DD"
BLUE_DARK = "#2477BE"
BLUE_LIGHT = "#7CC2F3"
BLUE_PALE = "#D3EBFB"

STEEL_OUT = "#374E6D"
STEEL = "#AFBFD1"
STEEL_DARK = "#93A6BD"
STEEL_LIGHT = "#D8E2EC"
STEEL_PALE = "#EEF3F8"

NAVY = "#2E466F"
NAVY_LIGHT = "#4A6894"

GREEN = "#2FA84F"
GREEN_OUT = "#1E7A38"
GREEN_LIGHT = "#5CC879"

ORANGE = "#FAA425"
ORANGE_OUT = "#C67A0E"
ORANGE_LIGHT = "#FFC870"

RED = "#E5484D"
RED_OUT = "#A8282C"

PAPER = "#FFFFFF"
PAPER_SHADE = "#E8EEF5"
LINE = "#93A6BD"

BADGE_COLORS = {
    "green": (GREEN, GREEN_OUT, GREEN_LIGHT),
    "orange": (ORANGE, ORANGE_OUT, ORANGE_LIGHT),
    "red": (RED, RED_OUT, "#F27D80"),
    "navy": (NAVY, "#1C2E4D", NAVY_LIGHT),
    "blue": (BLUE, BLUE_OUT, BLUE_LIGHT),
}

FONT = "Segoe UI"

DEFS = f"""
<defs>
  <linearGradient id="paper" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="{PAPER}"/><stop offset="1" stop-color="{PAPER_SHADE}"/>
  </linearGradient>
  <linearGradient id="blueFace" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="{BLUE_LIGHT}"/><stop offset="0.45" stop-color="{BLUE}"/>
    <stop offset="1" stop-color="{BLUE_DARK}"/>
  </linearGradient>
  <linearGradient id="steelFace" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="{STEEL_PALE}"/><stop offset="0.5" stop-color="{STEEL_LIGHT}"/>
    <stop offset="1" stop-color="{STEEL}"/>
  </linearGradient>
  <linearGradient id="orangeFace" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="{ORANGE_LIGHT}"/><stop offset="1" stop-color="{ORANGE}"/>
  </linearGradient>
  <linearGradient id="navyFace" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="{NAVY_LIGHT}"/><stop offset="1" stop-color="{NAVY}"/>
  </linearGradient>
  <linearGradient id="goldFace" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#FFD36B"/><stop offset="1" stop-color="#F2A81D"/>
  </linearGradient>
</defs>
"""


def svg(body: str, size: int = 32) -> str:
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" '
        f'viewBox="0 0 32 32" shape-rendering="geometricPrecision">{DEFS}{body}</svg>'
    )


def g(content: str, transform: str = "") -> str:
    return f'<g transform="{transform}">{content}</g>' if transform else f"<g>{content}</g>"


# --------------------------------------------------------------------------
# Tubes (pipes / round ducts) - layered strokes give the cylindrical look
# --------------------------------------------------------------------------

TUBE_STYLES = {
    "blue": (BLUE_OUT, BLUE, BLUE_LIGHT, BLUE_DARK),
    "steel": (STEEL_OUT, STEEL, STEEL_PALE, STEEL_DARK),
    "orange": (ORANGE_OUT, ORANGE, ORANGE_LIGHT, "#E58C12"),
    "green": (GREEN_OUT, GREEN, GREEN_LIGHT, "#23903F"),
    "red": (RED_OUT, RED, "#F27D80", "#C93439"),
    "navy": ("#1C2E4D", NAVY, NAVY_LIGHT, "#24395C"),
}


def _tube_layers(path_fn, width: float, style: str) -> str:
    """path_fn(offset) -> SVG path 'd' offset perpendicular to the tube axis.
    Negative offset = highlight side (top / left)."""
    out, body, light, dark = TUBE_STYLES[style]
    inner = width - 2
    band = inner / 3.2
    parts = [
        f'<path d="{path_fn(0)}" fill="none" stroke="{out}" stroke-width="{width}"/>',
        f'<path d="{path_fn(0)}" fill="none" stroke="{body}" stroke-width="{inner}"/>',
        f'<path d="{path_fn(inner / 2 - band / 2)}" fill="none" stroke="{dark}" stroke-width="{band}"/>',
        f'<path d="{path_fn(-inner / 2 + band / 2 + 0.6)}" fill="none" stroke="{light}" stroke-width="{band}"/>',
    ]
    return "".join(parts)


def tube_h(x1: float, x2: float, y: float, width: float = 8, style: str = "blue") -> str:
    return _tube_layers(lambda o: f"M{x1},{y + o} H{x2}", width, style)


def tube_v(x: float, y1: float, y2: float, width: float = 8, style: str = "blue") -> str:
    # left side is the highlight side
    return _tube_layers(lambda o: f"M{x + o},{y1} V{y2}", width, style)


def tube_elbow(x: float, y_end: float, x_end: float, y: float, r: float,
               width: float = 8, style: str = "blue") -> str:
    """Elbow: vertical leg at x from y_end (bottom) up to the bend, then a
    horizontal leg at y to x_end (right). Highlight on the outer (top/left) side."""

    def d(o: float) -> str:
        rr = max(r + o, 0.01)
        return (f"M{x + o},{y_end} V{y + r} "
                f"A{rr},{rr} 0 0 1 {x + r},{y + o} H{x_end}")

    return _tube_layers(d, width, style)


def flange_v(x: float, y: float, h: float, style: str = "blue") -> str:
    out, body, light, _ = TUBE_STYLES[style]
    return (f'<rect x="{x - 1.5}" y="{y}" width="3" height="{h}" rx="0.8" '
            f'fill="{body}" stroke="{out}" stroke-width="1"/>')


def flange_h(x: float, y: float, w: float, style: str = "blue") -> str:
    out, body, light, _ = TUBE_STYLES[style]
    return (f'<rect x="{x}" y="{y - 1.5}" width="{w}" height="3" rx="0.8" '
            f'fill="{body}" stroke="{out}" stroke-width="1"/>')


# --------------------------------------------------------------------------
# Rectangular duct (box section) - used where "duct" must read as HVAC
# --------------------------------------------------------------------------

def duct_h(x: float, y: float, w: float, h: float, style: str = "blue") -> str:
    out, body, light, dark = TUBE_STYLES[style]
    return (
        f'<rect x="{x + .5}" y="{y + .5}" width="{w - 1}" height="{h - 1}" rx="1" '
        f'fill="{body}" stroke="{out}"/>'
        f'<rect x="{x + 1}" y="{y + 1}" width="{w - 2}" height="{(h - 2) * 0.32}" fill="{light}"/>'
        f'<rect x="{x + 1}" y="{y + h - 1 - (h - 2) * 0.22}" width="{w - 2}" height="{(h - 2) * 0.22}" fill="{dark}"/>'
    )


# --------------------------------------------------------------------------
# Badges
# --------------------------------------------------------------------------

def _glyph(kind: str, cx: float, cy: float) -> str:
    s = 'stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" fill="none"'
    if kind == "plus":
        return f'<path d="M{cx - 3.2},{cy} H{cx + 3.2} M{cx},{cy - 3.2} V{cy + 3.2}" {s}/>'
    if kind == "minus":
        return f'<path d="M{cx - 3.2},{cy} H{cx + 3.2}" {s}/>'
    if kind == "x":
        return f'<path d="M{cx - 2.4},{cy - 2.4} L{cx + 2.4},{cy + 2.4} M{cx + 2.4},{cy - 2.4} L{cx - 2.4},{cy + 2.4}" {s}/>'
    if kind == "check":
        return f'<path d="M{cx - 3},{cy + .2} L{cx - 1},{cy + 2.3} L{cx + 3},{cy - 2.2}" {s}/>'
    if kind == "right":
        return f'<path d="M{cx - 3.2},{cy} H{cx + 3} M{cx + .4},{cy - 2.7} L{cx + 3},{cy} L{cx + .4},{cy + 2.7}" {s}/>'
    if kind == "left":
        return f'<path d="M{cx + 3.2},{cy} H{cx - 3} M{cx - .4},{cy - 2.7} L{cx - 3},{cy} L{cx - .4},{cy + 2.7}" {s}/>'
    if kind == "up":
        return f'<path d="M{cx},{cy + 3.2} V{cy - 3} M{cx - 2.7},{cy - .4} L{cx},{cy - 3} L{cx + 2.7},{cy - .4}" {s}/>'
    if kind == "down":
        return f'<path d="M{cx},{cy - 3.2} V{cy + 3} M{cx - 2.7},{cy + .4} L{cx},{cy + 3} L{cx + 2.7},{cy + .4}" {s}/>'
    if kind == "swap":
        return (f'<path d="M{cx - 3},{cy - 1.6} H{cx + 2.6} M{cx + 1},{cy - 3.2} L{cx + 2.8},{cy - 1.6} L{cx + 1},{cy}'
                f' M{cx + 3},{cy + 1.8} H{cx - 2.6} M{cx - 1},{cy + .3} L{cx - 2.8},{cy + 1.8} L{cx - 1},{cy + 3.4}" '
                f'stroke="#fff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>')
    if kind == "search":
        return (f'<circle cx="{cx - .8}" cy="{cy - .8}" r="2.3" {s}/>'
                f'<path d="M{cx + .9},{cy + .9} L{cx + 3},{cy + 3}" {s}/>')
    if kind == "pencil":
        return (f'<path d="M{cx - 2.6},{cy + 2.6} L{cx + 2.2},{cy - 2.2}" stroke="#fff" stroke-width="2.6" stroke-linecap="butt"/>'
                f'<path d="M{cx - 3.3},{cy + 3.3} L{cx - 2.6},{cy + 1.6} L{cx - 1.6},{cy + 2.6} Z" fill="#fff"/>')
    if kind == "hash":
        return (f'<path d="M{cx - 1.2},{cy - 3.2} L{cx - 1.8},{cy + 3.2} M{cx + 1.8},{cy - 3.2} L{cx + 1.2},{cy + 3.2} '
                f'M{cx - 3.2},{cy - 1.1} H{cx + 3.2} M{cx - 3.4},{cy + 1.3} H{cx + 3}" '
                f'stroke="#fff" stroke-width="1.4" stroke-linecap="round" fill="none"/>')
    if kind == "refresh":
        return (f'<path d="M{cx + 2.8},{cy - .4} A2.9,2.9 0 1 1 {cx + 1.6},{cy - 2.4}" {s}/>'
                f'<path d="M{cx + .4},{cy - 3.9} L{cx + 3.1},{cy - 3.2} L{cx + 2.4},{cy - .6}" '
                f'stroke="#fff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>')
    if kind == "link":
        return (f'<path d="M{cx - .6},{cy + 1.6} L{cx - 1.6},{cy + 2.6} A1.6,1.6 0 0 1 {cx - 3.8},{cy + .4} '
                f'L{cx - 2.2},{cy - 1.2} A1.6,1.6 0 0 1 {cx},{cy - 1.2}" '
                f'stroke="#fff" stroke-width="1.5" stroke-linecap="round" fill="none"/>'
                f'<path d="M{cx + .6},{cy - 1.6} L{cx + 1.6},{cy - 2.6} A1.6,1.6 0 0 1 {cx + 3.8},{cy - .4} '
                f'L{cx + 2.2},{cy + 1.2} A1.6,1.6 0 0 1 {cx},{cy + 1.2}" '
                f'stroke="#fff" stroke-width="1.5" stroke-linecap="round" fill="none"/>')
    if kind == "bolt":
        return f'<path d="M{cx + .8},{cy - 4} L{cx - 2.6},{cy + .6} H{cx - .2} L{cx - .9},{cy + 4} L{cx + 2.6},{cy - .7} H{cx + .2} Z" fill="#fff"/>'
    if kind == "star":
        return (f'<path d="M{cx},{cy - 4} Q{cx + .6},{cy - .6} {cx + 4},{cy} Q{cx + .6},{cy + .6} {cx},{cy + 4} '
                f'Q{cx - .6},{cy + .6} {cx - 4},{cy} Q{cx - .6},{cy - .6} {cx},{cy - 4} Z" fill="#fff"/>')
    if kind == "sigma":
        return f'<path d="M{cx + 2.4},{cy - 2.8} H{cx - 2.4} L{cx + .4},{cy} L{cx - 2.4},{cy + 2.8} H{cx + 2.4}" {s} stroke-width="1.6"/>'
    if kind == "dots":
        return "".join(f'<circle cx="{cx + dx}" cy="{cy}" r="1.1" fill="#fff"/>' for dx in (-2.6, 0, 2.6))
    if kind == "warn":
        return (f'<path d="M{cx},{cy - 3} V{cy + .8}" {s}/>'
                f'<circle cx="{cx}" cy="{cy + 3}" r="1.1" fill="#fff"/>')
    raise ValueError(kind)


def badge(kind: str, color: str = "green", cx: float = 25, cy: float = 25, r: float = 6.5) -> str:
    fill, out, light = BADGE_COLORS[color]
    return (
        f'<circle cx="{cx}" cy="{cy}" r="{r + 1}" fill="#fff"/>'
        f'<circle cx="{cx}" cy="{cy}" r="{r - .5}" fill="{fill}" stroke="{out}" stroke-width="1"/>'
        f'<path d="M{cx - r + 2},{cy - 1} A{r - 2},{r - 2} 0 0 1 {cx + r - 2},{cy - 1}" '
        f'stroke="{light}" stroke-width="1" fill="none" opacity="0.7"/>'
        + _glyph(kind, cx, cy)
    )


# --------------------------------------------------------------------------
# Generic objects
# --------------------------------------------------------------------------

def doc(x: float, y: float, w: float, h: float, fold: float = 6, lines: int = 3,
        line_color: str = LINE, fill: str = "url(#paper)") -> str:
    x0, y0, x1, y1 = x + .5, y + .5, x + w - .5, y + h - .5
    out = (f'<path d="M{x0},{y0 + 1} Q{x0},{y0} {x0 + 1},{y0} H{x1 - fold} L{x1},{y0 + fold} V{y1 - 1} '
           f'Q{x1},{y1} {x1 - 1},{y1} H{x0 + 1} Q{x0},{y1} {x0},{y1 - 1} Z" '
           f'fill="{fill}" stroke="{STEEL_OUT}"/>'
           f'<path d="M{x1 - fold},{y0} V{y0 + fold} H{x1}" fill="{STEEL_LIGHT}" stroke="{STEEL_OUT}" stroke-linejoin="round"/>')
    for i in range(lines):
        ly = y + fold + 3 + i * 3.5
        if ly > y + h - 3:
            break
        lw = (w - 7) if i % 2 == 0 else (w - 10)
        out += f'<path d="M{x + 3.5},{ly} H{x + 3.5 + lw}" stroke="{line_color}" stroke-width="1.6" stroke-linecap="round"/>'
    return out


def frame(x: float, y: float, w: float, h: float, bar: str = NAVY, content: str = "") -> str:
    """Revit view / window: navy title bar + pale-blue content area."""
    return (f'<rect x="{x + .5}" y="{y + .5}" width="{w - 1}" height="{h - 1}" rx="1.5" fill="{BLUE_PALE}" stroke="{STEEL_OUT}"/>'
            f'<path d="M{x + .5},{y + 4.5} V{y + 2} Q{x + .5},{y + .5} {x + 2},{y + .5} H{x + w - 2} Q{x + w - .5},{y + .5} {x + w - .5},{y + 2} V{y + 4.5} Z" '
            f'fill="{bar}" stroke="{STEEL_OUT}"/>' + content)


def plan_lines(x: float, y: float, w: float, h: float, color: str = BLUE) -> str:
    """Simple floor-plan walls inside a view frame."""
    return (f'<path d="M{x},{y} H{x + w} V{y + h} H{x} Z M{x + w * .55},{y} V{y + h * .6} M{x},{y + h * .6} H{x + w * .35}" '
            f'fill="none" stroke="{color}" stroke-width="1.6" stroke-linejoin="round"/>')


def sheet(x: float, y: float, w: float, h: float) -> str:
    return (f'<rect x="{x + .5}" y="{y + .5}" width="{w - 1}" height="{h - 1}" rx="1" fill="url(#paper)" stroke="{STEEL_OUT}"/>'
            f'<rect x="{x + 2.5}" y="{y + 2.5}" width="{w - 5}" height="{h - 5}" fill="none" stroke="{LINE}"/>'
            f'<rect x="{x + w - 9}" y="{y + h - 7}" width="6" height="4" fill="{NAVY}"/>')


def table(x: float, y: float, w: float, h: float, rows: int = 4, cols: int = 3,
          head: str = BLUE) -> str:
    rh = (h - 1) / rows
    out = (f'<rect x="{x + .5}" y="{y + .5}" width="{w - 1}" height="{h - 1}" rx="1" fill="url(#paper)" stroke="{STEEL_OUT}"/>'
           f'<path d="M{x + .5},{y + rh + .5} V{y + 1.5} Q{x + .5},{y + .5} {x + 1.5},{y + .5} H{x + w - 1.5} Q{x + w - .5},{y + .5} {x + w - .5},{y + 1.5} V{y + rh + .5} Z" fill="{head}" stroke="{STEEL_OUT}"/>')
    for r in range(2, rows):
        out += f'<path d="M{x + 1},{y + .5 + r * rh} H{x + w - 1}" stroke="{LINE}"/>'
    for c in range(1, cols):
        cx = x + w * c / cols
        out += f'<path d="M{cx},{y + rh + .5} V{y + h - 1}" stroke="{LINE}"/>'
    return out


def param_rows(x: float, y: float, w: float, rows: int = 3, accent_row: int | None = None,
               accent: str = ORANGE) -> str:
    """Parameter list: label cell + value cell per row."""
    out = ""
    for i in range(rows):
        ry = y + i * 6
        fill = accent if i == accent_row else BLUE
        out += (f'<rect x="{x + .5}" y="{ry + .5}" width="{w - 1}" height="5" rx="1" fill="url(#paper)" stroke="{STEEL_OUT}"/>'
                f'<rect x="{x + 2}" y="{ry + 2}" width="{w * .32}" height="2" rx="1" fill="{STEEL_DARK}"/>'
                f'<rect x="{x + w * .45}" y="{ry + 2}" width="{w * .45}" height="2" rx="1" fill="{fill}"/>')
    return out


def funnel(x: float, y: float, s: float = 1.0, fill: str = "url(#blueFace)", out: str = BLUE_OUT) -> str:
    d = "M1,1 H21 Q22,1 21.3,2 L13.5,11.5 V19 L8.5,21.5 V11.5 L0.7,2 Q0,1 1,1 Z"
    return (f'<g transform="translate({x},{y}) scale({s})">'
            f'<path d="{d}" fill="{fill}" stroke="{out}" stroke-width="{1 / s}" stroke-linejoin="round"/>'
            f'<path d="M3.5,3.5 H18.5" stroke="{BLUE_LIGHT}" stroke-width="{1.4 / s}" stroke-linecap="round"/></g>')


def space(x: float, y: float, w: float, h: float, cross: bool = True) -> str:
    """MEP space: pale fill, dashed boundary, diagonal cross."""
    out = (f'<rect x="{x + .5}" y="{y + .5}" width="{w - 1}" height="{h - 1}" fill="{BLUE_PALE}" stroke="{NAVY}" stroke-width="1"/>'
           f'<rect x="{x + 2.5}" y="{y + 2.5}" width="{w - 5}" height="{h - 5}" fill="none" stroke="{BLUE}" stroke-width="1.2" stroke-dasharray="2.5 1.5"/>')
    if cross:
        out += (f'<path d="M{x + 2.5},{y + 2.5} L{x + w - 2.5},{y + h - 2.5} M{x + w - 2.5},{y + 2.5} L{x + 2.5},{y + h - 2.5}" '
                f'stroke="{BLUE}" stroke-width="1" opacity="0.8"/>')
    return out


def level_head(cx: float, cy: float, r: float = 4, line_to: float | None = None) -> str:
    out = ""
    if line_to is not None:
        out += f'<path d="M{cx + r},{cy} H{line_to}" stroke="{NAVY}" stroke-width="1.4" stroke-dasharray="3 1.5"/>'
    out += (f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="#fff" stroke="{NAVY}" stroke-width="1.2"/>'
            f'<path d="M{cx},{cy} V{cy - r} A{r},{r} 0 0 1 {cx + r},{cy} Z M{cx},{cy} V{cy + r} A{r},{r} 0 0 1 {cx - r},{cy} Z" fill="{NAVY}"/>')
    return out


def tag_shape(x: float, y: float, w: float, h: float, fill: str = "url(#orangeFace)", out: str = ORANGE_OUT) -> str:
    """Annotation tag pointing left."""
    p = h / 2
    return (f'<path d="M{x + .5},{y + p} L{x + p},{y + .5} H{x + w - 1.5} Q{x + w - .5},{y + .5} {x + w - .5},{y + 1.5} '
            f'V{y + h - 1.5} Q{x + w - .5},{y + h - .5} {x + w - 1.5},{y + h - .5} H{x + p} Z" fill="{fill}" stroke="{out}" stroke-linejoin="round"/>'
            f'<circle cx="{x + p + 1}" cy="{y + p}" r="1.4" fill="#fff" stroke="{out}" stroke-width=".8"/>')


def cube(cx: float, cy: float, s: float = 8, style: str = "blue") -> str:
    """Isometric cube (family / model). cx,cy = centre of top vertex area."""
    out, body, light, dark = TUBE_STYLES[style]
    h = s * 0.58
    top = f"M{cx},{cy - h} L{cx + s},{cy - h + h} L{cx},{cy + h} L{cx - s},{cy} Z"
    left = f"M{cx - s},{cy} L{cx},{cy + h} V{cy + h + s * 1.1} L{cx - s},{cy + s * 1.1} Z"
    right = f"M{cx + s},{cy} L{cx},{cy + h} V{cy + h + s * 1.1} L{cx + s},{cy + s * 1.1} Z"
    return (f'<path d="{left}" fill="{body}" stroke="{out}" stroke-linejoin="round"/>'
            f'<path d="{right}" fill="{dark}" stroke="{out}" stroke-linejoin="round"/>'
            f'<path d="{top}" fill="{light}" stroke="{out}" stroke-linejoin="round"/>')


def folder(x: float, y: float, w: float, h: float) -> str:
    return (f'<path d="M{x + .5},{y + h - .5} V{y + 1.5} Q{x + .5},{y + .5} {x + 1.5},{y + .5} H{x + w * .38} L{x + w * .48},{y + 3} '
            f'H{x + w - 1.5} Q{x + w - .5},{y + 3} {x + w - .5},{y + 4} V{y + h - .5} Z" fill="#F2B544" stroke="#B57A10" stroke-linejoin="round"/>'
            f'<path d="M{x + .5},{y + h - .5} L{x + 2.5},{y + 6.5} H{x + w - .5} V{y + h - .5} Z" fill="#FFD36B" stroke="#B57A10" stroke-linejoin="round"/>')


def cloud(x: float, y: float, s: float = 1.0, fill: str = "url(#blueFace)", out: str = BLUE_OUT) -> str:
    d = "M6,16 A5,5 0 0 1 5.5,6.2 A7,7 0 0 1 18.5,4.5 A5.5,5.5 0 0 1 20.5,16 Z"
    return (f'<g transform="translate({x},{y}) scale({s})"><path d="{d}" fill="{fill}" stroke="{out}" '
            f'stroke-width="{1 / s}" stroke-linejoin="round"/></g>')


def eye(cx: float, cy: float, w: float = 22, h: float = 13) -> str:
    return (f'<path d="M{cx - w / 2},{cy} Q{cx},{cy - h} {cx + w / 2},{cy} Q{cx},{cy + h} {cx - w / 2},{cy} Z" '
            f'fill="#fff" stroke="{NAVY}" stroke-width="1.4" stroke-linejoin="round"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{h * .34}" fill="url(#blueFace)" stroke="{BLUE_OUT}"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{h * .14}" fill="{NAVY}"/>'
            f'<circle cx="{cx - h * .13}" cy="{cy - h * .13}" r="{h * .07}" fill="#fff"/>')


def layers(cx: float, cy: float, w: float = 11, h: float = 5.5, n: int = 3, step: float = 4.5) -> str:
    out = ""
    colors = [(STEEL_LIGHT, STEEL_OUT), (BLUE_LIGHT, BLUE_OUT), ("url(#blueFace)", BLUE_OUT)]
    for i in range(n):
        y = cy + (n - 1 - i) * step - (n - 1) * step / 2
        f, o = colors[i % 3]
        out += (f'<path d="M{cx - w},{y} L{cx},{y - h} L{cx + w},{y} L{cx},{y + h} Z" fill="{f}" stroke="{o}" '
                f'stroke-linejoin="round"/>')
    return out


def gauge(cx: float, cy: float, r: float = 11, needle_deg: float = 35) -> str:
    import math
    a = math.radians(needle_deg)
    nx, ny = cx + math.sin(a) * (r - 3), cy - math.cos(a) * (r - 3)
    ticks = ""
    for deg in (-70, -35, 0, 35, 70):
        t = math.radians(deg)
        x1, y1 = cx + math.sin(t) * (r - 1.5), cy - math.cos(t) * (r - 1.5)
        x2, y2 = cx + math.sin(t) * (r - 4), cy - math.cos(t) * (r - 4)
        ticks += f'<path d="M{x1:.2f},{y1:.2f} L{x2:.2f},{y2:.2f}" stroke="{NAVY}" stroke-width="1.3" stroke-linecap="round"/>'
    return (f'<path d="M{cx - r},{cy + 3} V{cy} A{r},{r} 0 0 1 {cx + r},{cy} V{cy + 3} Z" fill="#fff" stroke="{NAVY}" stroke-width="1.3" stroke-linejoin="round"/>'
            f'<path d="M{cx - r + 2.5},{cy} A{r - 2.5},{r - 2.5} 0 0 1 {cx},{cy - r + 2.5}" stroke="{GREEN}" stroke-width="2" fill="none"/>'
            f'<path d="M{cx},{cy - r + 2.5} A{r - 2.5},{r - 2.5} 0 0 1 {cx + (r - 2.5) * .7},{cy - (r - 2.5) * .7}" stroke="{ORANGE}" stroke-width="2" fill="none"/>'
            f'<path d="M{cx + (r - 2.5) * .7},{cy - (r - 2.5) * .7} A{r - 2.5},{r - 2.5} 0 0 1 {cx + r - 2.5},{cy}" stroke="{RED}" stroke-width="2" fill="none"/>'
            + ticks +
            f'<path d="M{cx},{cy} L{nx:.2f},{ny:.2f}" stroke="{NAVY}" stroke-width="2" stroke-linecap="round"/>'
            f'<circle cx="{cx}" cy="{cy}" r="2" fill="{NAVY}"/>')


def text(x: float, y: float, s: str, size: float = 9, fill: str = "#fff", weight: str = "bold",
         anchor: str = "middle") -> str:
    return (f'<text x="{x}" y="{y}" font-family="{FONT}" font-size="{size}" font-weight="{weight}" '
            f'fill="{fill}" text-anchor="{anchor}">{s}</text>')


def arrow(d: str, color: str = GREEN, out: str | None = None, width: float = 2.4) -> str:
    """Open stroked arrow path (no head)."""
    o = out or color
    return (f'<path d="{d}" fill="none" stroke="{o}" stroke-width="{width + 1.4}" stroke-linecap="round" stroke-linejoin="round"/>'
            f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>')


def head(x: float, y: float, direction: str, color: str = GREEN, out: str = GREEN_OUT, s: float = 1.0) -> str:
    """Filled triangular arrow head with the tip at (x, y)."""
    pts = {
        "right": [(0, 0), (-6, -4.5), (-6, 4.5)],
        "left": [(0, 0), (6, -4.5), (6, 4.5)],
        "up": [(0, 0), (-4.5, 6), (4.5, 6)],
        "down": [(0, 0), (-4.5, -6), (4.5, -6)],
    }[direction]
    p = " ".join(f"{x + px * s},{y + py * s}" for px, py in pts)
    return f'<polygon points="{p}" fill="{color}" stroke="{out}" stroke-linejoin="round"/>'


def block_arrow_h(x1: float, x2: float, y: float, color: str = "green", t: float = 5) -> str:
    """Solid block arrow pointing right (x1<x2) or left (x1>x2), like Connect."""
    fill, out, light = BADGE_COLORS[color]
    sgn = 1 if x2 > x1 else -1
    hl = 6.5 * sgn
    hw = t / 2 + 3
    pts = [(x1, y - t / 2), (x2 - hl, y - t / 2), (x2 - hl, y - hw), (x2, y),
           (x2 - hl, y + hw), (x2 - hl, y + t / 2), (x1, y + t / 2)]
    p = " ".join(f"{a},{b}" for a, b in pts)
    return f'<polygon points="{p}" fill="{fill}" stroke="{out}" stroke-linejoin="round"/>'


def block_arrow_v(x: float, y1: float, y2: float, color: str = "green", t: float = 5) -> str:
    fill, out, light = BADGE_COLORS[color]
    sgn = 1 if y2 > y1 else -1
    hl = 6.5 * sgn
    hw = t / 2 + 3
    pts = [(x - t / 2, y1), (x - t / 2, y2 - hl), (x - hw, y2 - hl), (x, y2),
           (x + hw, y2 - hl), (x + t / 2, y2 - hl), (x + t / 2, y1)]
    p = " ".join(f"{a},{b}" for a, b in pts)
    return f'<polygon points="{p}" fill="{fill}" stroke="{out}" stroke-linejoin="round"/>'


def diffuser(x: float, y: float, s: float = 14) -> str:
    """Square ceiling diffuser (air terminal) seen from below."""
    out = f'<rect x="{x + .5}" y="{y + .5}" width="{s - 1}" height="{s - 1}" rx="1.2" fill="url(#steelFace)" stroke="{STEEL_OUT}"/>'
    for k in (2.5, 5):
        if k * 2 < s - 2:
            out += f'<rect x="{x + k + .5}" y="{y + k + .5}" width="{s - 2 * k - 1}" height="{s - 2 * k - 1}" fill="none" stroke="{STEEL_DARK}"/>'
    c = s / 2
    out += f'<rect x="{x + c - 1.5}" y="{y + c - 1.5}" width="3" height="3" fill="{STEEL_OUT}"/>'
    return out


def ahu(x: float, y: float, w: float = 13, h: float = 11) -> str:
    """Air handling unit box with a fan."""
    cx, cy = x + w / 2, y + h / 2 + .5
    return (f'<rect x="{x + .5}" y="{y + .5}" width="{w - 1}" height="{h - 1}" rx="1.2" fill="url(#steelFace)" stroke="{STEEL_OUT}"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{min(w, h) / 2 - 2}" fill="#fff" stroke="{STEEL_OUT}"/>'
            f'<path d="M{cx},{cy} Q{cx - 3},{cy - 2.5} {cx},{cy - 3.3} Z M{cx},{cy} Q{cx + 3},{cy + 2.5} {cx},{cy + 3.3} Z '
            f'M{cx},{cy} Q{cx + 2.5},{cy - 3} {cx + 3.3},{cy} Z M{cx},{cy} Q{cx - 2.5},{cy + 3} {cx - 3.3},{cy} Z" fill="{BLUE}"/>')
