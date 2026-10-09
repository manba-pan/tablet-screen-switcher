"""Optional icon regeneration. Build/run of PenBridge does not require Python."""
from pathlib import Path
from PIL import Image, ImageDraw

scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)

def rect(box, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(tuple(round(v * scale) for v in box), radius=radius * scale,
                           fill=fill, outline=outline, width=width * scale)

rect((8, 8, 248, 248), 56, "#007aff")
rect((40, 80, 152, 164), 12, outline="#99ccff", width=10)
rect((102, 56, 214, 140), 12, "#1282f4", "white", 10)
points = [(174, 114), (191, 131), (117, 205), (91, 214), (100, 188)]
draw.polygon([(x * scale, y * scale) for x, y in points], fill="white")
draw.line([(100 * scale, 188 * scale), (117 * scale, 205 * scale)], fill="#1282f4", width=5 * scale)
draw.line([(180 * scale, 109 * scale), (197 * scale, 126 * scale)], fill="white", width=10 * scale)
image = image.resize((256, 256), Image.Resampling.LANCZOS)
path = Path(__file__).with_name("PenBridge.ico")
image.save(path, sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
print(path)
