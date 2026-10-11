"""Build assets/glyphs/<family>/<positional-name>.png from Kenney's Input Prompts (CC0, kenney.nl, 1.5A).

Usage: python build_kenney_glyphs.py <Kenney extracted dir> <assets/glyphs dir>
Replaces the old Switch-derived glyphs (personal use only) with CC0 art that can be shipped.
Names are positional (south = bottom face button on every pad); each image is trimmed to its visible pixels plus a
small margin, so they draw at the same size as the old 35 px set (the mod scales by height).
"""
import os, shutil, sys
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]

COMMON = {
    'lstick': 'stick_l', 'lstick_lr': 'stick_l_horizontal', 'lstick_ud': 'stick_l_vertical', 'lstick_click': 'stick_l_press',
    'rstick': 'stick_r', 'rstick_lr': 'stick_r_horizontal', 'rstick_ud': 'stick_r_vertical', 'rstick_click': 'stick_r_press',
    'dpad': 'dpad', 'dpad_up': 'dpad_up', 'dpad_down': 'dpad_down', 'dpad_left': 'dpad_left', 'dpad_right': 'dpad_right',
    'dpad_lr': 'dpad_horizontal', 'dpad_ud': 'dpad_vertical',
}

FAMILIES = {
    'xbox': ('Xbox Series', 'xbox_', {
        'south': 'button_a', 'east': 'button_b', 'west': 'button_x', 'north': 'button_y',
        'lb': 'lb', 'rb': 'rb', 'lt': 'lt', 'rt': 'rt', 'start': 'button_menu', 'back': 'button_view'}),
    'playstation': ('PlayStation Series', 'playstation_', {
        'south': 'button_cross', 'east': 'button_circle', 'west': 'button_square', 'north': 'button_triangle',
        'lb': 'trigger_l1', 'rb': 'trigger_r1', 'lt': 'trigger_l2', 'rt': 'trigger_r2',
        'start': '!playstation5_button_options', 'back': '!playstation5_button_create'}),
    # Positional: the Switch's bottom button is B, the right one A, the left Y, the top X.
    'switch': ('Nintendo Switch', 'switch_', {
        'south': 'button_b', 'east': 'button_a', 'west': 'button_y', 'north': 'button_x',
        'lb': 'button_l', 'rb': 'button_r', 'lt': 'button_zl', 'rt': 'button_zr', 'start': 'button_plus', 'back': 'button_minus'}),
    'steamdeck': ('Steam Deck', 'steamdeck_', {
        'south': 'button_a', 'east': 'button_b', 'west': 'button_x', 'north': 'button_y',
        'lb': 'button_l1', 'rb': 'button_r1', 'lt': 'button_l2', 'rt': 'button_r2',
        'start': 'button_options', 'back': 'button_view'}),
}


# Kenney's glyphs are pure white; tinted to a warm grey so they sit in the HUD.
TINT = (0xB9, 0xB0, 0x9E)


def tint(img, rgb=TINT):
    """Grey-ish pixels (the white shapes and their anti-aliased edges) become the shade at the same brightness;
    coloured ones (the red d-pad highlight) and transparent ones stay as they are."""
    img = img.convert('RGBA')
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            mx, mn = max(r, g, b), min(r, g, b)
            if mx > 0 and (mx - mn) / mx < 0.18:
                k = mx / 255
                px[x, y] = (round(rgb[0] * k), round(rgb[1] * k), round(rgb[2] * k), a)
    return img


def trim(img, margin=4):
    img = img.convert('RGBA')
    box = img.getchannel('A').point(lambda a: 255 if a > 8 else 0).getbbox()
    if box is None:
        return img
    l, t, r, b = box
    l, t = max(0, l - margin), max(0, t - margin)
    r, b = min(img.width, r + margin), min(img.height, b + margin)
    return img.crop((l, t, r, b))


for family, (folder, prefix, own) in FAMILIES.items():
    out = os.path.join(dst, family)
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)
    names = dict(COMMON)
    names.update(own)
    for ours, theirs in names.items():
        file = theirs[1:] if theirs.startswith('!') else prefix + theirs
        path = os.path.join(src, folder, 'Double', file + '.png')
        if not os.path.exists(path):
            print('MISSING', family, ours, path)
            continue
        img = tint(trim(Image.open(path)))
        if family == 'steamdeck' and ours in ('start', 'back'):
            # The Deck's View / Menu are wide pills; drawn at full glyph height they look huge. Pad them vertically.
            canvas = Image.new('RGBA', (img.width, round(img.height * 1.6)), (0, 0, 0, 0))
            canvas.paste(img, (0, (canvas.height - img.height) // 2))
            img = canvas
        img.save(os.path.join(out, ours + '.png'))
    print(family, len(os.listdir(out)), 'glyphs')

with open(os.path.join(dst, 'LICENSE-Kenney.txt'), 'w', encoding='utf-8') as f:
    f.write('Button glyphs: "Input Prompts" 1.5A by Kenney (www.kenney.nl), CC0 1.0 Universal\n'
            '(http://creativecommons.org/publicdomain/zero/1.0/). Renamed to positional names and trimmed by\n'
            'tools/build_kenney_glyphs.py, tinted warm grey. Button symbols are trademarks of their owners.\n')
