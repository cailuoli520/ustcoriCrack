#!/usr/bin/env python3
"""Statically extract the observed InstallShield 12 container; run no installer.
Format references (independent standard-library implementation):
https://raw.githubusercontent.com/lifenjoiner/ISx/master/ISx.c
https://raw.githubusercontent.com/binref/refinery/master/refinery/units/formats/archive/xtiss.py
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import struct
import zlib


def digest(data):
    return hashlib.sha256(data).hexdigest()


def decode_file(data, seed, flags):
    if not flags & 6:
        return data
    magic = bytes((0x13, 0x35, 0x86, 0x07))
    key = bytes(value ^ magic[i % 4] for i, value in enumerate(seed))
    if not key:
        raise ValueError('EMPTY_FILENAME_SEED')
    if flags & 4:
        return bytes((~(key[(i % 1024) % len(key)] ^ ((value << 4) | (value >> 4)))) & 255
                     for i, value in enumerate(data))
    return bytes((~(key[i % len(key)] ^ ((value << 4) | (value >> 4)))) & 255
                 for i, value in enumerate(data))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('installer', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    blob = args.installer.read_bytes()
    if blob[:2] != b'MZ':
        raise ValueError('INSTALLER_IS_NOT_PE')
    offset = blob.rfind(b'InstallShield\0')
    if offset < 0 or offset + 46 > len(blob):
        raise ValueError('INSTALLSHIELD_12_SIGNATURE_NOT_FOUND')
    count = struct.unpack_from('<H', blob, offset + 14)[0]
    if not 1 <= count <= 128:
        raise ValueError('UNEXPECTED_FILE_COUNT')
    position = offset + 46
    root = args.destination.resolve()
    root.mkdir(parents=True, exist_ok=True)
    entries = []
    for index in range(count):
        if position + 312 > len(blob):
            raise ValueError('TRUNCATED_ENTRY_HEADER')
        seed = blob[position:position + 260].split(b'\0', 1)[0]
        try:
            name = seed.decode('utf-8')
        except UnicodeDecodeError:
            name = seed.decode('gb18030')
        flags = struct.unpack_from('<I', blob, position + 260)[0]
        length = struct.unpack_from('<I', blob, position + 268)[0]
        unicode_flag = struct.unpack_from('<H', blob, position + 280)[0]
        start = position + 312
        end = start + length
        if end > len(blob):
            raise ValueError('TRUNCATED_ENTRY_DATA')
        relative = PurePosixPath(name.replace('\\', '/'))
        if relative.is_absolute() or '..' in relative.parts or ':' in name or not name:
            raise ValueError('INVALID_ENTRY_PATH')
        destination = (root / Path(*relative.parts)).resolve()
        if not destination.is_relative_to(root):
            raise ValueError('ENTRY_PATH_ESCAPES_DESTINATION')
        data = decode_file(blob[start:end], seed, flags)
        if unicode_flag:
            data = zlib.decompress(data)
        if name.lower().endswith('.msi') and data[:8] != b'\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1':
            raise ValueError('DECODED_MSI_HEADER_MISMATCH')
        if name.lower().endswith(('.exe', '.dll')) and data[:2] != b'MZ':
            raise ValueError('DECODED_PE_HEADER_MISMATCH')
        destination.parent.mkdir(parents=True, exist_ok=True)
        with destination.open('xb') as stream:
            stream.write(data)
        entry = dict(index=index, name=name, flags=flags, unicode=unicode_flag,
                     data_offset=start, encoded_bytes=length, bytes=len(data),
                     sha256=digest(data), output=str(destination))
        entries.append(entry)
        print(json.dumps(entry, ensure_ascii=False))
        position = end
    if position != len(blob):
        raise ValueError('UNPARSED_TRAILING_INSTALLER_DATA')
    manifest = dict(installer=str(args.installer), installer_sha256=digest(blob),
                    container_offset=offset, entries=entries, installer_executed=False)
    manifest_path = root / 'extraction-manifest.json'
    with manifest_path.open('x', encoding='utf-8') as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2)
    print('STATIC_EXTRACTION_COMPLETE')


if __name__ == '__main__':
    main()
