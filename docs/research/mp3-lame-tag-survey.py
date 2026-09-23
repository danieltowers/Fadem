# Header-only survey of the library's MP3s for gapless info (restage ticket 08). Throwaway.
import os, sys, collections

ROOT = r"the user's music folder"

def crc16(data):  # CRC-16/ARC (poly 0xA001 reflected, init 0), as LAME's tag CRC
    crc = 0
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = (crc >> 1) ^ 0xA001 if crc & 1 else crc >> 1
    return crc

def survey(path):
    size = os.path.getsize(path)
    with open(path, "rb") as f:
        head = f.read(1 << 20)
        f.seek(max(0, size - 128)); tail = f.read(128)
    id3 = 0
    if head[:3] == b"ID3":
        s = head[6:10]; id3 = 10 + ((s[0] << 21) | (s[1] << 14) | (s[2] << 7) | s[3])
        if head[5] & 0x10: id3 += 10
    tagarea = head[:id3]
    smpb = b"iTunSMPB" in tagarea or "iTunSMPB".encode("utf-16-le") in tagarea
    i = id3
    while i < len(head) - 4 and not (head[i] == 0xFF and (head[i + 1] & 0xE0) == 0xE0): i += 1
    frame = head[i:i + 400]
    pos = -1
    for tag in (b"Info", b"Xing"):
        p = frame.find(tag, 0, 60)
        if p >= 0: pos = p; kind = tag.decode(); break
    if pos < 0:
        return dict(variant="bare", smpb=smpb, vbri=frame.find(b"VBRI", 0, 60) >= 0)
    flags = int.from_bytes(frame[pos + 4:pos + 8], "big")
    q = pos + 8
    frames = None
    if flags & 1: frames = int.from_bytes(frame[q:q + 4], "big"); q += 4
    if flags & 2: q += 4
    if flags & 4: q += 100
    if flags & 8: q += 4
    ext = frame[q:q + 36]
    enc = ext[:9].split(b"\0")[0].decode("latin1", "replace").strip()
    delay = (ext[21] << 4) | (ext[22] >> 4)
    pad = ((ext[22] & 0xF) << 8) | ext[23]
    music_len = int.from_bytes(ext[28:32], "big")
    tag_crc = int.from_bytes(ext[34:36], "big")
    crc_ok = crc16(frame[:q + 34]) == tag_crc
    audio = size - id3 - (128 if tail[:3] == b"TAG" else 0)
    len_ok = music_len > 0 and abs(music_len - audio) <= max(10240, audio // 20)
    has_lame = enc[:4] in ("LAME", "Lavf", "Lavc", "GOGO") or enc[:1] == "L"
    return dict(variant="info" if kind == "Info" else "vbr", enc=enc[:8], has_lame=has_lame, crc_ok=crc_ok,
                len_ok=len_ok, delay=delay, pad=pad, smpb=smpb, frames=frames)

rows = []
for d, _, fs in os.walk(ROOT):
    for n in fs:
        if n.lower().endswith(".mp3"):
            p = os.path.join(d, n)
            try: rows.append((p, survey(p)))
            except Exception as e: rows.append((p, dict(variant="error", err=str(e))))

by = collections.Counter(r["variant"] for _, r in rows)
print("MP3 files:", len(rows), dict(by))
tagged = [r for _, r in rows if r["variant"] in ("info", "vbr")]
print("tagged with a LAME-style extension:", sum(r["has_lame"] for r in tagged), "of", len(tagged))
print("  tag CRC valid:", sum(r["has_lame"] and r["crc_ok"] for r in tagged))
print("  MusicLength matches audio size:", sum(r["has_lame"] and r["len_ok"] for r in tagged))
print("  CRC valid AND length matches:", sum(r["has_lame"] and r["crc_ok"] and r["len_ok"] for r in tagged))
print("  encoder strings:", collections.Counter(r["enc"] for r in tagged).most_common(10))
print("  delays:", collections.Counter(r["delay"] for r in tagged if r["has_lame"]).most_common(5))
print("  padding range:", min((r["pad"] for r in tagged if r["has_lame"]), default=None), "-", max((r["pad"] for r in tagged if r["has_lame"]), default=None))
bare = [(p, r) for p, r in rows if r["variant"] == "bare"]
print("bare:", len(bare), " with iTunSMPB:", sum(r["smpb"] for _, r in bare), " with VBRI:", sum(r["vbri"] for _, r in bare))
print("  bare by folder:", collections.Counter(os.path.relpath(p, ROOT).split(os.sep)[0][:50] for p, _ in bare).most_common(5))
print("  info by folder:", collections.Counter(os.path.relpath(p, ROOT).split(os.sep)[0][:50] for p, r in rows if r["variant"] == "info").most_common(5))
for p, r in rows:
    if r["variant"] == "error": print("  error:", os.path.relpath(p, ROOT), r["err"])
