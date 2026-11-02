"""Store listing images: poster, box art, app tile and the 1920x1080 screenshots.

Usage (needs Pillow):
    python scripts/make-listing-images.py <listing-dir> <raw-screenshots-dir>

Writes, for each language:
    <listing>/<lang>/logos/poster-720x1080.png, poster-1440x2160.png   (9:16 poster art)
    <listing>/<lang>/logos/boxart-1080x1080.png, boxart-2160x2160.png  (1:1 box art)
    <listing>/<lang>/logos/apptile-300x300.png                         (1:1 app tile icon)
    <listing>/<lang>/screenshots/<n>-<name>.png                        (when the raw pictures exist)
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = Path(sys.argv[1])
RAW = Path(sys.argv[2])
LOGO = ROOT / "packaging" / "Logo" / "MrWConverter-logo.png"
SITE_IMAGES = ROOT / "do_not_share" / "site" / "img"
FONTS = Path("C:/Windows/Fonts")

BACKGROUND = (10, 15, 29)
PANEL = (20, 28, 49)
TEXT = (237, 241, 250)
SUBTEXT = (163, 174, 199)

TAGLINE = {
    "tr": ("Dönüştürmek, bir", "sürükleme kadar kısa."),
    "en": ("Converting is", "one drag away."),
}

# (raw picture or special name, headline, subline), in listing order.
SHOTS = {
    "tr": [
        ("wheel", "Sürükleyin, Shift'e basın, bırakın", "Dosyayı herhangi bir pencereden sürüklerken çark açılır"),
        ("shrink", "Küçült", "Videoyu ve fotoğrafı hedef boyuta indirin, birden çok dosyada"),
        ("background", "Arka planı kaldır", "Tek hareketle saydam PNG, fotoğrafınız bilgisayarınızdan çıkmaz"),
        ("edit", "Videoyu düzenle", "Bölün, parça silin, baş ve sonu kesin"),
        ("main", "Görsel, ses, video ve PDF", "Dosyalarınız bilgisayarınızda işlenir, hesap ve reklam yok"),
    ],
    "en": [
        ("wheel", "Drag, press Shift, let go", "The wheel opens while you drag a file from any window"),
        ("shrink", "Shrink", "Bring videos and photos down to a target size, several at once"),
        ("background", "Remove background", "A transparent PNG in one move, and your photo stays on your computer"),
        ("edit", "Edit video", "Split, delete parts, trim the start and end"),
        ("main", "Pictures, sound, video and PDFs", "Your files are processed on your computer. No account, no ads"),
    ],
}


def font(size, bold=True):
    return ImageFont.truetype(str(FONTS / ("segoeuib.ttf" if bold else "segoeui.ttf")), size)


def centered(draw, y, text, typeface, fill, width):
    box = draw.textbbox((0, 0), text, font=typeface)
    draw.text(((width - (box[2] - box[0])) / 2, y), text, font=typeface, fill=fill)


def logo_at(side):
    return Image.open(LOGO).convert("RGBA").resize((side, side), Image.LANCZOS)


def poster(lang, width, height, path):
    canvas = Image.new("RGB", (width, height), BACKGROUND)
    draw = ImageDraw.Draw(canvas)
    side = int(width * 0.62)
    mark = logo_at(side)
    canvas.paste(mark, ((width - side) // 2, int(height * 0.16)), mark)
    y = int(height * 0.16) + side + int(height * 0.05)
    centered(draw, y, "MrW Converter", font(int(width * 0.095)), TEXT, width)
    for index, line in enumerate(TAGLINE[lang]):
        centered(draw, y + int(width * 0.16) + index * int(width * 0.075), line, font(int(width * 0.055), False), SUBTEXT, width)
    canvas.save(path, optimize=True)


def box_art(side, path):
    canvas = Image.new("RGB", (side, side), BACKGROUND)
    inner = int(side * 0.72)
    mark = logo_at(inner)
    canvas.paste(mark, ((side - inner) // 2, (side - inner) // 2), mark)
    canvas.save(path, optimize=True)


def fit(image, box_width, box_height):
    scale = min(box_width / image.width, box_height / image.height, 1)
    if scale == 1:
        return image
    return image.resize((int(image.width * scale), int(image.height * scale)), Image.LANCZOS)


def scene(name, lang):
    """The picture for one screenshot, or None when its raw pictures aren't there."""
    if name == "background":
        before = SITE_IMAGES / "bike-before.jpg"
        after = SITE_IMAGES / "bike-after.png"
        if not before.exists() or not after.exists():
            return None
        left = Image.open(before).convert("RGBA")
        right = Image.open(after).convert("RGBA")
        check = Image.new("RGBA", right.size, (223, 229, 240, 255))
        draw = ImageDraw.Draw(check)
        for y in range(0, right.height, 22):
            for x in range(0, right.width, 22):
                if (x + y) // 22 % 2:
                    draw.rectangle((x, y, x + 21, y + 21), fill=(197, 205, 218, 255))
        check.alpha_composite(right)
        joined = Image.new("RGBA", (left.width * 2 + 24, left.height), (0, 0, 0, 0))
        joined.paste(left, (0, 0))
        joined.paste(check, (left.width + 24, 0))
        return joined

    if name == "wheel":
        main_path, wheel_path = RAW / f"{lang}-main.png", RAW / f"{lang}-wheel.png"
        if not main_path.exists() or not wheel_path.exists():
            return None
        main = Image.open(main_path).convert("RGBA")
        wheel = Image.open(wheel_path).convert("RGBA")
        # The window draws on nothing, which a capture shows as black: keep only the ring.
        mask = Image.new("L", wheel.size, 0)
        ImageDraw.Draw(mask).ellipse((0, 0, wheel.width - 1, wheel.height - 1), fill=255)
        hole = int(wheel.width * 0.17)
        centre = (wheel.width // 2, wheel.height // 2)
        ImageDraw.Draw(mask).ellipse((centre[0] - hole, centre[1] - hole, centre[0] + hole, centre[1] + hole), fill=0)
        wheel.putalpha(mask)
        main.alpha_composite(wheel, ((main.width - wheel.width) // 2, (main.height - wheel.height) // 2))
        return main

    path = RAW / f"{lang}-{name}.png"
    return Image.open(path).convert("RGBA") if path.exists() else None


def screenshot(picture, headline, subline, path):
    canvas = Image.new("RGB", (1920, 1080), BACKGROUND)
    draw = ImageDraw.Draw(canvas)
    centered(draw, 44, headline, font(52), TEXT, 1920)
    centered(draw, 116, subline, font(28, False), SUBTEXT, 1920)
    shown = fit(picture, 1720, 860)
    x, y = (1920 - shown.width) // 2, 180 + (880 - shown.height) // 2
    draw.rectangle((x - 1, y - 1, x + shown.width, y + shown.height), outline=PANEL)
    canvas.paste(shown, (x, y), shown)
    canvas.save(path, optimize=True)


for lang in ("tr", "en"):
    logos = OUT / lang / "logos"
    logos.mkdir(parents=True, exist_ok=True)
    poster(lang, 720, 1080, logos / "poster-720x1080.png")
    poster(lang, 1440, 2160, logos / "poster-1440x2160.png")
    box_art(1080, logos / "boxart-1080x1080.png")
    box_art(2160, logos / "boxart-2160x2160.png")
    logo_at(300).save(logos / "apptile-300x300.png", optimize=True)

    shots = OUT / lang / "screenshots"
    shots.mkdir(parents=True, exist_ok=True)
    for number, (name, headline, subline) in enumerate(SHOTS[lang], 1):
        picture = scene(name, lang)
        if picture is None:
            print(f"no raw picture for {lang}-{name}")
            continue
        screenshot(picture, headline, subline, shots / f"{number}-{name}.png")
