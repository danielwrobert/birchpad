#!/usr/bin/env python3
"""Draw the 256x256 plugin icons shown in Logi Options+. Rerun after changing a design."""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT = "/System/Library/Fonts/Menlo.ttc"


def icon(path, background, glyph, glyph_color=(255, 255, 255)):
    img = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle((8, 8, 248, 248), radius=48, fill=background)
    font = ImageFont.truetype(FONT, 96)
    box = draw.textbbox((0, 0), glyph, font=font)
    x = (256 - (box[2] - box[0])) / 2 - box[0]
    y = (256 - (box[3] - box[1])) / 2 - box[1]
    draw.text((x, y), glyph, font=font, fill=glyph_color)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)
    print(path.relative_to(ROOT))


icon(ROOT / "src/ITermPlugin/package/metadata/Icon256x256.png", (30, 30, 30), ">_", (0, 215, 95))
icon(ROOT / "src/AgentHubPlugin/package/metadata/Icon256x256.png", (46, 107, 230), "[*]")
icon(ROOT / "src/CodexPlugin/package/metadata/Icon256x256.png", (31, 164, 99), "{ }")
icon(ROOT / "src/ClaudePlugin/package/metadata/Icon256x256.png", (142, 84, 233), "<*>")
