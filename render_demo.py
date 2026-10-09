"""Encode screenshots rendered by --demo-record as a local demonstration GIF."""
from pathlib import Path
from PIL import Image
import argparse

parser = argparse.ArgumentParser()
parser.add_argument("frames", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
files = sorted(args.frames.glob("frame-*.png"))
if len(files) != 80:
    raise SystemExit(f"Expected 80 rendered frames, found {len(files)}")
frames = []
for path in files:
    with Image.open(path) as frame:
        size = (1000, round(frame.height * 1000 / frame.width))
        frames.append(frame.convert("RGB").resize(size, Image.Resampling.LANCZOS))
palette = frames[0].quantize(colors=128)
indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
durations = [100] * len(indexed)
for index in (19, 39, 59, 79):
    durations[index] = 900
args.output.parent.mkdir(parents=True, exist_ok=True)
indexed[0].save(args.output, save_all=True, append_images=indexed[1:], duration=durations,
                loop=0, optimize=True, disposal=1)
print(f"Created {args.output} ({args.output.stat().st_size:,} bytes), 80 frames, 11.2 seconds")
