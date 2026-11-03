"""One More Station - trailer v2: cuts the captured footage to the original theme "Next Stop".

  python edit.py [footage_dir] [out.mp4]

Footage comes from "MetroSimulator.exe -autotest trailer" (Assets/Scripts/Testing/AutoTest.Trailer.cs): numbered
frames plus scene and event markers in autotest_log.txt. The music runs at 120 BPM, so a beat is exactly 15 frames;
every cut below sits on a beat, and key moments (the first line opening, the town growing, the growth card) are
lined up with the hits in the score (see music.py).
"""
import math
import os
import re
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))   # Steam/store
import storeart as A  # noqa: E402  (logo, palette and fonts from the store art)

FOOT = sys.argv[1] if len(sys.argv) > 1 else r"E:\UnityProjects\MetroSimulator\Builds\Trailer\footage_v2"
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(HERE), "one_more_station_trailer_v2.mp4")
MUSIC = os.path.join(HERE, "next_stop_theme.wav")
W, H, FPS = 1920, 1080, 30
LENGTH = 71.5

# ---------------------------------------------------------------------------------------- footage markers
scenes, events = {}, {}
with open(os.path.join(FOOT, "autotest_log.txt"), encoding="utf-8", errors="replace") as f:
    for line in f:
        m = re.match(r"scene (\S+) (start|end) (\d+)", line)
        if m:
            scenes.setdefault(m[1], {})[m[2]] = int(m[3])
        m = re.match(r"event (\S+) (\d+)", line)
        if m:
            events[m[1]] = int(m[2])


def S(scene, off=0):
    return ("scene", scene, scenes[scene]["start"] + off)


def E(event, off=0):
    scene = next(n for n, r in scenes.items() if r["start"] <= events[event] <= r.get("end", 10 ** 9))
    return ("scene", scene, events[event] + off)


# ---------------------------------------------------------------------------------------- the cut (seconds)
SEG = [
    (0.0, 4.0, S("hero", 0), {}),                      # cold open: the metropolis at rush hour
    (4.0, 6.0, None, {"card": "title"}),               # TITLE on the first hit
    (6.0, 9.5, S("town_intro", 70), {}),               # a small town at first light
    (9.5, 12.0, E("line_built", -75), {}),             # the first line, stop by stop...
    (12.0, 14.0, E("line_built", 5), {}),              # ...opens on bar 7 (skips the panel's 4-frame flash)
    (14.0, 18.0, S("rush", 50), {}),                   # the morning rush
    (18.0, 20.0, E("train_added_1", -15), {}),         # more trains (+ on the beat)
    (20.0, 22.0, E("grow", -60), {}),                  # the night lifts...
    (22.0, 24.0, E("grow", 0), {"hit": True}),         # ...the town grows on the chorus
    (24.0, 26.0, E("card", 0), {}),                    # the growth card on bar 13
    (26.0, 27.0, E("line2_built", -30), {}),           # a second line, the first interchange,
    (27.0, 28.0, E("line2_built", 5), {}),             # built on 27.0 (skipping the panel flash)
    (28.0, 30.0, S("pencil", 8), {}),                  # plans in pencil
    (30.0, 32.0, S("market_pan", 40), {}),             # a market town humming
    (32.0, 36.0, E("letter", -10), {}),                # City Hall writes
    (36.0, 40.0, S("matchday", 40), {}),               # the final whistle: everyone at once
    (40.0, 42.0, E("session", -15), {}),               # election day at the council (dialog on 40.5)
    (42.0, 44.0, S("wide_pan", 40), {}),               # breakdown: a calm look across the city
    (44.0, 46.0, S("close_1", 15), {"hit": True}),     # final chorus
    (46.0, 48.0, S("close_2", 15), {}),
    (48.0, 50.0, S("close_3", 15), {}),
    (50.0, 52.0, S("hero", 100), {}),
    (52.0, 58.0, S("zoom_out", 8), {}),                # one platform to the whole region
    (58.0, 62.0, S("night", 20), {"hit": True}),       # resolution: night falls
    (62.0, 64.0, E("stars", 0), {}),                   # three stars
    (64.0, LENGTH, None, {"card": "end"}),
]

CAPTIONS = [  # text, colour, from, to
    ("START WITH A SMALL TOWN", A.SUN, 6.3, 9.35),
    ("DRAW YOUR FIRST LINE", A.SKY, 9.75, 11.9),
    ("SURVIVE THE RUSH HOUR", A.TOMATO, 14.3, 17.85),
    ("GROW OVERNIGHT", A.GRAPE, 20.3, 23.85),
    ("PLAN IN PENCIL", A.MINT, 28.25, 29.9),
    ("CITY HALL WANTS THINGS", A.GUM, 32.3, 35.85),
    ("GET THE FANS HOME", A.TANG, 36.3, 39.85),
    ("KEEP THE MAYOR HAPPY", A.SKY, 40.25, 41.9),
    ("KEEP MILLIONS MOVING", A.MINT, 46.25, 49.85),
    ("FROM SMALL TOWN TO METROPOLIS", A.SUN, 54.6, 57.9),
    ("EARN ALL THE STARS", A.GRAPE, 62.25, 63.9),
]
FLASHES = [4.0, 22.0, 44.0, 58.0]


# ---------------------------------------------------------------------------------------- frames
_cache = {}


_bad = {}


def receipt_flash(n):
    """True for the odd frame where the build receipt under the cursor turns red ("can't build here"): the instant
    a stop is placed the cursor sits on it, and crossing the river is no place for a station. Real feedback, but in
    a trailer it reads as an error, so those frames are swapped for the clean frame before them."""
    if n not in _bad:
        a = np.asarray(Image.open(os.path.join(FOOT, "frames", f"f_{n:05d}.jpg")).convert("RGB")).astype(np.int16)
        pink = (a[..., 0] > 230) & (a[..., 1] > 175) & (a[..., 1] < 215) & (a[..., 2] > 175) & (a[..., 2] < 220) & ((a[..., 0] - a[..., 1]) > 30)
        _bad[n] = int(pink.sum()) > 10000
    return _bad[n]


def frame(scene, n):
    r = scenes[scene]
    n = max(r["start"], min(r["end"], n))
    if scene in ("draw_line", "second_line"):
        k = 0
        while k < 6 and n > r["start"] and receipt_flash(n):
            n -= 1
            k += 1
    if n not in _cache:
        if len(_cache) > 40:
            _cache.clear()
        _cache[n] = Image.open(os.path.join(FOOT, "frames", f"f_{n:05d}.jpg")).convert("RGB")
    return _cache[n]


def source_at(t):
    for t0, t1, src, opt in SEG:
        if t0 <= t < t1:
            return t0, t1, src, opt
    return SEG[-1]


# ---------------------------------------------------------------------------------------- animation helpers

def ease_out_back(x, s=1.9):
    x = min(max(x, 0.0), 1.0) - 1
    return x * x * ((s + 1) * x + s) + 1


def smooth(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


_stickers = {}


def sticker(text, col, fs=62, fg=None):
    key = (text, col, fs)
    if key in _stickers:
        return _stickers[key]
    K = 2
    font = ImageFont.truetype(A.LUCKIEST, fs * K)
    bb = font.getbbox(text)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    px, py = int(fs * K * 0.58), int(fs * K * 0.36)
    bw, bh = tw + 2 * px, th + 2 * py
    sh, ow = int(fs * K * 0.13), max(4, fs * K // 12)
    im = Image.new("RGBA", (bw + sh + 10, bh + sh + 10), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = int(bh * 0.34)
    d.rounded_rectangle([sh, sh, sh + bw, sh + bh], r, fill=A.INK)
    d.rounded_rectangle([0, 0, bw, bh], r, fill=col, outline=A.INK, width=ow)
    d.text((px - bb[0], py - bb[1] + int(fs * K * 0.03)), text, font=font, fill=fg or A.INK)
    im = im.resize((im.width // K, im.height // K), Image.LANCZOS)
    _stickers[key] = im
    return im


def paste_sticker(img, st, cx, cy, scale, angle, alpha=1.0):
    if scale <= 0.01 or alpha <= 0.01:
        return
    s = st.resize((max(1, int(st.width * scale)), max(1, int(st.height * scale))), Image.BICUBIC)
    s = s.rotate(angle, resample=Image.BICUBIC, expand=True)
    if alpha < 1:
        a = s.getchannel("A").point(lambda v: int(v * alpha))
        s.putalpha(a)
    img.alpha_composite(s, (int(cx - s.width / 2), int(cy - s.height / 2)))


def caption(img, t):
    for text, col, a, b in CAPTIONS:
        if a <= t < b:
            st = sticker(text, col)
            k_in = (t - a) * FPS / 9.0
            k_out = (b - t) * FPS / 6.0
            scale = ease_out_back(k_in) if k_in < 1 else 1.0
            alpha = 1.0
            if k_out < 1:
                scale *= 0.85 + 0.15 * k_out
                alpha = k_out
            ang = -2.0 + 1.1 * math.sin(t * 2.6)
            paste_sticker(img, st, W / 2, H * 0.84, scale, ang, alpha)


# ---------------------------------------------------------------------------------------- cards

LOGO = A.render_logo(330)
LOGO_SMALL = A.render_logo(270)
_blur = {}


def blurred(scene, n, radius=16, dim=0.42):
    key = (scene, n, radius)
    if key not in _blur:
        im = frame(scene, n).filter(ImageFilter.GaussianBlur(radius))
        im = Image.blend(im, Image.new("RGB", im.size, A.INK), dim)
        _blur.clear()
        _blur[key] = im
    return _blur[key]


def title_card(t):
    k = t - 4.0
    bg = blurred("hero", scenes["hero"]["start"] + 119, 18, 0.48).copy().convert("RGBA")
    z = 1.0 + 0.03 * k
    bg = bg.resize((int(W * z), int(H * z)), Image.BICUBIC).crop((int((W * z - W) / 2), int((H * z - H) / 2), int((W * z - W) / 2) + W, int((H * z - H) / 2) + H))
    sc = ease_out_back(k * FPS / 11.0)
    paste_sticker(bg, LOGO, W / 2, H * 0.44, sc, 0.0)
    if k > 0.45:
        tag = sticker("A METRO NETWORK MANAGEMENT SIM", A.CREAM, 40)
        paste_sticker(bg, tag, W / 2, H * 0.44 + LOGO.height * 0.62, ease_out_back((k - 0.45) * FPS / 9.0), -1.5)
    return bg


def end_card(t):
    k = t - 64.0
    r = scenes["night"]
    n = min(r["end"], r["start"] + 140 + int(k * FPS * 0.5))
    bg = blurred("night", n, 14, 0.5).copy().convert("RGBA")
    sc = ease_out_back(k * FPS / 11.0)
    paste_sticker(bg, LOGO_SMALL, W / 2, H * 0.40, sc, 0.0)
    if k > 0.9:
        st = sticker("WISHLIST NOW ON STEAM", A.SUN, 58)
        paste_sticker(bg, st, W / 2, H * 0.40 + LOGO_SMALL.height * 0.66, ease_out_back((k - 0.9) * FPS / 9.0), -2.0 + 0.8 * math.sin(t * 2.2))
    return bg


# ---------------------------------------------------------------------------------------- render

def render(n):
    t = n / FPS
    t0, t1, src, opt = source_at(t)
    if opt.get("card") == "title":
        img = title_card(t)
    elif opt.get("card") == "end":
        img = end_card(t)
    else:
        _, scene, base = src
        img = frame(scene, base + int(round((t - t0) * FPS))).convert("RGBA")
    # a small zoom punch and a white flash on the big hits
    for h in FLASHES:
        d = (t - h) * FPS
        if 0 <= d < 9:
            if d < 8 and opt.get("card") != "title":
                z = 1.0 + 0.045 * (1 - smooth(d / 8.0))
                big = img.resize((int(W * z), int(H * z)), Image.BICUBIC)
                ox, oy = (big.width - W) // 2, (big.height - H) // 2
                img = big.crop((ox, oy, ox + W, oy + H))
            a = int(255 * 0.55 * max(0.0, 1 - d / 5.0))
            if a > 0:
                img = Image.alpha_composite(img, Image.new("RGBA", (W, H), (255, 250, 235, a)))
    caption(img, t)
    # fade to black at the very end
    if t > LENGTH - 0.9:
        a = int(255 * smooth((t - (LENGTH - 0.9)) / 0.9))
        img = Image.alpha_composite(img, Image.new("RGBA", (W, H), (0, 0, 0, a)))
    return img.convert("RGB")


def main():
    total = int(round(LENGTH * FPS))
    cmd = [
        "ffmpeg", "-y", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
        "-i", MUSIC,
        "-map", "0:v", "-map", "1:a",
        "-c:v", "libx264", "-preset", "slow", "-crf", "15", "-pix_fmt", "yuv420p", "-profile:v", "high",
        "-g", "60", "-bf", "2",
        "-c:a", "aac", "-b:a", "320k", "-ar", "48000",
        "-af", f"afade=t=out:st={LENGTH - 1.0:.2f}:d=1.0",
        "-t", f"{LENGTH:.3f}", "-movflags", "+faststart", OUT,
    ]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for n in range(total):
        p.stdin.write(render(n).tobytes())
        if n % 150 == 0:
            print(f"  {n / FPS:5.1f} s", flush=True)
    p.stdin.close()
    p.wait()
    print("wrote", OUT)


if __name__ == "__main__":
    if len(sys.argv) > 3 and sys.argv[3] == "--stills":
        os.makedirs(os.path.join(HERE, "stills"), exist_ok=True)
        for tt in [float(x) for x in sys.argv[4].split(",")]:
            render(int(round(tt * FPS))).save(os.path.join(HERE, "stills", f"t{tt:05.2f}.jpg"), quality=88)
        print("stills written")
    else:
        main()
