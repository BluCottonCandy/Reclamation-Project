"""Extract attachment mount geometry from existing gun pixels; does not modify any sprite.
Run from the repository root with Python and Pillow after adding/replacing gun art.
"""
import json
import math
from pathlib import Path
from PIL import Image

ROOT = Path("Resources/Textures")
OUT = Path("Resources/GunAttachmentMounts.json")


# Measured muzzle directions for held SMGs whose skeleton stocks and detached
# hand-occlusion fragments defeat silhouette endpoint-width guesses.
# RSI direction order is south, north, east, west; None means the gun's muzzle
# is hidden behind the body in that frame, so do not draw floating attachments.
WIELDED_DIRECTIONS = {
    "12mmSMG.rsi": {"left": (-1, None, -1, -1), "right": (1, None, 1, 1)},
    "greasegun.rsi": {"left": (-1, None, -1, -1), "right": (-1, None, 1, 1)},
    "45SMG.rsi": {"left": (-1, None, 1, -1), "right": (1, None, 1, 1)},
    "chineseadvanceSMG.rsi": {"left": (-1, None, -1, -1), "right": (1, None, 1, 1)},
    "canadianadvanceSMG.rsi": {"left": (1, None, 1, 1), "right": (-1, None, -1, -1)},
}

def largest_body(image):
    """Ignore detached stock/hand fragments when locating the visible receiver."""
    remaining = {(x,y) for y in range(image.height) for x in range(image.width)
                 if image.getpixel((x,y))[3] > 64}
    largest = set()
    while remaining:
        pending = [remaining.pop()]
        body = set(pending)
        while pending:
            x,y = pending.pop()
            for dx,dy in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1)):
                q = x+dx,y+dy
                if q in remaining:
                    remaining.remove(q); body.add(q); pending.append(q)
        if len(body) > len(largest): largest = body
    return largest

def geometry(image, path, state, direction=0):
    smg = path.startswith("_Nuclear14/") and "/SMGs/" in path
    visible_body = largest_body(image) if smg and "inhand" in state else None
    pixels = [(x + .5 - image.width / 2, image.height / 2 - y - .5)
              for y in range(image.height) for x in range(image.width)
              if image.getpixel((x, y))[3] > 64 and (visible_body is None or (x,y) in visible_body)]
    if len(pixels) < 3:
        return None
    cx = sum(x for x, y in pixels) / len(pixels)
    cy = sum(y for x, y in pixels) / len(pixels)
    xx = sum((x-cx)**2 for x,y in pixels)
    yy = sum((y-cy)**2 for x,y in pixels)
    xy = sum((x-cx)*(y-cy) for x,y in pixels)
    angle = .5 * math.atan2(2*xy, xx-yy)
    ground = "inhand" not in state
    n14 = path.startswith("_Nuclear14/")
    aer = n14 and path.endswith(("/laser_rifle.rsi", "/laser_autorifle.rsi"))
    preset = WIELDED_DIRECTIONS.get(path.rsplit("/",1)[-1]) if smg else None
    hand = "left" if state.endswith("-left") else "right"
    explicit = preset[hand][direction] if preset and "wielded-inhand" in state else None
    if preset and "wielded-inhand" in state and explicit is None:
        return None
    tuned_smg = preset is not None
    # These frames have detached hand/body gaps and narrow stocks. Use their
    # inspected barrel direction rather than comparing endpoint widths.
    wielded_horizontal = "wielded-inhand" in state and (aer or tuned_smg)
    horizontal = wielded_horizontal or ground and n14 and ("/Pistols/" in path or "/SMGs/" in path or abs(angle) < math.radians(25))
    if horizontal: angle = 0
    ux,uy = math.cos(angle),math.sin(angle)
    projections = [(x*ux+y*uy, -x*uy+y*ux, x, y) for x,y in pixels]
    lo,hi = min(p[0] for p in projections),max(p[0] for p in projections)
    def end(at, sign):
        points = [p for p in projections if (p[0]-at)*sign < 2.5]
        width = max(p[1] for p in points)-min(p[1] for p in points)
        return width
    # A barrel terminates narrowly; the butt/receiver is generally wider.
    # Near ties prefer the conventional upper/right muzzle orientation.
    reverse = end(lo,1) + .5 < end(hi,-1)
    # One-handed ground sprites use a right-facing barrel. A narrow rear stock
    # on several SMGs otherwise fools the endpoint-width heuristic.
    if ground and n14 and ("/SMGs/" in path or "/Pistols/" in path):
        reverse = False
    # N14 energy rifles have a thin skeletal butt and a wide emitter housing.
    # Their muzzle is right, even though the narrower-end heuristic suggests left.
    if ground and n14 and "/Battery/" in path and horizontal:
        reverse = False
    # N14 diagonal long-gun ground sprites consistently point upper/right.
    if ground and n14 and not horizontal:
        reverse = False
    if wielded_horizontal:
        if aer:
            reverse = "inhand-left" in state
        else:
            reverse = explicit < 0
    if reverse:
        ux,uy = -ux,-uy
    along = [(x*ux+y*uy,x,y) for x,y in pixels]
    lo,hi = min(p[0] for p in along),max(p[0] for p in along)
    def point(at, sign):
        pts = [p for p in along if (p[0]-at)*sign <= .85]
        return [sum(p[i] for p in pts)/len(pts) for i in (1,2)]
    muzzle,rear = point(hi,-1),point(lo,1)
    if horizontal:
        rear[1] = muzzle[1]
    anchors = muzzle + rear
    if smg and path.endswith("/45SMG.rsi"):
        # Thompson wood lies below the bore. Mount the stock onto that wood,
        # and seat the holo's foot on the top receiver surface.
        wood = []
        for y in range(image.height):
            for x in range(image.width):
                red, green, blue, alpha = image.getpixel((x,y))
                if alpha > 64 and red > 45 and red > green * 1.2 and red > blue * 1.5:
                    wood.append((x+.5-image.width/2, image.height/2-y-.5))
        if wood:
            remaining = set(wood)
            groups = []
            while remaining:
                pending = [remaining.pop()]
                group = set(pending)
                while pending:
                    x,y = pending.pop()
                    for dx in (-1,0,1):
                        for dy in (-1,0,1):
                            q = x+dx,y+dy
                            if q in remaining:
                                remaining.remove(q); group.add(q); pending.append(q)
                if len(group) >= 2: groups.append(group)
            rear_wood = min(groups or [wood], key=lambda group: sum(x*ux+y*uy for x,y in group)/len(group))
            front = max(x*ux+y*uy for x,y in rear_wood)
            edge = [(x,y) for x,y in rear_wood if x*ux+y*uy >= front-.5]
            stock = [sum(p[i] for p in edge)/len(edge) for i in (0,1)]
        else:
            stock = [rear[0]+ux*2,rear[1]+uy*2]
        along_target = (rear[0]+(muzzle[0]-rear[0])*.45)*ux + (rear[1]+(muzzle[1]-rear[1])*.45)*uy
        down_x,down_y = (uy,-ux) if ux >= 0 else (-uy,ux)
        surface = [(x,y) for x,y in pixels if abs(x*ux+y*uy-along_target) <= 1]
        if not surface: surface = pixels
        top = min(x*down_x+y*down_y for x,y in surface)
        surface = [(x,y) for x,y in surface if x*down_x+y*down_y <= top+.5]
        holo = [sum(p[i] for p in surface)/len(surface) for i in (0,1)]
        anchors += stock + holo
    return [round(v,3) for v in anchors]

def main():
    result = {}
    for meta in sorted(ROOT.rglob("meta.json")):
        path = str(meta.parent.relative_to(ROOT)).replace("\\", "/")
        if "/Guns/" not in "/"+path or "/Attachments/" in path or "/GunAttachments/" in path:
            continue
        data = json.loads(meta.read_text(encoding="utf-8-sig"))
        if not any("inhand" in s["name"] for s in data["states"]):
            continue
        w,h = data["size"]["x"],data["size"]["y"]
        for state in data["states"]:
            name = state["name"]
            if name.startswith("equipped-"):
                continue
            file = meta.parent/(name+".png")
            if not file.exists(): continue
            image = Image.open(file).convert("RGBA")
            dirs = state.get("directions",1)
            delays = state.get("delays", [[1] for _ in range(dirs)])
            frames = []
            index = 0
            for direction in range(dirs):
                count = len(delays[direction]) if direction < len(delays) else 1
                poses = []
                for frame in range(count):
                    x,y = index % (image.width//w)*w, index//(image.width//w)*h
                    poses.append(geometry(image.crop((x,y,x+w,y+h)), path, name, direction))
                    index += 1
                frames.append(poses)
            result[path+"/"+name] = frames
    originals = {}
    for key in result:
        folder, state = key.rsplit("/",1)
        if not folder.startswith("_ReclamationProject/Objects/Weapons/Guns/Makeshift/"):
            originals[(folder.rsplit("/",1)[-1].lower(), state)] = key
    aliases = 0
    for key in list(result):
        folder, state = key.rsplit("/",1)
        if not folder.startswith("_ReclamationProject/Objects/Weapons/Guns/Makeshift/"):
            continue
        filename = folder.rsplit("/",1)[-1]
        source = filename.split("_",1)[1].rsplit("_",1)[0] + ".rsi"
        original = originals.get((source.lower(), state))
        if original is None:
            raise ValueError(f"No source-frame mount for {key} ({source})")
        result[key] = result[original]
        aliases += 1
    print(f"{aliases} makeshift states reuse their original frame geometry")
    OUT.write_text(json.dumps(result,separators=(",",":"))+"\n")
    print(f"{len(result)} sprite states across {len(set(k.rsplit('/',1)[0] for k in result))} gun RSIs -> {OUT}")

if __name__ == "__main__": main()
