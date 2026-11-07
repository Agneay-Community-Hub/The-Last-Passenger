"""Cuts the trailer for The Last Passenger.

1. Render the frames with a development build:
     Builds/WindowsDev/TheLastPassenger.exe -screen-fullscreen 0 -popupwindow -screen-width 1280 -screen-height 720 \
         -tlptrailer -tlpsuper 2 -tlpshots trailer_work/frames
   This writes f00000.jpg ... at 30 fps, cues.txt (frame, kind, text) and sfx/*.wav (the game's own synthesised sounds).
2. python tools/make_trailer.py trailer_work/frames trailer_work/TheLastPassenger_Trailer.mp4

Captions and the title card are drawn here; the sound is mixed from the game's clips, timed to the cues.
Needs Pillow, numpy and ffmpeg on PATH.
"""
import glob
import os
import random
import subprocess
import sys
import wave

import numpy as np
from PIL import Image, ImageDraw, ImageFont

FPS = 30
SR = 44100
OUT_W, OUT_H = 1920, 1080

frames_dir = sys.argv[1] if len(sys.argv) > 1 else "trailer_work/frames"
out_path = sys.argv[2] if len(sys.argv) > 2 else "trailer_work/TheLastPassenger_Trailer.mp4"
work = os.path.join(os.path.dirname(os.path.abspath(out_path)), "cut")
os.makedirs(work, exist_ok=True)

frames = sorted(glob.glob(os.path.join(frames_dir, "f*.jpg")))
N = len(frames)
cues = []
for line in open(os.path.join(frames_dir, "cues.txt"), encoding="utf-8"):
    parts = line.rstrip("\n").split("\t")
    if len(parts) >= 2:
        cues.append((int(parts[0]), parts[1], parts[2] if len(parts) > 2 else ""))
cuts = sorted({f for f, k, _ in cues if k == "cut"} | {N})


def next_cut(f):
    return min(c for c in cuts if c > f)


def first(kind):
    return next(f for f, k, _ in cues if k == kind)


# ------------------------------------------------------------------ text
FONT_DIR = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts")


def font(size, variation="Regular"):
    f = ImageFont.truetype(os.path.join(FONT_DIR, "bahnschrift.ttf"), size)
    try:
        f.set_variation_by_name(variation)
    except Exception:
        pass
    return f


F_CAP = font(46, "SemiLight")
F_CARD = font(64, "Light")
F_TITLE = font(150, "Bold Condensed")
F_SUB = font(40, "Light")
F_SMALL = font(32, "SemiLight")


def spaced(text, n=1):
    return (" " * n).join(text) if n else text


def draw_center(d, y, text, fnt, alpha, color=(235, 228, 210), track=0):
    t = spaced(text, track)
    w = d.textlength(t, font=fnt)
    x = (OUT_W - w) / 2
    d.text((x + 3, y + 3), t, font=fnt, fill=(0, 0, 0, int(200 * alpha)))
    d.text((x, y), t, font=fnt, fill=color + (int(255 * alpha),))


def env(i, start, end, fade=8):
    if i < start or i >= end:
        return 0.0
    return max(0.0, min(1.0, (i - start) / fade, (end - i) / fade))


captions = [(f + 6, next_cut(f) - 3, t) for f, k, t in cues if k == "caption"]
cards = [(f, next_cut(f), t) for f, k, t in cues if k == "card"]
title_f = first("title")
rng = random.Random(117)
flicker = [1.0 if (i > 14 or rng.random() > 0.45) else rng.random() * 0.3 for i in range(N)]

print("compositing", N, "frames")
for i, path in enumerate(frames):
    im = Image.open(path).convert("RGB").resize((OUT_W, OUT_H), Image.LANCZOS)
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    # letterbox bars for a film look
    d.rectangle((0, 0, OUT_W, 70), fill=(0, 0, 0, 255))
    d.rectangle((0, OUT_H - 70, OUT_W, OUT_H), fill=(0, 0, 0, 255))
    for s, e, t in captions:
        a = env(i, s, e)
        if a > 0:
            tw = d.textlength(t.upper(), font=F_CAP)
            d.rounded_rectangle(((OUT_W - tw) / 2 - 28, OUT_H - 186, (OUT_W + tw) / 2 + 28, OUT_H - 108), 6, fill=(0, 0, 0, int(165 * a)))
            draw_center(d, OUT_H - 175, t.upper(), F_CAP, a, track=0)
    for s, e, t in cards:
        a = env(i, s + 4, e - 2, 12)
        if a > 0:
            draw_center(d, OUT_H / 2 - 40, t, F_CARD, a, color=(225, 215, 195))
    if i >= title_f:
        k = i - title_f
        a = min(1.0, k / 4) * flicker[min(k, N - 1)]
        a *= min(1.0, (N - 1 - i) / 20)
        draw_center(d, 330, "THE LAST PASSENGER", F_TITLE, a, color=(238, 232, 214))
        a2 = min(1.0, max(0.0, (k - 40) / 15)) * min(1.0, (N - 1 - i) / 20)
        draw_center(d, 520, "Night shift. Route 117. Don't lose count.", F_SUB, a2, color=(200, 190, 170))
        a3 = min(1.0, max(0.0, (k - 75) / 15)) * min(1.0, (N - 1 - i) / 20)
        draw_center(d, 700, "GITHUB GAME OFF 2026   ·   THEME: SHIFT", F_SMALL, a3, color=(232, 170, 70))
        draw_center(d, 750, "Play free in your browser on itch.io", F_SMALL, a3, color=(190, 182, 165))
    im = Image.alpha_composite(im.convert("RGBA"), layer).convert("RGB")
    im.save(os.path.join(work, "c%05d.jpg" % i), quality=94)
    if i % 200 == 0:
        print(" ", i)

# ------------------------------------------------------------------ sound
def load(name):
    w = wave.open(os.path.join(frames_dir, "sfx", name + ".wav"))
    sr = w.getframerate()
    x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    t = np.arange(int(len(x) * SR / sr)) * sr / SR
    return np.interp(t, np.arange(len(x)), x).astype(np.float32)


total = int(N / FPS * SR) + SR
L = np.zeros(total, np.float32)
R = np.zeros(total, np.float32)


def at(f):
    return int(f / FPS * SR)


def place(clip, f, vol=1.0, pan=0.0, rate=1.0):
    if rate != 1.0:
        idx = np.arange(0, len(clip) - 1, rate)
        clip = np.interp(idx, np.arange(len(clip)), clip).astype(np.float32)
    s = at(f)
    e = min(total, s + len(clip))
    c = clip[: e - s] * vol
    L[s:e] += c * (1 - max(0, pan))
    R[s:e] += c * (1 + min(0, pan))


def bed(clip, f0, f1, vol, fade_in=1.0, fade_out=1.0):
    s, e = at(f0), min(total, at(f1))
    n = e - s
    if n <= 0:
        return
    xf = int(0.25 * SR)  # crossfade the loop so it never clicks
    out = np.zeros(n, np.float32)
    pos, step = 0, len(clip) - xf
    ramp = np.linspace(0, 1, xf, dtype=np.float32)
    seg = clip.copy()
    seg[:xf] *= ramp
    seg[-xf:] *= ramp[::-1]
    while pos < n:
        k = min(len(seg), n - pos)
        out[pos:pos + k] += seg[:k]
        pos += step
    g = np.ones(n, np.float32)
    fi, fo = int(fade_in * SR), int(fade_out * SR)
    if fi:
        g[:min(fi, n)] = np.linspace(0, 1, min(fi, n))
    if fo:
        g[-min(fo, n):] *= np.linspace(1, 0, min(fo, n))
    out *= g * vol
    L[s:e] += out
    R[s:e] += out


def boom(seconds=4.0, f0=58, f1=26):
    t = np.arange(int(seconds * SR)) / SR
    freq = f0 + (f1 - f0) * (t / seconds)
    ph = 2 * np.pi * np.cumsum(freq) / SR
    tone = np.sin(ph) + 0.35 * np.sin(2 * ph) + 0.15 * np.sign(np.sin(ph * 1.01))
    noise = np.random.default_rng(1).standard_normal(len(t)) * np.exp(-t * 6) * 0.4
    e = np.minimum(1, t / 0.01) * np.exp(-t * 1.1)
    return ((tone * 0.7 + noise) * e).astype(np.float32)


def braam(seconds=3.0):
    t = np.arange(int(seconds * SR)) / SR
    x = np.zeros_like(t)
    for hz, a in ((41.2, 1.0), (61.7, 0.6), (82.4, 0.45), (87.3, 0.25)):
        ph = 2 * np.pi * hz * t * (1 + 0.003 * np.sin(2 * np.pi * 0.7 * t))
        x += a * (2 * ((ph / (2 * np.pi)) % 1) - 1)
    k = np.ones(40) / 40
    x = np.convolve(x, k, mode="same")
    e = np.minimum(1, t / 0.04) * np.exp(-t * 0.9)
    return (x * e * 0.35).astype(np.float32)


sfx = {n: load(n) for n in ("rain", "engine", "hum", "drone", "bell", "printer", "whisper", "breath",
                            "stinger", "rattleHit", "knock", "phone", "heartbeat", "scrape", "thump", "step")}

end_driving = first("drone")           # the montage starts here
lunge = first("lunge")
heart = [f for f, k, _ in cues if k == "heart"]

bed(sfx["rain"], 0, title_f, 0.30, 2.0, 1.0)
bed(sfx["engine"], 0, end_driving, 0.32, 2.0, 0.4)
bed(sfx["hum"], cuts[1], end_driving, 0.10, 0.3, 0.3)  # fluorescent hum once we are inside
bed(sfx["drone"], 0, end_driving, 0.18, 4.0, 0.5)
bed(sfx["drone"], end_driving, heart[0], 0.40, 0.5, 0.5)
bed(sfx["drone"], heart[0], lunge - 18, 0.62, 0.5, 0.15)

for f, k, t in cues:
    if k == "bell":
        place(sfx["bell"], f + 3, 0.55)
    elif k == "printer":
        place(sfx["printer"], f, 0.55)
    elif k == "whisper":
        place(sfx["whisper"], f, 0.7, pan=-0.5)
    elif k == "breath":
        place(sfx["breath"], f, 0.9, pan=0.4)
    elif k == "phone":
        place(sfx["phone"], f + 4, 0.55, pan=-0.3)
    elif k == "card":
        place(braam(3.2), f, 0.9)
    elif k in ("hit", "lunge"):
        s = float(t or 1)
        place(sfx["stinger"], f, min(1.0, 0.45 + 0.5 * s))
        place(sfx["rattleHit"], f, 0.5 * s)
        if k == "lunge":
            place(boom(3.5), f, 0.9)
            place(sfx["scrape"], f, 0.8)
    elif k == "cut" and t == "reardoor":
        for kf in (0.24, 0.53, 0.79):
            place(sfx["rattleHit"], f + kf * 66, 0.85, pan=-0.6)
            place(sfx["knock"], f + kf * 66 + 2, 0.8, pan=-0.6)

# heartbeat speeds up through the lights-out sequence
f, rate = heart[0], 1.0
while f < lunge - 6:
    place(sfx["heartbeat"], f, 0.85)
    f += FPS * 0.9 / rate
    rate = min(2.2, rate + 0.08)
# a few slow footsteps in the dark between stalker cuts
for h in heart[1:]:
    place(sfx["step"], h - 8, 0.6, rate=0.8)

# title: one hit, then rain fading under it
place(boom(5.0, 50, 22), title_f, 1.0)
place(sfx["thump"], title_f, 0.8)
place(braam(4.5), title_f + 2, 0.7)
bed(sfx["rain"], title_f, N, 0.22, 0.5, 1.5)

mix = np.stack([L, R], 1)[: at(N)]
mix /= max(1e-6, np.abs(mix).max()) / 0.92
mix = np.tanh(mix * 1.2) / np.tanh(1.2)
wav_path = os.path.join(work, "mix.wav")
with wave.open(wav_path, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((mix * 32767).astype(np.int16).tobytes())

# ------------------------------------------------------------------ encode
subprocess.run([
    "ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-i", os.path.join(work, "c%05d.jpg"),
    "-i", wav_path, "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
    "-af", "loudnorm=I=-16:TP=-1.5:LRA=11", "-ar", "48000", "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", out_path], check=True)
print("wrote", out_path, round(N / FPS, 1), "s")
