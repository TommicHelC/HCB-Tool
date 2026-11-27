"""Turn raw ribbon captures (capture_ribbon.ps1) into README images with a red outline.

    python tools/docs/make_images.py [.scrape/ribbon]

Writes docs/images/ribbon/:
  ribbon-overview.png   - all HCB Tools panels
  panel-<panel>.png     - the panel outlined
  <tool>.png            - the tool outlined (pulldown tools: with the menu opened)
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageEnhance

sys.path.insert(0, str(Path(__file__).parent))
from build_readme import IMAGES, slug  # noqa: E402
from ribbon_model import REPO, Pulldown, load  # noqa: E402

RED = (230, 30, 30)
MARGIN = 36  # context around the panel in tool images


def read_json(p: Path) -> dict:
    return json.loads(p.read_text(encoding="utf-8-sig"))


def box(r: dict, origin: dict, pad: int = 0) -> tuple[int, int, int, int]:
    x, y = r["x"] - origin["x"], r["y"] - origin["y"]
    return (x - pad, y - pad, x + r["w"] + pad, y + r["h"] + pad)


def union(*boxes):
    return (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))


def focus(img: Image.Image, keep: list[tuple], outline: list[tuple], thin: list[tuple] = ()) -> Image.Image:
    """Dim everything except `keep`, draw red outlines."""
    dim = ImageEnhance.Brightness(img).enhance(0.45)
    for k in keep:
        k = tuple(int(v) for v in k)
        dim.paste(img.crop(k), k[:2])
    d = ImageDraw.Draw(dim)
    for b in thin:
        d.rounded_rectangle(b, radius=4, outline=RED, width=2)
    for b in outline:
        d.rounded_rectangle(b, radius=5, outline=RED, width=3)
    return dim


def crop(img: Image.Image, b: tuple) -> Image.Image:
    b = (max(0, b[0]), max(0, b[1]), min(img.width, b[2]), min(img.height, b[3]))
    return img.crop(b)


def menu_box(n: int, raw: Path, button: tuple) -> tuple[int, int, int, int] | None:
    """Bounding box of the opened menu: what changed below and right of the pulldown button."""
    before = Image.open(raw / f"pulldown-{n}-before.png").convert("RGB")
    after = Image.open(raw / f"pulldown-{n}-after.png").convert("RGB")
    diff = ImageChops.difference(before, after).convert("L").point(lambda v: 255 if v > 24 else 0)
    region = (max(0, button[0] - 6), button[3] - 4, after.width, after.height)
    bb = diff.crop(region).getbbox()
    if not bb:
        return None
    top = bb[1] + region[1]
    # Other things on screen may change too (canvas redraw), so the diff only gives the top edge.
    # The menu has a 1 px frame: find its left column next to the button, then follow the frame
    # down to the bottom edge and along the bottom edge to the right.
    px = after.load()
    probe_y = top + 8
    left = next((x for x in range(button[0] - 4, button[0] + 8) if near(px[x, probe_y], MENU_FRAME)), None)
    if left is None:
        return (bb[0] + region[0], top, bb[2] + region[0], bb[3] + region[1])
    bottom = probe_y
    while bottom + 1 < after.height and near(px[left, bottom + 1], MENU_FRAME):
        bottom += 1
    right = left + 1
    while right + 1 < after.width and near(px[right + 1, bottom], MENU_FRAME):
        right += 1
    return (left, top, right + 1, bottom + 1)


MENU_FRAME = (81, 92, 111)  # Revit 2025 dark theme pulldown menu border


def near(c, ref, tol: int = 18) -> bool:
    return sum(abs(a - b) for a, b in zip(c, ref)) < tol


def blank_outside(img: Image.Image, keep: list[tuple], ribbon_bottom: int) -> Image.Image:
    """Paint over everything below the ribbon except `keep` - hides the open project's content."""
    out = img.copy()
    d = ImageDraw.Draw(out)
    d.rectangle((0, ribbon_bottom, img.width, img.height), fill=(46, 46, 46))
    for k in keep:
        k = tuple(int(v) for v in k)
        out.paste(img.crop(k), k[:2])
    return out


def main(raw: Path) -> None:
    IMAGES.mkdir(parents=True, exist_ok=True)
    layout = read_json(raw / "layout.json")
    origin = layout["origin"]
    ribbon = Image.open(raw / "ribbon.png").convert("RGB")
    panels_raw = {p["name"]: p for p in layout["panels"]}
    top = min(p["rect"]["y"] for p in layout["panels"]) - origin["y"] - 2

    # Overview: panels only (the tab row shows the user's own tabs, so it is cut off).
    # The full strip is ~2000 px wide and unreadable in the README column, so it is split at the
    # panel boundary closest to the middle and the two halves are stacked.
    strip = crop(ribbon, (0, top, ribbon.width, ribbon.height))
    edges = [p["rect"]["x"] + p["rect"]["w"] - origin["x"] for p in layout["panels"]][:-1]
    cut = min(edges, key=lambda x: abs(x - strip.width / 2)) if edges else strip.width
    left, right = strip.crop((0, 0, cut, strip.height)), strip.crop((cut, 0, strip.width, strip.height))
    gap = 8
    overview = Image.new("RGB", (max(left.width, right.width), strip.height * 2 + gap), (46, 46, 46))
    overview.paste(left, (0, 0))
    overview.paste(right, (0, strip.height + gap))
    overview.save(IMAGES / "ribbon-overview.png", optimize=True)

    # Pulldown captures, numbered in tab order.
    pulldown_shots = {}
    for jf in sorted(raw.glob("pulldown-*.json"), key=lambda p: int(p.stem.split("-")[1])):
        n = int(jf.stem.split("-")[1])
        meta = read_json(jf)
        pulldown_shots[meta["text"]] = (n, meta)

    written = []
    for panel in load():
        praw = panels_raw.get(panel.name)
        if not praw:
            print(f"! panel not on the ribbon: {panel.name}")
            continue
        pbox = box(praw["rect"], origin)
        view = (pbox[0] - MARGIN, top, pbox[2] + MARGIN, ribbon.height)

        img = focus(ribbon, [pbox], [box(praw["rect"], origin, -1)])
        crop(img, (pbox[0] - 140, top, pbox[2] + 140, ribbon.height)).save(IMAGES / f"panel-{slug(panel.name)}.png", optimize=True)
        written.append(f"panel-{slug(panel.name)}")

        by_text = {i["text"]: i for i in praw["items"]}
        for item in panel.items:
            raw_item = by_text.get(item.text)
            if not raw_item:
                print(f"! not on the ribbon: {panel.name} / {item.text}")
                continue
            ibox = box(raw_item["rect"], origin, 1)

            if not isinstance(item, Pulldown):
                out = crop(focus(ribbon, [pbox], [ibox]), view)
                out.save(IMAGES / f"{slug(item.text)}.png", optimize=True)
                written.append(slug(item.text))
                continue

            n, meta = pulldown_shots[item.text]
            shot = Image.open(raw / f"pulldown-{n}-after.png").convert("RGB")
            so = meta["origin"]
            bbtn = box(meta["button"], so, 1)
            mbox = menu_box(n, raw, bbtn)
            if not mbox:
                print(f"! menu not found for {item.text}")
                continue
            ribbon_bottom = max(p["rect"]["y"] + p["rect"]["h"] for p in layout["panels"]) - so["y"]
            shot = blank_outside(shot, [mbox], ribbon_bottom)
            pb = box(praw["rect"], so)
            v = union((pb[0] - MARGIN, top, pb[2] + MARGIN, pb[3]), (mbox[0] - 12, 0, mbox[2] + 12, mbox[3] + 12))
            v = (v[0], top, v[2], v[3])
            revit = int(layout.get("revit", 2025))
            shown = [b for b in item.buttons if not b.min_version or b.min_version <= revit]
            row_h = (mbox[3] - mbox[1]) / len(shown)
            for i, b in enumerate(shown):
                row = (mbox[0] + 2, mbox[1] + i * row_h + 1, mbox[2] - 2, mbox[1] + (i + 1) * row_h - 1)
                out = crop(focus(shot, [pb, mbox], [row], [bbtn]), v)
                out.save(IMAGES / f"{slug(b.text)}.png", optimize=True)
                written.append(slug(b.text))

    print(f"{len(written)} images -> {IMAGES.relative_to(REPO)}")


if __name__ == "__main__":
    main(Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / ".scrape" / "ribbon")
