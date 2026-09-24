"""Builds TrayApp/app.ico from the SVG masters in this folder.

Sizes up to 24 px use logo-small.svg (fewer, thicker pins, so it stays
readable in the tray); larger sizes use logo.svg. Each SVG is rendered once by
headless Microsoft Edge at 512x512 and then downscaled with Pillow, which acts
as supersampled antialiasing.

Requirements: Microsoft Edge and Python 3 with Pillow (pip install pillow).
Usage:        python assets/build-icon.py
"""

import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import time

from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
ICO_PATH = HERE.parent / "TrayApp" / "app.ico"

RENDER_SIZE = 512
# Tray icon size at each Windows display scaling step (100% = 16 px, 125% = 20, ...)
# plus the usual Explorer/taskbar sizes.
SMALL_SIZES = [16, 20, 24]
LARGE_SIZES = [28, 32, 36, 40, 48, 64, 128, 256]

EDGE_CANDIDATES = [
    pathlib.Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft/Edge/Application/msedge.exe",
    pathlib.Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Microsoft/Edge/Application/msedge.exe",
]


def find_edge() -> pathlib.Path:
    for candidate in EDGE_CANDIDATES:
        if candidate.exists():
            return candidate
    sys.exit("Microsoft Edge not found: it's needed to render the SVGs.")


def render(edge: pathlib.Path, svg: pathlib.Path, work_dir: pathlib.Path) -> Image.Image:
    html = work_dir / f"{svg.stem}.html"
    html.write_text(
        '<!doctype html><html><body style="margin:0;background:transparent">'
        f'<img src="{svg.as_uri()}" width="{RENDER_SIZE}" height="{RENDER_SIZE}" style="display:block">'
        "</body></html>",
        encoding="utf-8",
    )
    png = work_dir / f"{svg.stem}.png"

    subprocess.run(
        [
            str(edge),
            "--headless=new",
            "--disable-gpu",
            "--hide-scrollbars",
            "--no-first-run",
            # One profile per render: a second launch on a profile still in use would
            # hand off to that instance instead of taking its own screenshot.
            f"--user-data-dir={work_dir / ('edge-profile-' + svg.stem)}",
            "--default-background-color=00000000",
            f"--window-size={RENDER_SIZE},{RENDER_SIZE}",
            f"--screenshot={png}",
            html.as_uri(),
        ],
        check=True,
        capture_output=True,
        timeout=120,
    )

    # msedge.exe can return before its child process has written the screenshot.
    deadline = time.monotonic() + 60
    while not png.exists() and time.monotonic() < deadline:
        time.sleep(0.25)
    if not png.exists():
        sys.exit(f"Edge didn't write a screenshot for {svg.name}.")
    time.sleep(0.5)

    image = Image.open(png).convert("RGBA")
    if image.size != (RENDER_SIZE, RENDER_SIZE):
        sys.exit(f"Unexpected render size for {svg.name}: {image.size}")
    return image


def main() -> None:
    edge = find_edge()
    work_dir = pathlib.Path(tempfile.mkdtemp(prefix="bpm-icon-"))
    try:
        small = render(edge, HERE / "logo-small.svg", work_dir)
        large = render(edge, HERE / "logo.svg", work_dir)
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)

    # BOX (area average) instead of LANCZOS: Lanczos rings on such a large reduction and
    # leaves a dark halo around the bolt. Pillow resizes RGBA with premultiplied alpha.
    frames = [small.resize((s, s), Image.BOX) for s in SMALL_SIZES]
    frames += [large.resize((s, s), Image.BOX) for s in LARGE_SIZES]

    # The first image is the base; append_images supplies the exact frame for every other size.
    frames[-1].save(
        ICO_PATH,
        format="ICO",
        sizes=[f.size for f in frames],
        append_images=frames[:-1],
    )
    print(f"Wrote {ICO_PATH} ({', '.join(str(f.width) for f in frames)} px)")


if __name__ == "__main__":
    main()
