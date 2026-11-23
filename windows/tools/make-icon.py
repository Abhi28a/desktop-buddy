"""Builds windows/src/DesktopBuddy.App/Assets/AppIcon.ico from the Dynamatix tree logo.

Windows icons are shape-only (no macOS rounded tile), so the tree is centred on a
transparent square. Run: python windows/tools/make-icon.py  (needs Pillow)
"""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[2]
src = Image.open(root / "Resources" / "Logos" / "dynamatix.png").convert("RGBA")
bbox = src.getbbox()
src = src.crop(bbox)
side = int(max(src.size) * 1.06)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(src, ((side - src.width) // 2, (side - src.height) // 2), src)
out = root / "windows" / "src" / "DesktopBuddy.App" / "Assets"
out.mkdir(parents=True, exist_ok=True)
square.resize((256, 256), Image.LANCZOS).save(
    out / "AppIcon.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)])
square.resize((256, 256), Image.LANCZOS).save(out / "AppIcon.png")
print("wrote", out / "AppIcon.ico")

# The tray icon while something is waiting: the same tree with a coral dot (Lily pad "Waiting",
# #C24E2C) in the top-right corner, ringed in white so it reads on light and dark taskbars.
from PIL import ImageDraw
waiting = square.resize((256, 256), Image.LANCZOS)
draw = ImageDraw.Draw(waiting)
cx, cy, r, ring = 218, 38, 34, 10
draw.ellipse((cx - r - ring, cy - r - ring, cx + r + ring, cy + r + ring), fill=(255, 255, 255, 255))
draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(0xC2, 0x4E, 0x2C, 255))
waiting.save(out / "AppIcon-waiting.ico",
             sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)])
waiting.save(out / "AppIcon-waiting.png")
print("wrote", out / "AppIcon-waiting.ico")
