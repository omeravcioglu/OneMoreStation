"""Doodle-style client/community icon for One More Station: a red line ending in a sunny '+' station."""
import os
from PIL import Image, ImageDraw

OUT = os.path.dirname(os.path.abspath(__file__))
INK = (43, 30, 63); CREAM = (255, 246, 227); SUN = (255, 210, 63)
TOMATO = (255, 90, 78); SKY = (79, 195, 247); MINT = (95, 224, 176)


def icon(size=1024):
    S = size
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    m = int(S * 0.04); r = int(S * 0.22); bw = int(S * 0.035); sh = int(S * 0.035)
    d.rounded_rectangle([m + sh, m + sh, S - m, S - m], r, fill=INK)
    d.rounded_rectangle([m, m, S - m - sh, S - m - sh], r, fill=CREAM, outline=INK, width=bw)
    lw = int(S * 0.085); ow = int(S * 0.025)

    def line(pts, col):
        d.line(pts, fill=INK, width=lw + 2 * ow, joint="curve")
        d.line(pts, fill=col, width=lw, joint="curve")

    def stop(c, rr, fill=(255, 255, 255)):
        d.ellipse([c[0] - rr - ow, c[1] - rr - ow, c[0] + rr + ow, c[1] + rr + ow], fill=INK)
        d.ellipse([c[0] - rr, c[1] - rr, c[0] + rr, c[1] + rr], fill=fill)

    f = lambda x, y: (int(S * x), int(S * y))
    line([f(0.24, 0.16), f(0.24, 0.40), f(0.52, 0.68), f(0.52, 0.86)], SKY)
    line([f(0.14, 0.68), f(0.40, 0.68), f(0.62, 0.46), f(0.70, 0.46)], TOMATO)
    stop(f(0.24, 0.30), S * 0.045)
    stop(f(0.40, 0.68), S * 0.065)  # interchange
    stop(f(0.52, 0.80), S * 0.045)
    stop(f(0.24, 0.68), S * 0.045)
    # the new station: big sunny plus
    c = f(0.72, 0.44); R = S * 0.13
    d.ellipse([c[0] - R + sh, c[1] - R + sh, c[0] + R + sh, c[1] + R + sh], fill=INK)
    d.ellipse([c[0] - R, c[1] - R, c[0] + R, c[1] + R], fill=SUN, outline=INK, width=bw)
    p = R * 0.52; pw = int(R * 0.3)
    d.line([(c[0] - p, c[1]), (c[0] + p, c[1])], fill=INK, width=pw)
    d.line([(c[0], c[1] - p), (c[0], c[1] + p)], fill=INK, width=pw)
    return im


if __name__ == "__main__":
    big = icon(1024)
    big.resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, "client_icon_512.png"))
    big.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "shortcut_icon_256.png"))
    big.resize((184, 184), Image.LANCZOS).convert("RGB").save(os.path.join(OUT, "community_icon_184x184.jpg"), quality=95)
    big.save(os.path.join(OUT, "client_icon.ico"), sizes=[(16, 16), (32, 32), (48, 48), (256, 256)])
    big.resize((32, 32), Image.LANCZOS).save(os.path.join(OUT, "client_icon_32x32.tga"))
    print("ok")
