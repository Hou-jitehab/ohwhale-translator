# -*- coding: utf-8 -*-
"""把插件里的小鲸鱼形象转成 exe 用的 .ico（只是缩放/裁边/打包，不重绘图片）。"""
import sys
from PIL import Image

src = sys.argv[1]
dst = sys.argv[2]

img = Image.open(src).convert("RGBA")
w, h = img.size

# 方形裁切：以中心为准，取短边
side = min(w, h)
left = (w - side) // 2
top = (h - side) // 2
img = img.crop((left, top, left + side, top + side))

# 留一点边距，避免图标顶到边
canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
pad = int(side * 0.04)
canvas.paste(img.resize((side - 2 * pad, side - 2 * pad), Image.LANCZOS), (pad, pad))

sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
canvas.save(dst, format="ICO", sizes=sizes)
print("ok", dst, canvas.size)