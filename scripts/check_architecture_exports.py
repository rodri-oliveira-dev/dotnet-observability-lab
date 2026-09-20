#!/usr/bin/env python3
"""Check LikeC4-generated image and static-site artifacts, without hand-drawn models.

Run after npm run architecture:build and npm exec -- likec4 export png --flat
-o dist/architecture-previews docs/architecture. Fail rather than publishing
partial exports or a site with missing relative assets.
"""
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlsplit, unquote
import argparse

ROOT = Path(__file__).resolve().parents[1]
VIEW_IDS = (
    "systemContext", "containers", "ingestionComponents",
    "outboxPublisherComponents", "consolidationApiComponents",
    "consolidationComponents", "valueWriteFlow",
)
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


class LocalAssets(HTMLParser):
    def __init__(self):
        super().__init__()
        self.paths = []

    def handle_starttag(self, tag, attrs):
        for name, value in attrs:
            if value and ((tag == "script" and name == "src") or
                          (tag == "link" and name == "href" and
                           value.endswith((".js", ".css")))):
                self.paths.append(value)


def check(site, images):
    if not (site / "index.html").is_file():
        raise ValueError("Static LikeC4 site is missing index.html")
    parser = LocalAssets()
    parser.feed((site / "index.html").read_text(encoding="utf-8"))
    if not parser.paths:
        raise ValueError("Static LikeC4 site has no script/style assets")
    for asset in parser.paths:
        parsed = urlsplit(asset)
        if parsed.scheme or parsed.netloc or parsed.path.startswith("/"):
            raise ValueError(f"Non-relocatable static asset: {asset}")
        local = (site / unquote(parsed.path)).resolve()
        if not local.is_relative_to(site.resolve()) or not local.is_file():
            raise ValueError(f"Static asset not found: {asset}")
    for view in VIEW_IDS:
        path = images / f"{view}.png"
        if not path.is_file():
            raise ValueError(f"Expected LikeC4 exported view is missing: {path}")
        if path.stat().st_size < 1000 or path.read_bytes()[:8] != PNG_SIGNATURE:
            raise ValueError(f"Invalid/empty LikeC4 PNG: {path}")
    return len(parser.paths)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site", type=Path, default=ROOT / "dist/architecture")
    parser.add_argument("--images", type=Path, default=ROOT / "dist/architecture-previews")
    args = parser.parse_args()
    count = check(args.site, args.images)
    print(f"Validated {len(VIEW_IDS)} rendered LikeC4 PNGs and {count} relative site assets.")


if __name__ == "__main__":
    main()
