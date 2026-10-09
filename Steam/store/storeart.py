"""Generates Steam store / library art for One More Station in the Doodle Metro style."""
import math, os, random, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

PROJ = r"E:\UnityProjects\MetroSimulator"
OUT = os.path.join(PROJ, "Steam", "store")
FONTS = os.path.join(PROJ, "Assets", "Resources", "Fonts")
os.makedirs(OUT, exist_ok=True)

INK = (43, 30, 63)
CREAM = (255, 246, 227)
SAND = (243, 228, 192)
MAPBG = (240, 233, 218)
PARK = (214, 232, 200)
RIVER = (196, 222, 240)
SUN = (255, 210, 63); GUM = (255, 126, 182); MINT = (95, 224, 176); SKY = (79, 195, 247)
GRAPE = (161, 120, 255); TANG = (255, 154, 61); TOMATO = (255, 90, 78)
LRED = (235, 64, 60); LBLUE = (40, 100, 220); LGREEN = (40, 160, 90); LYEL = (240, 180, 20)
LPURP = (140, 70, 200); LORNG = (245, 130, 30); LPINK = (225, 80, 150); LBROWN = (120, 80, 50)

LUCKIEST = os.path.join(FONTS, "LuckiestGuy-Regular.ttf")
SHANTELL = os.path.join(FONTS, "ShantellSans-ExtraBold.ttf")

# ---------------------------------------------------------------- master map (grid units, 48 x 24)
GW, GH = 48, 24
LINES = [
    (LRED,   [(-2, 15), (6, 15), (11, 10), (33, 10), (38, 5), (50, 5)]),
    (LBLUE,  [(14, -2), (14, 4), (20, 10), (20, 15), (29, 24), (29, 26)]),
    (LGREEN, [(2, -2), (2, 3), (8, 3), (15, 10), (15, 15), (21, 21), (40, 21), (44, 17), (50, 17)]),
    (LYEL,   [(50, 13), (37, 13), (34, 10), (26, 10), (26, 4), (32, -2)]),
    (LPURP,  [(-2, 6), (6, 6), (13, 13), (26, 13), (31, 18), (41, 18), (47, 24), (47, 26)]),
    (LORNG,  [(34, -2), (34, 2), (22, 14), (22, 17), (16, 23), (12, 23), (10, 26)]),
    (LBROWN, [(-2, 20), (5, 20), (9, 24), (9, 26)]),
]
LOOP = (LPINK, [(17, 6), (23, 6), (27, 10), (27, 12), (24, 15), (16, 15), (13, 12), (13, 9), (16, 6)])
LOOP = (LPINK, [(17, 5), (23, 5), (28, 10), (28, 12), (25, 16), (16, 16), (12, 12), (12, 9), (16, 5), (17, 5)])
RIVER_PTS = [(-2, 1), (6, 2.5), (11, 6), (17, 8.5), (23, 11.5), (28, 15), (33, 16), (39, 19.5), (44, 22), (50, 23)]
PARKS = [(7, 9, 1.6), (31, 7, 1.4), (19, 19, 1.8), (40, 14, 1.2), (4, 17, 1.1), (24, 2, 1.2), (44, 9, 1.5)]


def catmull(pts, n=16):
    out = []
    p = [pts[0]] + pts + [pts[-1]]
    for i in range(1, len(p) - 2):
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[j]) + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2 + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in range(2)))
    out.append(pts[-1])
    return out


def stations_of(pts, spacing=3.2):
    """Stations at every vertex plus evenly along long segments."""
    res = []
    for a, b in zip(pts, pts[1:]):
        L = math.dist(a, b)
        n = max(1, round(L / spacing))
        for k in range(n):
            t = k / n
            res.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    res.append(pts[-1])
    return res


# ---------------------------------------------------------------- storybook map (matches the in-game map restyle)
H = lambda s: tuple(int(s[i:i + 2], 16) for i in (1, 3, 5))
LAND = H("#F3E4C0"); LAND_EDGE = (222, 204, 166)
TOWN_TINTS = [H("#F1CDB0"), H("#E2DEF2"), H("#DEDBE6")]
WATER = H("#6DCBF5"); SHORE = H("#3F8FC0"); GLINT = H("#C4EEFF"); BANK = H("#A9DE88")
PARKF = H("#A9DE88"); PARKE = H("#6FAE4F"); TREES = [H("#7FCF5C"), H("#9FDB7C")]; TREE_INK = H("#4E8A36")
WALLS = [H("#FFF9EC"), H("#FFE6D2"), H("#F3E6FF"), H("#E9F8E1")]
ROOFS = [H("#F4876A"), H("#8DBDF0"), H("#BE93E0"), H("#F4B84E")]
MOODS = [H("#7EE0A8"), H("#7EE0A8"), H("#FFD84D"), H("#FF9F45")]
CLOUD = (255, 255, 255); CLOUD_SH = H("#E6F0FA")
NEW_LINE_COLS = [H("#FF5146"), H("#3D86F5"), H("#35B864"), H("#FF8F1F"), H("#9A5BF0"), H("#FFC929"), H("#A8693F"), H("#FF62AE")]


def _seg_dist(p, a, b):
    ax, ay = a; bx, by = b; px, py = p
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy or 1e-9
    t = max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / L2))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def render_map(w, h, view, seed=3, trains=True, labels_font=None, dim=0.0, clouds=False):
    """Storybook map. view = (x0, y0, x1, y1) in grid units. Rendered at 2x then downsampled."""
    S = 2
    W, Hh = w * S, h * S
    x0, y0, x1, y1 = view
    sc = W / (x1 - x0)
    cy = (y0 + y1) / 2
    hh = Hh / sc / 2
    y0, y1 = cy - hh, cy + hh
    tx = lambda p: ((p[0] - x0) * sc, (p[1] - y0) * sc)
    u = sc
    rnd = random.Random(seed)

    # land with soft town tints per district
    sites = np.array([(rnd.uniform(-4, GW + 4), rnd.uniform(-4, GH + 4)) for _ in range(70)])
    lw, lh = max(2, W // 4), max(2, Hh // 4)
    gx = np.linspace(x0, x1, lw)[None, :]
    gy = np.linspace(y0, y1, lh)[:, None]
    lab = ((gx[..., None] - sites[:, 0]) ** 2 + (gy[..., None] - sites[:, 1]) ** 2).argmin(-1)
    pal = np.array([LAND] * len(sites), dtype=np.float32)
    for i in range(len(sites)):
        if rnd.random() < 0.45:
            t = TOWN_TINTS[rnd.randrange(3)]
            pal[i] = np.array(LAND) * 0.6 + np.array(t) * 0.4
    base = pal[lab]
    edge = np.zeros_like(lab, dtype=bool)
    edge[:, 1:] |= lab[:, 1:] != lab[:, :-1]
    edge[1:, :] |= lab[1:, :] != lab[:-1, :]
    base[edge] = np.array(LAND_EDGE)
    img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8)).resize((W, Hh), Image.NEAREST).filter(ImageFilter.GaussianBlur(max(1, W / 900)))
    d = ImageDraw.Draw(img)

    all_lines = [(NEW_LINE_COLS[i % len(NEW_LINE_COLS)], pts) for i, (_, pts) in enumerate(LINES)] + [(NEW_LINE_COLS[7], LOOP[1])]
    river = catmull(RIVER_PTS)

    def near_stuff(p, pad):
        if any(_seg_dist(p, a, b) < pad for _, pts in all_lines for a, b in zip(pts, pts[1:])):
            return True
        return any(math.dist(p, q) < pad + 0.9 for q in river[::3])

    # river: banks, shoreline, water, glint
    rp = [tx(p) for p in river]
    d.line(rp, fill=BANK, width=int(u * 1.9), joint="curve")
    d.line(rp, fill=SHORE, width=int(u * 1.45), joint="curve")
    d.line(rp, fill=WATER, width=int(u * 1.2), joint="curve")
    d.line([(x, y - u * 0.18) for x, y in rp], fill=GLINT, width=max(2, int(u * 0.12)), joint="curve")

    # parks with trees
    for (px, py, r) in PARKS:
        cx, cy2 = tx((px, py)); rr = r * u
        d.ellipse([cx - rr, cy2 - rr * 0.8, cx + rr, cy2 + rr * 0.8], fill=PARKF, outline=PARKE, width=max(2, int(u * 0.08)))
        for _ in range(int(r * 4)):
            a = rnd.uniform(0, 6.28); k = rnd.uniform(0, 0.7)
            tc = (cx + math.cos(a) * rr * k, cy2 + math.sin(a) * rr * 0.8 * k)
            tr_ = u * rnd.uniform(0.2, 0.3)
            d.ellipse([tc[0] - tr_, tc[1] - tr_, tc[0] + tr_, tc[1] + tr_], fill=rnd.choice(TREES), outline=TREE_INK, width=max(1, int(u * 0.05)))

    # little houses dotted around the towns
    for _ in range(int((x1 - x0) * (y1 - y0) * 0.22)):
        p = (rnd.uniform(x0, x1), rnd.uniform(y0, y1))
        if near_stuff(p, 0.75) or any(math.dist(p, (q[0], q[1])) < q[2] + 0.3 for q in PARKS):
            continue
        c = tx(p); s = u * rnd.uniform(0.26, 0.36); ol = max(1, int(u * 0.05))
        d.rectangle([c[0] - s, c[1] - s * 0.6, c[0] + s, c[1] + s * 0.9], fill=rnd.choice(WALLS), outline=INK, width=ol)
        d.polygon([(c[0] - s * 1.25, c[1] - s * 0.55), (c[0], c[1] - s * 1.5), (c[0] + s * 1.25, c[1] - s * 0.55)], fill=rnd.choice(ROOFS), outline=INK)

    lwid = max(4, int(u * 0.42))
    casing = int(lwid * 1.53)

    def poly(col, pts, width):
        P = [tx(p) for p in pts]
        d.line(P, fill=col, width=width, joint="curve")
        r = width / 2
        for q in (P[0], P[-1]):
            d.ellipse([q[0] - r, q[1] - r, q[0] + r, q[1] + r], fill=col)

    for col, pts in all_lines:
        poly(INK, pts, casing)
    for col, pts in all_lines:
        poly(col, pts, lwid)
    ow = max(2, (casing - lwid) // 2)

    # gumball trains: 2-3 shiny balls in the line colour
    if trains:
        tr = random.Random(seed + 7)
        for col, pts in all_lines:
            for a, b in zip(pts, pts[1:]):
                L = math.dist(a, b)
                if L < 3:
                    continue
                for _ in range(1 if L < 8 else 2):
                    t = tr.uniform(0.2, 0.75)
                    ux, uy = (b[0] - a[0]) / L, (b[1] - a[1]) / L
                    n = tr.choice((2, 3))
                    br = lwid * 0.62
                    for k in range(n):
                        p = (a[0] + (b[0] - a[0]) * t + ux * k * 0.42, a[1] + (b[1] - a[1]) * t + uy * k * 0.42)
                        c = tx(p)
                        d.ellipse([c[0] - br - ow, c[1] - br - ow, c[0] + br + ow, c[1] + br + ow], fill=INK)
                        d.ellipse([c[0] - br, c[1] - br, c[0] + br, c[1] + br], fill=col)
                        hr = br * 0.32
                        d.ellipse([c[0] - br * 0.45 - hr, c[1] - br * 0.45 - hr, c[0] - br * 0.45 + hr, c[1] - br * 0.45 + hr], fill=(255, 255, 255))

    # stations with mood faces; interchanges white with a mood dot
    def seg_int(a, b, c, e):
        d1 = (b[0] - a[0], b[1] - a[1]); d2 = (e[0] - c[0], e[1] - c[1])
        den = d1[0] * d2[1] - d1[1] * d2[0]
        if abs(den) < 1e-9:
            return None
        t = ((c[0] - a[0]) * d2[1] - (c[1] - a[1]) * d2[0]) / den
        v = ((c[0] - a[0]) * d1[1] - (c[1] - a[1]) * d1[0]) / den
        if 0 <= t <= 1 and 0 <= v <= 1:
            return (a[0] + d1[0] * t, a[1] + d1[1] * t)
        return None

    def face(c, r, fill):
        d.ellipse([c[0] - r - ow, c[1] - r - ow, c[0] + r + ow, c[1] + r + ow], fill=INK)
        d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=fill)
        er = max(1, r * 0.13)
        for sx in (-0.33, 0.33):
            e = (c[0] + r * sx, c[1] - r * 0.15)
            d.ellipse([e[0] - er, e[1] - er, e[0] + er, e[1] + er], fill=INK)
        d.arc([c[0] - r * 0.38, c[1] - r * 0.25, c[0] + r * 0.38, c[1] + r * 0.45], 20, 160, fill=INK, width=max(1, int(r * 0.14)))

    inter = []
    for i in range(len(all_lines)):
        for j in range(i + 1, len(all_lines)):
            A, B = all_lines[i][1], all_lines[j][1]
            for a, b in zip(A, A[1:]):
                for c, e in zip(B, B[1:]):
                    p = seg_int(a, b, c, e)
                    if p and not any(math.dist(p, q) < 1.0 for q in inter):
                        inter.append(p)
    mr = random.Random(seed + 11)
    for col, pts in all_lines:
        placed = []
        for s_ in stations_of(pts):
            if any(math.dist(s_, q) < 1.3 for q in inter) or any(math.dist(s_, q) < 1.5 for q in placed):
                continue
            placed.append(s_)
            face(tx(s_), lwid * 0.78, mr.choice(MOODS))
    for q in inter:
        c = tx(q); r = lwid * 1.02
        face(c, r, (255, 255, 255))
        dr = r * 0.3; dc = (c[0] + r * 0.72, c[1] - r * 0.72)
        d.ellipse([dc[0] - dr - ow * 0.6, dc[1] - dr - ow * 0.6, dc[0] + dr + ow * 0.6, dc[1] + dr + ow * 0.6], fill=INK)
        d.ellipse([dc[0] - dr, dc[1] - dr, dc[0] + dr, dc[1] + dr], fill=mr.choice(MOODS))

    # puffy clouds over the unexplored edges
    if clouds:
        cr = random.Random(seed + 23)
        for _ in range(7):
            side = cr.choice(("t", "b"))
            cx = cr.uniform(0, W); cyy = cr.uniform(-0.02, 0.07) * Hh if side == "t" else Hh - cr.uniform(-0.02, 0.07) * Hh
            base_r = u * cr.uniform(1.0, 1.6)
            blobs = [(cx + k * base_r * 0.9, cyy + cr.uniform(-0.3, 0.3) * base_r, base_r * cr.uniform(0.7, 1.1)) for k in range(-2, 3)]
            for bx, by, br in blobs:
                d.ellipse([bx - br - ow * 1.5, by - br - ow * 1.5, bx + br + ow * 1.5, by + br + ow * 1.5], fill=INK)
            for bx, by, br in blobs:
                d.ellipse([bx - br, by - br, bx + br, by + br], fill=CLOUD_SH)
            for bx, by, br in blobs:
                d.ellipse([bx - br * 0.92, by - br, bx + br * 0.86, by + br * 0.8], fill=CLOUD)

    if dim > 0:
        img = Image.blend(img, Image.new("RGB", img.size, CREAM), dim)
    return img.resize((w, h), Image.LANCZOS)


# ---------------------------------------------------------------- logo
RAINBOW = [TOMATO, SUN, MINT, SKY, GRAPE, TANG, GUM]
TILTS = [-6, 4, -3, 5, -4, 3, -5]


def render_word(word, fs, col_offset=0, tilt_scale=1.0):
    font = ImageFont.truetype(LUCKIEST, fs)
    stroke = max(2, fs // 16)
    shadow = max(3, fs // 11)
    letters = []
    k = col_offset
    for i, ch in enumerate(word):
        if ch == " ":
            letters.append(Image.new("RGBA", (int(fs * 0.28), 1), (0, 0, 0, 0)))
            continue
        bb = font.getbbox(ch, stroke_width=stroke)
        lw, lh = bb[2] - bb[0] + shadow + 8, bb[3] - bb[1] + shadow + 8
        L = Image.new("RGBA", (lw, lh), (0, 0, 0, 0))
        dl = ImageDraw.Draw(L)
        o = (-bb[0] + 4, -bb[1] + 4)
        dl.text((o[0] + shadow * 0.55, o[1] + shadow), ch, font=font, fill=INK, stroke_width=stroke, stroke_fill=INK)
        dl.text(o, ch, font=font, fill=RAINBOW[k % len(RAINBOW)], stroke_width=stroke, stroke_fill=INK)
        L = L.rotate(TILTS[k % len(TILTS)] * tilt_scale, resample=Image.BICUBIC, expand=True)
        letters.append(L)
        k += 1
    gap = -int(fs * 0.035)
    tw = sum(l.width for l in letters) + gap * (len(letters) - 1)
    th = max(l.height for l in letters)
    out = Image.new("RGBA", (tw, th + int(fs * 0.03)), (0, 0, 0, 0))
    x = 0
    for i, l in enumerate(letters):
        out.alpha_composite(l, (x, (th - l.height) // 2 + (int(fs * 0.03) if i % 2 else 0)))
        x += l.width + gap
    return out.crop(out.getbbox())


def render_line_strip(width, fs):
    """A little metro line with stations, ending in a new '+' station: one more station."""
    h = int(fs * 0.5)
    img = Image.new("RGBA", (width, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    lw = max(4, int(fs * 0.11)); ow = max(2, int(lw * 0.3))
    y = h // 2
    xe = width - int(h * 0.55)
    d.line([(h * 0.3, y), (xe, y)], fill=INK, width=lw + 2 * ow)
    d.line([(h * 0.3, y), (xe - h * 0.35, y)], fill=TOMATO, width=lw)
    # dashed "planned" bit towards the new station
    n = 5
    for i in range(n):
        x0 = xe - h * 0.35 + i * (h * 0.35 / n)
    r = lw * 0.9
    stops = [h * 0.3 + (xe - h * 0.3) * t for t in (0.0, 0.22, 0.44, 0.66)]
    for x in stops:
        d.ellipse([x - r - ow, y - r - ow, x + r + ow, y + r + ow], fill=INK)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255))
    # the new station: bigger, sunny, with a plus
    R = h * 0.42
    cx = xe
    d.ellipse([cx - R + ow * 1.5, y - R + ow * 1.5, cx + R + ow * 1.5, y + R + ow * 1.5], fill=INK)
    d.ellipse([cx - R, y - R, cx + R, y + R], fill=SUN, outline=INK, width=ow + 1)
    p = R * 0.5; pw = max(3, int(R * 0.28))
    d.line([(cx - p, y), (cx + p, y)], fill=INK, width=pw)
    d.line([(cx, y - p), (cx, y + p)], fill=INK, width=pw)
    return img


def render_logo(height, subtitle=True, sub_text=None):
    """Transparent RGBA logo: 'ONE MORE' over a big rainbow 'STATION', plus a little line strip."""
    S = 2
    fs = int(height * S * 0.62)
    top = render_word("ONE MORE", int(fs * 0.62), col_offset=3, tilt_scale=0.7)
    main = render_word("STATION", fs, col_offset=0)
    W = max(top.width, main.width)
    strip = render_line_strip(int(main.width * 0.9), fs) if subtitle else None
    H = top.height + main.height - int(fs * 0.1) + (strip.height if strip else 0)
    out = Image.new("RGBA", (W + 20, H + 20), (0, 0, 0, 0))
    out.alpha_composite(top, ((W - top.width) // 2, 0))
    out.alpha_composite(main, ((W - main.width) // 2, top.height - int(fs * 0.1)))
    if strip:
        out.alpha_composite(strip, ((W - strip.width) // 2 + int(fs * 0.04), top.height + main.height - int(fs * 0.12)))
    out = out.crop(out.getbbox())
    return out.resize((out.width // S, out.height // S), Image.LANCZOS)


def fit(logo, maxw, maxh):
    k = min(maxw / logo.width, maxh / logo.height)
    return logo.resize((int(logo.width * k), int(logo.height * k)), Image.LANCZOS)


def sticker_panel(img, box, pad, radius=None):
    """Cream sticker card with ink border + hard shadow behind the logo."""
    x0, y0, x1, y1 = box
    d = ImageDraw.Draw(img)
    radius = radius or int(pad * 1.2)
    bw = max(2, pad // 6)
    sh = max(3, pad // 3)
    d.rounded_rectangle([x0 - pad + sh, y0 - pad + sh, x1 + pad + sh, y1 + pad + sh], radius, fill=INK)
    d.rounded_rectangle([x0 - pad, y0 - pad, x1 + pad, y1 + pad], radius, fill=CREAM, outline=INK, width=bw)


def capsule(name, w, h, view, logo_frac_w, logo_frac_h, anchor=(0.5, 0.5), panel=True, dim=0.0, sub=True):
    img = render_map(w, h, view, dim=dim).convert("RGBA")
    logo = fit(render_logo(int(h * logo_frac_h * 1.4), subtitle=sub), w * logo_frac_w, h * logo_frac_h)
    lx = int(anchor[0] * w - logo.width / 2)
    ly = int(anchor[1] * h - logo.height / 2)
    if panel:
        pad = max(6, int(min(w, h) * 0.035))
        sticker_panel(img, (lx, ly, lx + logo.width, ly + logo.height), pad)
    img.alpha_composite(logo, (lx, ly))
    path = os.path.join(OUT, name)
    img.convert("RGB").save(path, optimize=True)
    print(name, img.size)


if __name__ == "__main__":
    # Store capsules
    capsule("header_capsule_920x430.png", 920, 430, (0, 0, 31, 24), 0.56, 0.62, (0.33, 0.5))
    capsule("small_capsule_462x174.png", 462, 174, (10, 0, 34, 24), 0.74, 0.70, (0.5, 0.5))
    capsule("main_capsule_1232x706.png", 1232, 706, (-1, 0, 30, 24), 0.52, 0.52, (0.31, 0.5))
    capsule("vertical_capsule_748x896.png", 748, 896, (8, 0, 32, 24), 0.80, 0.34, (0.5, 0.25))
    # Library
    capsule("library_capsule_600x900.png", 600, 900, (10, 0, 30, 24), 0.84, 0.32, (0.5, 0.24))
    capsule("library_header_920x430.png", 920, 430, (0, 0, 31, 24), 0.56, 0.62, (0.33, 0.5))
    hero = render_map(3840, 1240, (0, 2, 48, 22), clouds=True)
    hero.save(os.path.join(OUT, "library_hero_3840x1240.png"), optimize=True); print("hero")
    logo = render_logo(720)
    logo = fit(logo, 1280, 720)
    canvas = Image.new("RGBA", (1280, 720), (0, 0, 0, 0))
    canvas.alpha_composite(logo, ((1280 - logo.width) // 2, (720 - logo.height) // 2))
    canvas.crop(canvas.getbbox()).save(os.path.join(OUT, "library_logo_transparent.png"), optimize=True); print("logo", canvas.getbbox())
    # Page background (optional), faded
    render_map(1438, 810, (2, 0, 46, 24), trains=False, dim=0.55).save(os.path.join(OUT, "page_background_1438x810.png"), optimize=True)
    # Icons come from make_icon.py.
