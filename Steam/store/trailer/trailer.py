"""Assembles the One More Station Steam trailer from captured frame sequences (or stills as a fallback).

Usage: python trailer.py plan.json out.mp4 [music.wav]
The plan is a list of segments:
  {"type": "card", "kind": "intro" | "end", "dur": 3.0}
  {"type": "frames", "dir": "...", "start": 120, "end": 300, "caption": "Start with one line", "color": "sun"}
  {"type": "still", "path": "...png", "dur": 4.0, "caption": "...", "zoom": [1.0, 1.12], "pan": [0, 0, 0.05, 0.02]}
"""
import json, math, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import storeart as A  # noqa: E402  (logo, map and palette from the store art)

W, H, FPS = 1920, 1080, 30
XFADE = 10  # frames
FFMPEG = "ffmpeg"
COLORS = {"sun": A.SUN, "mint": A.MINT, "sky": A.SKY, "gum": A.GUM, "grape": A.GRAPE, "tang": A.TANG, "tomato": A.TOMATO}


def ease_out_back(t, s=1.70158):
    t -= 1
    return t * t * ((s + 1) * t + s) + 1


def ease_in_out(t):
    return 0.5 - 0.5 * math.cos(math.pi * max(0.0, min(1.0, t)))


# ---------------------------------------------------------------- caption sticker
_sticker_cache = {}


def sticker(text, col, fs=64):
    key = (text, col, fs)
    if key in _sticker_cache:
        return _sticker_cache[key]
    S = 2
    f = ImageFont.truetype(A.LUCKIEST, fs * S)
    bb = f.getbbox(text)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    padx, pady = int(fs * S * 0.6), int(fs * S * 0.38)
    bw, bh = tw + 2 * padx, th + 2 * pady
    sh = int(fs * S * 0.14); ow = max(4, fs * S // 13)
    im = Image.new("RGBA", (bw + sh + 8, bh + sh + 8), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    rad = int(bh * 0.32)
    d.rounded_rectangle([sh, sh, sh + bw, sh + bh], rad, fill=A.INK)
    d.rounded_rectangle([0, 0, bw, bh], rad, fill=col, outline=A.INK, width=ow)
    d.text((padx - bb[0], pady - bb[1]), text, font=f, fill=A.INK)
    im = im.rotate(-2, resample=Image.BICUBIC, expand=True)
    im = im.resize((im.width // S, im.height // S), Image.LANCZOS)
    _sticker_cache[key] = im
    return im


def put_sticker(frame, text, col, i, n, anchor=(0.5, 0.80)):
    """Pop in over 10 frames, pop out over the last 8."""
    st = sticker(text, col)
    if i < 10:
        k = ease_out_back(i / 10)
    elif i > n - 8:
        k = max(0.0, (n - i) / 8)
    else:
        k = 1.0
    if k <= 0.02:
        return frame
    s = st.resize((max(1, int(st.width * k)), max(1, int(st.height * k))), Image.BICUBIC)
    x = int(anchor[0] * W - s.width / 2)
    y = int(anchor[1] * H - s.height / 2)
    frame = frame.convert("RGBA")
    frame.alpha_composite(s, (x, y))
    return frame.convert("RGB")


# ---------------------------------------------------------------- cards
_bg = None


def card_bg():
    global _bg
    if _bg is None:
        _bg = A.render_map(W, H, (2, 0, 46, 24), clouds=True, dim=0.55)
    return _bg


def logo_img(h):
    return A.render_logo(h)


def intro_frames(dur):
    n = int(dur * FPS)
    bg = card_bg()
    logo = A.fit(logo_img(520), W * 0.62, H * 0.52)
    for i in range(n):
        f = bg.copy().convert("RGBA")
        t = min(1.0, i / 18)
        k = ease_out_back(t) if t < 1 else 1.0
        if k > 0.02:
            lg = logo.resize((max(1, int(logo.width * k)), max(1, int(logo.height * k))), Image.BICUBIC)
            f.alpha_composite(lg, ((W - lg.width) // 2, (H - lg.height) // 2 - 30))
        yield f.convert("RGB")


def end_frames(dur):
    n = int(dur * FPS)
    bg = card_bg()
    logo = A.fit(logo_img(460), W * 0.52, H * 0.44)
    wish = sticker("WISHLIST NOW ON STEAM", A.SUN, 70)
    for i in range(n):
        f = bg.copy().convert("RGBA")
        t = min(1.0, i / 16)
        k = ease_out_back(t) if t < 1 else 1.0
        lg = logo.resize((max(1, int(logo.width * k)), max(1, int(logo.height * k))), Image.BICUBIC)
        f.alpha_composite(lg, ((W - lg.width) // 2, int(H * 0.40 - lg.height / 2)))
        if i >= 12:
            t2 = min(1.0, (i - 12) / 12)
            k2 = ease_out_back(t2)
            w2 = wish.resize((max(1, int(wish.width * k2)), max(1, int(wish.height * k2))), Image.BICUBIC)
            f.alpha_composite(w2, ((W - w2.width) // 2, int(H * 0.80 - w2.height / 2)))
        yield f.convert("RGB")


# ---------------------------------------------------------------- footage
def frame_seq(seg):
    d = seg["dir"]
    n = seg["end"] - seg["start"] + 1
    for k, idx in enumerate(range(seg["start"], seg["end"] + 1)):
        p = os.path.join(d, "f_%05d.jpg" % idx)
        f = Image.open(p).convert("RGB")
        if f.size != (W, H):
            f = f.resize((W, H), Image.LANCZOS)
        if seg.get("caption"):
            c0 = seg.get("cap_from", 0); c1 = seg.get("cap_to", n)
            if c0 <= k < c1:
                f = put_sticker(f, seg["caption"], COLORS.get(seg.get("color", "sun"), A.SUN), k - c0, c1 - c0,
                                tuple(seg.get("pos", (0.5, 0.80))))
        yield f


def still_seq(seg):
    src = Image.open(seg["path"]).convert("RGB").resize((W, H), Image.LANCZOS)
    n = int(seg.get("dur", 4) * FPS)
    z0, z1 = seg.get("zoom", [1.0, 1.1])
    px0, py0, px1, py1 = seg.get("pan", [0, 0, 0, 0])
    for i in range(n):
        t = ease_in_out(i / max(1, n - 1))
        z = z0 + (z1 - z0) * t
        cw, ch = W / z, H / z
        cx = W / 2 + (px0 + (px1 - px0) * t) * W
        cy = H / 2 + (py0 + (py1 - py0) * t) * H
        x0 = max(0, min(W - cw, cx - cw / 2)); y0 = max(0, min(H - ch, cy - ch / 2))
        f = src.resize((W, H), Image.BICUBIC, box=(x0, y0, x0 + cw, y0 + ch))
        if seg.get("caption"):
            f = put_sticker(f, seg["caption"], COLORS.get(seg.get("color", "sun"), A.SUN), i, n)
        yield f


def segment(seg):
    if seg["type"] == "card":
        return intro_frames(seg["dur"]) if seg["kind"] == "intro" else end_frames(seg["dur"])
    if seg["type"] == "frames":
        return frame_seq(seg)
    return still_seq(seg)


def main():
    plan = json.load(open(sys.argv[1], encoding="utf-8"))
    out = sys.argv[2]
    music = sys.argv[3] if len(sys.argv) > 3 else None
    tmp = out + ".video.mp4"
    p = subprocess.Popen([FFMPEG, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
                          "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                          "-pix_fmt", "yuv420p", "-movflags", "+faststart", tmp], stdin=subprocess.PIPE)
    total = 0
    tail = []  # last XFADE frames of the previous segment, for crossfades
    for si, seg in enumerate(plan):
        frames = segment(seg)
        head = []
        for f in frames:
            if tail and len(head) < XFADE:
                head.append(f)
                if len(head) == XFADE:
                    for k, (a, b) in enumerate(zip(tail, head)):
                        p.stdin.write(Image.blend(a, b, (k + 1) / (XFADE + 1)).tobytes()); total += 1
                    tail = []
                continue
            # hold back the last XFADE frames of each segment
            tail.append(f)
            if len(tail) > XFADE:
                p.stdin.write(tail.pop(0).tobytes()); total += 1
        print(f"segment {si} done ({seg['type']}), frames so far {total}", flush=True)
    for f in tail:
        p.stdin.write(f.tobytes()); total += 1
    p.stdin.close(); p.wait()
    secs = total / FPS
    if music:
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", tmp, "-i", music, "-map", "0:v", "-map", "1:a",
                        "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                        "-af", f"loudnorm=I=-15:TP=-1.5:LRA=11,afade=t=in:d=0.8,afade=t=out:st={max(0, secs - 3):.2f}:d=3",
                        "-shortest", "-movflags", "+faststart", out], check=True)
        os.remove(tmp)
    else:
        os.replace(tmp, out)
    print(f"wrote {out}: {total} frames, {secs:.1f} s")


if __name__ == "__main__":
    main()
