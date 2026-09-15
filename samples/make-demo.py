"""Builds demo.ovx, the sample file used by the screenshots and the README walkthrough.

    python samples/make-demo.py          # writes demo.ovx (562,500 bytes) next to the cwd

It imitates a custom device job format: a 64 byte header (ascii tag, version, total size,
offset field, unix time, device id), a block of scan data, an embedded PNG thumbnail,
a UTF-8 json info block, a UTF-16LE settings block, and a few log strings at the tail -
i.e. exactly the things this viewer is meant to find.
"""
import struct, random, zlib, io

random.seed(7)
buf = bytearray()

def u32(v): return struct.pack('<I', v)

# --- 64 byte header of a fictional scanner job format -------------------
buf += b'OVRX'                 # 0x00 ascii tag
buf += u32(3)                  # 0x04 format version
buf += u32(0)                  # 0x08 total file size (patched later)
buf += u32(0)                  # 0x0C offset of the project_info json (patched)
buf += u32(1755658694)         # 0x10 unix time
buf += u32(9)                  # 0x14 entry count
buf += b'SCANNER-X200\x00\x00\x00\x00'   # 0x18 device id
buf += b'\x00' * (64 - len(buf))

# --- payload block: looks like captured scan data ----------------------
block = bytearray()
for i in range(240 * 1024):
    block += struct.pack('<H', int(2048 + 900 * random.gauss(0, 1)) & 0xFFFF)
buf += block

# --- embedded png thumbnail --------------------------------------------
def png(w, h):
    raw = b''
    for y in range(h):
        raw += b'\x00' + bytes(((x * 7 + y * 3) % 256) for x in range(w * 3))
    def chunk(tag, data):
        c = tag + data
        return struct.pack('>I', len(data)) + c + struct.pack('>I', zlib.crc32(c) & 0xFFFFFFFF)
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw))
            + chunk(b'IEND', b''))

buf += png(96, 96)
buf += b'\x00' * (16 - len(buf) % 16)

# --- utf-8 json: the block the user is usually looking for -------------
json_off = len(buf)
info = ('{"project":"Front bumper A-pillar","version":"v3: restored from V1",'
        '"exported":"2026-08-20T11:58:14","device":{"model":"SCANNER-X200",'
        '"serial":"SX2-00417","firmware":"2.4.1"},"scans":9,"unit":"mm",'
        '"resolution":0.035,"operator":"bsjang","tags":["inspection","release"]}')
buf += info.encode('utf-8')
buf += b'\x00' * 8

# --- more payload, then a utf-16le json --------------------------------
buf += bytes(random.getrandbits(8) for _ in range(64 * 1024))
buf += ('{"ui":{"locale":"ko-KR","theme":"dark"},"lastOpened":"D:/jobs/0820"}'
        ).encode('utf-16-le')
buf += b'\x00' * 8

# --- trailing strings, like a log tail ---------------------------------
for line in ['calibration ok', 'mesh merged: 9 scans', 'export complete',
             'C:/ProgramData/Scanner/profiles/default.cfg']:
    buf += line.encode('ascii') + b'\x00'
buf += bytes(random.getrandbits(8) for _ in range(4096))

# --- patch the two header fields ---------------------------------------
buf[0x08:0x0C] = u32(len(buf))
buf[0x0C:0x10] = u32(json_off)

open('demo.ovx', 'wb').write(bytes(buf))
print('demo.ovx', len(buf), 'bytes; json at 0x%06X' % json_off)
