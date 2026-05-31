"""Generate a futuristic 3D-style app icon for Music Widget -> icon.ico (+ preview png)."""
from PIL import Image, ImageDraw, ImageFilter
import math

def lerp(a, b, t): return tuple(int(a[i] + (b[i]-a[i])*t) for i in range(3))

def make(size):
    S = size * 4  # supersample
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Rounded-square base with diagonal gradient (deep violet -> cyan): futuristic
    top = (124, 58, 237)    # violet
    bot = (34, 211, 238)    # cyan
    r = int(S * 0.22)
    pad = int(S * 0.06)
    grad = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=lerp(top, bot, t) + (255,))
    mask = Image.new("L", (S, S), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([pad, pad, S - pad, S - pad], radius=r, fill=255)
    base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    base.paste(grad, (0, 0), mask)

    # Top glossy highlight (3D sheen)
    gloss = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gl = ImageDraw.Draw(gloss)
    gl.rounded_rectangle([pad, pad, S - pad, int(S * 0.5)], radius=r, fill=(255, 255, 255, 60))
    gloss = gloss.filter(ImageFilter.GaussianBlur(S * 0.02))
    base = Image.alpha_composite(base, Image.composite(gloss, Image.new("RGBA",(S,S),(0,0,0,0)), mask))

    # Neon equalizer bars + a play triangle (music + futuristic)
    fg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fg)
    cx, cy = S // 2, S // 2
    bar_w = int(S * 0.07)
    gap = int(S * 0.045)
    heights = [0.20, 0.36, 0.52, 0.34, 0.22]
    total_w = len(heights) * bar_w + (len(heights) - 1) * gap
    x0 = cx - total_w // 2
    for i, h in enumerate(heights):
        bh = int(S * h)
        x = x0 + i * (bar_w + gap)
        fd.rounded_rectangle([x, cy - bh // 2, x + bar_w, cy + bh // 2],
                             radius=bar_w // 2, fill=(255, 255, 255, 235))
    # subtle glow
    glow = fg.filter(ImageFilter.GaussianBlur(S * 0.012))
    base = Image.alpha_composite(base, glow)
    base = Image.alpha_composite(base, fg)

    # Outer soft drop shadow under the rounded square (3D lift)
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle([pad, pad + int(S*0.03), S - pad, S - pad + int(S*0.03)],
                         radius=r, fill=(10, 10, 30, 120))
    shadow = shadow.filter(ImageFilter.GaussianBlur(S * 0.025))
    out = Image.alpha_composite(shadow, base)

    return out.resize((size, size), Image.LANCZOS)

sizes = [16, 24, 32, 48, 64, 128, 256]
imgs = [make(s) for s in sizes]
imgs[0].save("icon.ico", format="ICO", sizes=[(s, s) for s in sizes],
             append_images=imgs[1:])
make(256).save("icon-preview.png")
print("wrote icon.ico + icon-preview.png")
