"""Build HCB ribbon icons.

Usage (from repo root):
    python tools/icons/build_ribbon_icons.py            # render SVG + PNG into the add-in
    python tools/icons/build_ribbon_icons.py --preview  # also write tools/icons/preview.png
    python tools/icons/build_ribbon_icons.py --only levels,splitter

Requires: pip install resvg-py pillow

Each icon is drawn once on a 32x32 grid (see hcb_icon_kit.py). The same SVG
is rendered to icon32.png and icon16.png; icons that need a simpler small
variant define a `small` body. The SVG sources are written to tools/icons/svg/.
"""

from __future__ import annotations

import argparse
import io
import math
import sys
from pathlib import Path

import resvg_py
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).parent))
from hcb_icon_kit import *  # noqa: E402,F403

ROOT = Path(__file__).resolve().parents[2]
ADDIN = ROOT / "src" / "HCB.RevitAddin"
SVG_DIR = Path(__file__).parent / "svg"


# --------------------------------------------------------------------------
# Extra local primitives
# --------------------------------------------------------------------------

def badge_text(label: str, color: str = "navy", cx: float = 25, cy: float = 25, r: float = 6.5,
               size: float = 8) -> str:
    fill, out, _ = BADGE_COLORS[color]
    return (f'<circle cx="{cx}" cy="{cy}" r="{r + 1}" fill="#fff"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{r - .5}" fill="{fill}" stroke="{out}"/>'
            + text(cx, cy + size * .36, label, size=size))


def gear(cx: float, cy: float, r: float = 5, teeth: int = 8, fill: str = NAVY, hole: str = "#fff") -> str:
    pts = []
    for i in range(teeth * 4):
        a = math.pi * 2 * i / (teeth * 4)
        rr = r if (i % 4) in (0, 1) else r * .74
        pts.append(f"{cx + math.cos(a) * rr:.2f},{cy + math.sin(a) * rr:.2f}")
    return (f'<polygon points="{" ".join(pts)}" fill="{fill}" stroke="{fill}" stroke-width=".6" stroke-linejoin="round"/>'
            f'<circle cx="{cx}" cy="{cy}" r="{r * .36}" fill="{hole}"/>')


def num_circle(cx: float, cy: float, n: str, fill: str = NAVY, r: float = 5, size: float = 8.5) -> str:
    return (f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{fill}" stroke="#fff" stroke-width="1"/>'
            + text(cx, cy + size * .36, n, size=size))


def cursor(x: float, y: float, s: float = 1.0) -> str:
    d = "M0,0 V14 L3.4,10.8 L6,16.4 L8.4,15.3 L5.9,9.8 H10.4 Z"
    return (f'<g transform="translate({x},{y}) scale({s})"><path d="{d}" fill="#fff" stroke="{NAVY}" '
            f'stroke-width="{1.2 / s}" stroke-linejoin="round"/></g>')


def coin_stack(cx: float, base_y: float, n: int = 3, rx: float = 6, ry: float = 2.4) -> str:
    out = ""
    for i in range(n):
        y = base_y - i * 3
        out += (f'<path d="M{cx - rx},{y - 1.6} V{y} A{rx},{ry} 0 0 0 {cx + rx},{y} V{y - 1.6}" fill="#F2A81D" stroke="#B57A10"/>'
                f'<ellipse cx="{cx}" cy="{y - 1.6}" rx="{rx}" ry="{ry}" fill="url(#goldFace)" stroke="#B57A10"/>')
    return out


def chain(cx: float, cy: float, color: str = BLUE, out: str = BLUE_OUT) -> str:
    link = lambda x, y: (f'<rect x="{x - 6}" y="{y - 3}" width="12" height="6" rx="3" fill="none" stroke="{out}" stroke-width="3.4"/>'
                         f'<rect x="{x - 6}" y="{y - 3}" width="12" height="6" rx="3" fill="none" stroke="{color}" stroke-width="1.8"/>')
    return g(link(cx - 4, cy) + link(cx + 4, cy), f"rotate(-40 {cx} {cy})")


def leader(x1, y1, x2, y2, color=NAVY) -> str:
    return (f'<path d="M{x1},{y1} L{x2},{y2}" stroke="{color}" stroke-width="1.2"/>'
            f'<circle cx="{x2}" cy="{y2}" r="1.4" fill="{color}"/>')


def dash_cut(x: float, y1: float, y2: float) -> str:
    return f'<path d="M{x},{y1} V{y2}" stroke="{ORANGE_OUT}" stroke-width="1.6" stroke-dasharray="2.2 1.6"/>'


def rfa_doc(x, y, w, h) -> str:
    """Family file: paper with a small cube."""
    return doc(x, y, w, h, lines=0) + cube(x + w / 2, y + h / 2 - 1, min(w, h) * .26)


def rvt_docs(x, y, n=3, w=16, h=20, step=3) -> str:
    out = ""
    for i in range(n):
        out += doc(x + i * step, y + i * step, w, h, lines=3 if i == n - 1 else 0)
    return out


# --------------------------------------------------------------------------
# Icons. Each returns the SVG body for the 32 grid.
# --------------------------------------------------------------------------

def i_filters():
    return funnel(5, 5, 1.0)


def i_copy_filters():
    return (funnel(2, 2, .72, fill="url(#steelFace)", out=STEEL_OUT)
            + funnel(8, 7, .8) + badge("right", "green"))


def i_filters_bulk_edit():
    rows = ""
    for i, on in enumerate((True, False, True)):
        y = 3 + i * 8
        rows += (f'<rect x="2.5" y="{y + .5}" width="21" height="6" rx="1.2" fill="url(#paper)" stroke="{STEEL_OUT}"/>'
                 + funnel(4.2, y + 1.6, .2)
                 + f'<rect x="10" y="{y + 2.6}" width="6" height="2" rx="1" fill="{STEEL_DARK}"/>'
                 + f'<rect x="17.5" y="{y + 2}" width="4.5" height="3" rx="1.5" fill="{GREEN if on else STEEL}"/>'
                 + f'<circle cx="{20.5 if on else 19}" cy="{y + 3.5}" r="1.1" fill="#fff"/>')
    return rows + badge("pencil", "orange")


def i_filters_legend():
    out = (f'<rect x="3.5" y="2.5" width="25" height="27" rx="1.5" fill="url(#paper)" stroke="{STEEL_OUT}"/>'
           f'<rect x="3.5" y="2.5" width="25" height="5" rx="1.5" fill="{NAVY}" stroke="{STEEL_OUT}"/>')
    for i, c in enumerate((BLUE, ORANGE, GREEN)):
        y = 10 + i * 6
        out += (f'<rect x="6.5" y="{y + .5}" width="6" height="4" fill="{c}" stroke="{STEEL_OUT}" stroke-width=".8"/>'
                f'<path d="M15,{y + 2.5} H{25 if i != 1 else 22}" stroke="{LINE}" stroke-width="1.6" stroke-linecap="round"/>')
    return out + funnel(19, 17, .5)


def i_levels():
    return (level_head(6, 8, 4.2, line_to=30) + level_head(6, 24, 4.2, line_to=30)
            + f'<rect x="12.5" y="10.5" width="17" height="9" rx="2" fill="url(#orangeFace)" stroke="{ORANGE_OUT}"/>'
            + text(21, 17.6, "+3.0", size=7.5, fill=NAVY))


def i_rename():
    return (f'<rect x="2.5" y="8.5" width="27" height="15" rx="2" fill="url(#paper)" stroke="{STEEL_OUT}"/>'
            f'<rect x="5" y="12" width="13" height="8" rx="1" fill="{BLUE_LIGHT}"/>'
            f'<path d="M7,16 H16" stroke="{BLUE_OUT}" stroke-width="1.6" stroke-linecap="round"/>'
            f'<path d="M22,6 H26 M24,6 V26 M22,26 H26" stroke="{ORANGE_OUT}" stroke-width="3.4" stroke-linecap="round"/>'
            f'<path d="M22,6 H26 M24,6 V26 M22,26 H26" stroke="{ORANGE}" stroke-width="1.8" stroke-linecap="round"/>')


def i_rename_views():
    return frame(2, 3, 23, 20, content=plan_lines(5.5, 10, 16, 10)) + badge("pencil", "orange")


def i_rename_sheets():
    return sheet(2, 4, 25, 19) + plan_lines(6, 9, 10, 8) + badge("pencil", "orange")


def i_rename_materials():
    return (f'<circle cx="13" cy="13" r="10.5" fill="url(#blueFace)" stroke="{BLUE_OUT}"/>'
            f'<path d="M5,17 L17,5 M7,21 L21,7 M11,23 L23,11" stroke="{BLUE_OUT}" stroke-width="1" opacity=".45"/>'
            f'<ellipse cx="9.5" cy="8.5" rx="3.6" ry="2.2" fill="#fff" opacity=".75" transform="rotate(-35 9.5 8.5)"/>'
            + badge("pencil", "orange"))


def i_numbering():
    return (f'<path d="M7,25 L16,16 L25,7" stroke="{STEEL_DARK}" stroke-width="1.6" stroke-dasharray="2.2 1.8"/>'
            + num_circle(7, 25, "1", BLUE) + num_circle(16, 16, "2", BLUE) + num_circle(25, 7, "3", ORANGE))


def i_manual_num():
    return (f'<path d="M7,8 L19,8" stroke="{STEEL_DARK}" stroke-width="1.6" stroke-dasharray="2.2 1.8"/>'
            + num_circle(7, 8, "1", BLUE) + num_circle(19, 8, "2", BLUE)
            + f'<circle cx="16" cy="21" r="5.5" fill="none" stroke="{ORANGE}" stroke-width="1.4" stroke-dasharray="2 1.6"/>'
            + cursor(16, 16, 1.0))


def i_num_duct_and_fit():
    return (tube_elbow(8, 31, 31, 9, 7, 9, "blue") + flange_v(20, 3.5, 11)
            + badge("hash", "navy"))


def i_num_duct():
    return tube_h(1, 31, 12, 11, "blue") + flange_v(4, 5.5, 13) + flange_v(28, 5.5, 13) + badge("hash", "navy")


def i_num_fitting():
    return tube_elbow(10, 31, 31, 10, 9, 10, "blue") + badge("hash", "navy")


def i_num_accessory():
    return (tube_h(1, 31, 12, 11, "blue") + flange_v(9, 5.5, 13) + flange_v(23, 5.5, 13)
            + f'<circle cx="16" cy="12" r="1.6" fill="{NAVY}"/>'
            + f'<path d="M12.5,15.5 L19.5,8.5" stroke="{NAVY}" stroke-width="2" stroke-linecap="round"/>'
            + badge("hash", "navy"))


def i_spaces():
    return space(3, 4, 26, 24)


def i_space_params_linked():
    return space(2, 2, 24, 22) + badge("link", "navy")


def i_space_to_element():
    return (space(2, 2, 18, 16) + diffuser(17, 17, 14)
            + arrow("M8,19.5 V24 H13", color=GREEN, out=GREEN_OUT, width=2.4) + head(16.5, 24, "right", s=.75))


def i_space_cleanup():
    return space(2, 2, 24, 22) + badge("x", "red")


def i_unique_colors():
    return (tube_h(1, 31, 7, 7, "blue") + tube_h(1, 31, 16, 7, "orange") + tube_h(1, 31, 25, 7, "green"))


def i_flow():
    out = tube_h(1, 31, 6, 9, "blue")
    for x, l in ((7, 10), (16, 19), (25, 14)):
        out += arrow(f"M{x},12 V{12 + l - 4}", color=ORANGE, out=ORANGE_OUT, width=2.2) + head(x, 12 + l + 1.5, "down", ORANGE, ORANGE_OUT, .72)
    return out


def i_velocity():
    return tube_h(1, 31, 25.5, 9, "blue") + gauge(16, 17, 12.5, 40)


def i_hvac_tags():
    return (tube_h(1, 21, 24, 9, "blue")
            + f'<path d="M8,20 L13,10" stroke="{NAVY}" stroke-width="1.3"/>'
            + f'<circle cx="8" cy="20.5" r="1.5" fill="{NAVY}"/>'
            + tag_shape(12, 4, 18, 11)
            + badge("plus", "green"))


def i_hvac_tools():
    wrench = (f'<path d="M14.2,13 L5,22.2 A2.4,2.4 0 0 0 8.4,25.6 L17.6,16.4" fill="url(#orangeFace)" stroke="{ORANGE_OUT}" stroke-linejoin="round"/>'
              f'<path d="M14.2,13 A7,7 0 0 1 22.7,4.2 L18.6,8.3 L19.4,11.2 L22.3,12 L26.4,7.9 A7,7 0 0 1 17.6,16.4 Z" '
              f'fill="url(#orangeFace)" stroke="{ORANGE_OUT}" stroke-linejoin="round"/>')
    return tube_elbow(7, 31, 31, 21, 6, 8, "blue") + wrench


def i_fittings_angle():
    # elbow + angle symbol (two legs and an arc) in the free corner
    return (tube_elbow(6, 31, 31, 6, 8, 8, "blue")
            + f'<path d="M30,28.5 H13.5 L26.5,15" stroke="{NAVY}" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>'
            + f'<path d="M22.5,28.5 A9,9 0 0 0 19.8,22" stroke="{ORANGE_OUT}" stroke-width="3.6" fill="none"/>'
            + f'<path d="M22.5,28.5 A9,9 0 0 0 19.8,22" stroke="{ORANGE}" stroke-width="2" fill="none"/>'
            + text(28, 25, "°", size=12, fill=ORANGE_OUT))


def i_level_from_hvac():
    return (level_head(6, 6, 4.2, line_to=31) + tube_h(1, 31, 25, 9, "blue")
            + block_arrow_v(19, 11, 21, "green", 4))


def i_system_assigner():
    return (tube_h(12, 31, 20, 8, "blue")
            + ahu(1, 13, 12, 14)
            + leader(20, 9, 20, 17) + leader(28, 9, 28, 17)
            + f'<rect x="15.5" y="2.5" width="9" height="6" rx="1.5" fill="url(#orangeFace)" stroke="{ORANGE_OUT}"/>'
            + f'<rect x="24" y="2.5" width="7.5" height="6" rx="1.5" fill="url(#orangeFace)" stroke="{ORANGE_OUT}"/>'
            + f'<path d="M7,12 Q7,5 13,5.5" stroke="{GREEN}" stroke-width="1.8" fill="none" stroke-dasharray="2 1.4"/>'
            + head(15, 5.6, "right", GREEN, GREEN_OUT, .5))


def i_mass():
    return (f'<circle cx="16" cy="7.5" r="4" fill="none" stroke="{STEEL_OUT}" stroke-width="3.4"/>'
            f'<circle cx="16" cy="7.5" r="4" fill="none" stroke="{STEEL}" stroke-width="1.8"/>'
            f'<path d="M7.5,11.5 H24.5 Q26,11.5 26.4,13 L29.6,27.4 Q30,29.5 28,29.5 H4 Q2,29.5 2.4,27.4 L5.6,13 Q6,11.5 7.5,11.5 Z" '
            f'fill="url(#steelFace)" stroke="{STEEL_OUT}"/>'
            + text(16, 25.5, "kg", size=10, fill=NAVY))


def _area_base():
    hatch = "".join(f'<path d="M{x},{y1} L{x + 6},{y1 - 6}" stroke="{ORANGE}" stroke-width="1.3"/>' for x, y1 in ((4, 26), (4, 20), (7, 14)))
    return tube_elbow(8, 31, 31, 8, 9, 11, "blue") + hatch


def i_area():
    return (tube_elbow(8, 31, 31, 8, 9, 11, "blue")
            + f'<rect x="15.5" y="17.5" width="15" height="11" rx="2" fill="url(#orangeFace)" stroke="{ORANGE_OUT}"/>'
            + text(23, 26, "m²", size=9, fill=NAVY))


def i_area_lin():
    return tube_elbow(8, 31, 31, 8, 9, 11, "blue") + badge_text("L", "orange", size=9)


def i_area_mc():
    return tube_elbow(8, 31, 31, 8, 9, 11, "blue") + badge_text("M", "navy", size=8.5)


def i_estimate():
    return doc(2, 1, 19, 25, lines=4) + coin_stack(23, 29, 3)


def i_splitter():
    return (tube_h(1, 9.5, 16, 11, "blue") + tube_h(12.5, 19.5, 16, 11, "blue") + tube_h(22.5, 31, 16, 11, "blue")
            + flange_v(10.5, 9.5, 13) + flange_v(11.5, 9.5, 13) + flange_v(20.5, 9.5, 13) + flange_v(21.5, 9.5, 13)
            + dash_cut(11, 2, 8) + dash_cut(11, 24, 30) + dash_cut(21, 2, 8) + dash_cut(21, 24, 30))


def i_unhide_all():
    return eye(15, 13, 26, 16) + badge("check", "green")


def i_views_duplicate():
    return (frame(2, 2, 20, 16) + frame(7, 8, 21, 17, content=plan_lines(10, 15, 14, 7))
            + badge("plus", "green"))


def i_templates():
    return frame(2, 3, 26, 22, content=plan_lines(5.5, 10, 12, 11, color=STEEL_DARK)) + gear(22, 17, 6.5)


def i_view_templates():
    return frame(2, 2, 23, 20, content=plan_lines(5, 9, 10, 9, color=STEEL_DARK)) + gear(19, 14, 5) + badge("right", "green")


def i_schedule_templates():
    return table(2, 2, 23, 20, rows=5, cols=3, head=NAVY) + badge("right", "green")


def i_schedule_excel():
    return table(1, 2, 22, 21, rows=5, cols=3, head=NAVY) + block_arrow_h(15, 31, 24, "green", 5)


def i_copy_between_projects():
    return (doc(1, 1, 16, 21, lines=3) + doc(12, 7, 17, 22, lines=0) + cube(20.5, 15, 5.2)
            + badge("right", "green"))


def i_cad_link_vg():
    return (layers(14, 14, 12, 6, 3, 5)
            + f'<rect x="1.5" y="1.5" width="12" height="7" rx="1.5" fill="{NAVY}"/>' + text(7.5, 7.2, "CAD", size=5.6)
            + badge("right", "green"))


def i_without_open():
    return rvt_docs(2, 2, 3, 17, 21, 4) + badge("bolt", "navy")


def i_batch_scan():
    return rvt_docs(2, 2, 3, 17, 21, 4) + badge("search", "navy")


def i_unload_links():
    return doc(3, 1, 20, 26, lines=0) + chain(13, 14) + badge("x", "red")


def i_family_report():
    return table(9, 8, 21, 19, rows=5, cols=2, head=NAVY) + cube(8, 7, 6.5)


def i_viewer_export():
    return (cloud(4, 0, 1.0) + folder(5, 16, 23, 15)
            + block_arrow_v(16, 9, 24, "green", 4))


def i_upgrade_copy():
    return rvt_docs(2, 2, 2, 18, 22, 5) + badge("up", "green")


def i_add_shared():
    return param_rows(9, 10, 21, 3) + cube(9, 7.5, 6.5) + badge("plus", "green")


def i_rename_family_params():
    return param_rows(9, 10, 21, 3, accent_row=1) + cube(9, 7.5, 6.5) + badge("pencil", "orange")


def i_parameters():
    return param_rows(2, 6, 28, 3, accent_row=1)


def i_shared_params():
    return param_rows(2, 3, 26, 3) + badge("check", "green")


def i_param_audit():
    return param_rows(2, 3, 26, 3, accent_row=1, accent=RED) + badge("search", "navy")


def i_param_service():
    return cloud(1, 0, .7) + param_rows(6, 11, 22, 2) + badge("down", "green")


def i_calculator():
    out = (f'<rect x="5.5" y="1.5" width="21" height="29" rx="2.5" fill="url(#navyFace)" stroke="#1C2E4D"/>'
           f'<rect x="8.5" y="4.5" width="15" height="6" rx="1" fill="{BLUE_PALE}" stroke="#1C2E4D"/>'
           + text(22, 9.6, "123", size=5.5, fill=NAVY, anchor="end"))
    for r in range(3):
        for c in range(3):
            fill = ORANGE if (r, c) == (2, 2) else STEEL_LIGHT
            out += f'<rect x="{8.5 + c * 5.3}" y="{13.5 + r * 5.3}" width="4" height="4" rx=".8" fill="{fill}"/>'
    return out


def i_clear_param():
    return (param_rows(2, 3, 26, 3)
            + f'<rect x="14" y="10" width="12" height="3" fill="#fff"/>'
            + f'<path d="M14.5,11.5 H25" stroke="{RED}" stroke-width="1.2" stroke-dasharray="1.6 1.2"/>'
            + badge("x", "red"))


def i_copy_param():
    return (param_rows(2, 3, 22, 1) + param_rows(2, 20, 22, 1, accent_row=0, accent=BLUE)
            + arrow("M24,6 Q30,6 30,13 Q30,22.5 26.5,22.5", color=GREEN, out=GREEN_OUT, width=2)
            + head(24, 22.5, "left", GREEN, GREEN_OUT, .6))


def i_purge_anno():
    return (text(11, 19, "A", size=20, fill=NAVY)
            + f'<path d="M2.5,25 H19.5 M2.5,22 V28 M19.5,22 V28" stroke="{BLUE}" stroke-width="1.4"/>'
            + head(3, 25, "left", BLUE, BLUE, .4) + head(19, 25, "right", BLUE, BLUE, .4)
            + badge("x", "red"))


def i_hc_wire():
    cores = ((12, 9.5, "#8B5A2B"), (7.5, 17.5, "#2B2B2B"), (16.5, 17.5, "#9AA3AE"))
    out = (f'<circle cx="12" cy="14.5" r="10.5" fill="url(#steelFace)" stroke="{STEEL_OUT}"/>'
           f'<circle cx="12" cy="14.5" r="8.5" fill="#fff" stroke="{STEEL_DARK}"/>')
    for cx, cy, c in cores:
        out += f'<circle cx="{cx}" cy="{cy}" r="3.4" fill="{c}" stroke="#fff" stroke-width=".8"/><circle cx="{cx}" cy="{cy}" r="1.3" fill="#D98C3A"/>'
    return out + badge("bolt", "orange")


# ---- simplified 16 px variants -------------------------------------------

def s_numbering():
    return (f'<path d="M10,22 L22,10" stroke="{STEEL_DARK}" stroke-width="2.4"/>'
            + num_circle(9, 23, "1", BLUE, 8, 13) + num_circle(23, 9, "2", ORANGE, 8, 13))


def s_manual_num():
    return num_circle(10, 10, "1", BLUE, 8.5, 13) + cursor(14, 11, 1.25)


def s_levels():
    return (level_head(7, 8, 6, line_to=31) + level_head(7, 24, 6, line_to=31)
            + f'<rect x="16" y="12" width="14" height="8" rx="2" fill="{ORANGE}" stroke="{ORANGE_OUT}"/>')


def s_system_assigner():
    return (tube_h(12, 31, 22, 10, "blue") + ahu(1, 13, 14, 16)
            + f'<rect x="17.5" y="3.5" width="13" height="9" rx="2" fill="{ORANGE}" stroke="{ORANGE_OUT}"/>'
            + f'<path d="M24,12.5 V17" stroke="{NAVY}" stroke-width="2"/>')


def s_fittings_angle():
    return (tube_elbow(7, 31, 31, 7, 8, 10, "blue")
            + f'<path d="M30.5,28.5 H14 L26,15.5" stroke="{NAVY}" stroke-width="2.6" fill="none" stroke-linejoin="round"/>'
            + f'<path d="M23,28.5 A9,9 0 0 0 20.3,22" stroke="{ORANGE}" stroke-width="3" fill="none"/>')


SMALL = {
    "numbering": s_numbering,
    "manual-num": s_manual_num,
    "levels": s_levels,
    "system-assigner": s_system_assigner,
    "fittings-angle": s_fittings_angle,
}


# name -> (body fn, [target dirs relative to src/HCB.RevitAddin])
ICONS = {
    "filters": (i_filters, ["Resources/Ribbon/Filters"]),
    "copy-filters": (i_copy_filters, ["Features/CopyFilters/Resources"]),
    "filters-bulk-edit": (i_filters_bulk_edit, ["Features/ViewFiltersBulkEdit/Resources"]),
    "filters-legend": (i_filters_legend, ["Features/ViewFiltersLegend/Resources"]),
    "levels": (i_levels, ["Features/Levels/Resources"]),
    "rename": (i_rename, ["Resources/Ribbon/Rename"]),
    "rename-views": (i_rename_views, ["Features/RenameViews/Resources"]),
    "rename-sheets": (i_rename_sheets, ["Features/RenameSheets/Resources"]),
    "rename-materials": (i_rename_materials, ["Features/RenameMaterials/Resources"]),
    "numbering": (i_numbering, ["Resources/Ribbon/Numbering/Pulldown"]),
    "manual-num": (i_manual_num, ["Resources/Ribbon/Numbering/Manual", "Features/ManualNumbering/Resources"]),
    "num-duct-fit": (i_num_duct_and_fit, ["Resources/Ribbon/Numbering/DuctAndFit", "Features/DuctFittingNumbering/Resources"]),
    "num-duct": (i_num_duct, ["Resources/Ribbon/Numbering/Duct"]),
    "num-fitting": (i_num_fitting, ["Resources/Ribbon/Numbering/DuctFitting"]),
    "num-accessory": (i_num_accessory, ["Resources/Ribbon/Numbering/MepItems", "Features/AccessoryTerminalNumbering/Resources"]),
    "spaces": (i_spaces, ["Resources/Ribbon/Spaces"]),
    "space-params-linked": (i_space_params_linked, ["Features/SpaceParamLinked/Resources"]),
    "space-to-element": (i_space_to_element, ["Features/SpaceToElement/Resources"]),
    "space-cleanup": (i_space_cleanup, ["Features/SpaceCleanup/Resources"]),
    "unique-colors": (i_unique_colors, ["Features/ColorUniqueSystems/Resources"]),
    "flow": (i_flow, ["Features/FlowChanger/Resources"]),
    "velocity": (i_velocity, ["Features/VelocitySizer/Resources"]),
    "hvac-tags": (i_hvac_tags, ["Features/HvacTagging/Resources"]),
    "hvac-tools": (i_hvac_tools, ["Resources/Ribbon/HvacTools"]),
    "fittings-angle": (i_fittings_angle, ["Features/FittingsAngle/Resources"]),
    "level-from-hvac": (i_level_from_hvac, ["Features/LevelFromHVACElements/Resources"]),
    "system-assigner": (i_system_assigner, ["Features/SystemAssigner/Resources"]),
    "mass": (i_mass, ["Features/MassOfDuctsFittings/Resources"]),
    "area": (i_area, ["Resources/Ribbon/Area", "Features/DuctFittingsArea/Resources"]),
    "area-lin": (i_area_lin, ["Resources/Ribbon/Area/Lin"]),
    "area-mc": (i_area_mc, ["Resources/Ribbon/Area/Mc"]),
    "estimate": (i_estimate, ["Features/Estimate/Resources"]),
    "splitter": (i_splitter, ["Features/Splitter/Resources"]),
    "unhide-all": (i_unhide_all, ["Features/UnhideAllElements/Resources"]),
    "views-duplicate": (i_views_duplicate, ["Features/ViewsDuplicate/Resources"]),
    "templates": (i_templates, ["Resources/Ribbon/Templates"]),
    "view-templates": (i_view_templates, ["Features/TransferViewTemplates/Resources"]),
    "schedule-templates": (i_schedule_templates, ["Resources/Ribbon/Templates/Schedule"]),
    "schedule-excel": (i_schedule_excel, ["Features/ScheduleExcel/Resources"]),
    "copy-between-projects": (i_copy_between_projects, ["Features/CopyBetweenProjects/Resources"]),
    "cad-link-vg": (i_cad_link_vg, ["Features/CadLinkGraphicsCopy/Resources"]),
    "without-open": (i_without_open, ["Resources/Ribbon/WithoutOpen"]),
    "batch-scan": (i_batch_scan, ["Features/BatchFileScan/Resources"]),
    "unload-links": (i_unload_links, ["Features/UnloadLinks/Resources"]),
    "family-report": (i_family_report, ["Features/FamilyParameterReport/Resources"]),
    "viewer-export": (i_viewer_export, ["Features/ViewerExport/Resources"]),
    "upgrade-copy": (i_upgrade_copy, ["Features/UpgradeAndCopy/Resources"]),
    "add-shared": (i_add_shared, ["Features/BatchAddSharedFamilyParameters/Resources"]),
    "rename-family-params": (i_rename_family_params, ["Features/RenameFamilyContent/Resources"]),
    "parameters": (i_parameters, ["Resources/Ribbon/Parameters"]),
    "shared-params": (i_shared_params, ["Features/SharedParameters/Resources"]),
    "param-audit": (i_param_audit, ["Features/ProjectParameterAudit/Resources"]),
    "param-service": (i_param_service, ["Features/ParameterService/Resources"]),
    "calculator": (i_calculator, ["Features/ParameterCalculator/Resources"]),
    "clear-param": (i_clear_param, ["Features/ParameterClear/Resources"]),
    "copy-param": (i_copy_param, ["Features/ParameterCopy/Resources"]),
    "purge-anno": (i_purge_anno, ["Features/PurgeAnnotations/Resources"]),
    "hc-wire": (i_hc_wire, ["Features/HCWireSize/Resources"]),
}

# Reference icons drawn earlier by hand (kept as the style anchor, not regenerated).
REFERENCE = ["Features/ConnectElements/Resources", "Features/ConnectorExtend/Resources",
             "Features/RotateFittings/Resources"]


def render(svg_text: str, size: int) -> Image.Image:
    png = bytes(resvg_py.svg_to_bytes(svg_string=svg_text, width=size, height=size))
    return Image.open(io.BytesIO(png)).convert("RGBA")


def build(names: list[str], write: bool) -> dict[str, tuple[Image.Image, Image.Image]]:
    SVG_DIR.mkdir(parents=True, exist_ok=True)
    out = {}
    for name in names:
        fn, targets = ICONS[name]
        body = fn()
        src = svg(body)
        (SVG_DIR / f"{name}.svg").write_text(src, encoding="utf-8")
        src16 = svg(SMALL[name]()) if name in SMALL else src
        if name in SMALL:
            (SVG_DIR / f"{name}.16.svg").write_text(src16, encoding="utf-8")
        im32, im16 = render(src, 32), render(src16, 16)
        out[name] = (im32, im16)
        if write:
            for t in targets:
                d = ADDIN / t
                d.mkdir(parents=True, exist_ok=True)
                im32.save(d / "icon32.png")
                im16.save(d / "icon16.png")
    return out


def preview(images: dict, path: Path, reference_root: Path | None) -> None:
    items = list(images.items())
    if reference_root:
        for r in REFERENCE:
            p32, p16 = reference_root / r / "icon32.png", reference_root / r / "icon16.png"
            if p32.exists():
                items.append(("REF " + r.split("/")[1], (Image.open(p32).convert("RGBA"), Image.open(p16).convert("RGBA"))))
    cols, cw, ch = 8, 150, 120
    rows = (len(items) + cols - 1) // cols
    sheet_im = Image.new("RGB", (cols * cw, rows * ch), (243, 245, 248))
    d = ImageDraw.Draw(sheet_im)
    for i, (name, (a, b)) in enumerate(items):
        x, y = (i % cols) * cw + 8, (i // cols) * ch + 6
        big = a.resize((96, 96), Image.NEAREST)
        sheet_im.paste(big, (x, y), big)
        sheet_im.paste(a, (x + 102, y + 4), a)
        sheet_im.paste(b, (x + 110, y + 44), b)
        d.rectangle((x + 102, y + 64, x + 133, y + 95), fill=(59, 68, 83))
        sheet_im.paste(a, (x + 102, y + 64), a)
        d.text((x, y + 99), name[:24], fill=(20, 20, 20))
    sheet_im.save(path)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--no-write", action="store_true")
    ap.add_argument("--reference-root", default=str(ADDIN))
    ap.add_argument("--preview-path", default=str(Path(__file__).parent / "preview.png"))
    a = ap.parse_args()
    names = [n for n in a.only.split(",") if n] or list(ICONS)
    images = build(names, write=not a.no_write)
    if a.preview:
        preview(images, Path(a.preview_path), Path(a.reference_root))
    print(f"built {len(images)} icons")


if __name__ == "__main__":
    main()
