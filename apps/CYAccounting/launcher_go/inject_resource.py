from __future__ import annotations

import struct
import sys
from pathlib import Path


def align(value: int, alignment: int) -> int:
    return (value + alignment - 1) // alignment * alignment


def pe_info(data: bytes):
    pe = struct.unpack_from('<I', data, 0x3C)[0]
    if data[pe:pe+4] != b'PE\0\0':
        raise ValueError('not a PE file')
    coff = pe + 4
    num_sections = struct.unpack_from('<H', data, coff + 2)[0]
    opt_size = struct.unpack_from('<H', data, coff + 16)[0]
    opt = coff + 20
    if struct.unpack_from('<H', data, opt)[0] != 0x20B:
        raise ValueError('expected PE32+')
    section_table = opt + opt_size
    sections = []
    for i in range(num_sections):
        off = section_table + i * 40
        name = data[off:off+8].rstrip(b'\0').decode('ascii', errors='replace')
        vsize, va, raw_size, raw_ptr = struct.unpack_from('<IIII', data, off + 8)
        characteristics = struct.unpack_from('<I', data, off + 36)[0]
        sections.append(dict(name=name, off=off, vsize=vsize, va=va, raw_size=raw_size, raw_ptr=raw_ptr, characteristics=characteristics))
    file_alignment = struct.unpack_from('<I', data, opt + 36)[0]
    section_alignment = struct.unpack_from('<I', data, opt + 32)[0]
    size_headers = struct.unpack_from('<I', data, opt + 60)[0]
    return pe, coff, opt, section_table, sections, file_alignment, section_alignment, size_headers


def inject(source_with_resource: Path, target: Path, output: Path) -> None:
    src = source_with_resource.read_bytes()
    dst = bytearray(target.read_bytes())
    _, _, _, _, src_sections, _, _, _ = pe_info(src)
    rsrc = next((s for s in src_sections if s['name'] == '.rsrc'), None)
    if not rsrc:
        raise ValueError('source has no .rsrc section')
    rsrc_data = src[rsrc['raw_ptr']:rsrc['raw_ptr'] + rsrc['raw_size']]

    _, coff, opt, section_table, dst_sections, file_alignment, section_alignment, size_headers = pe_info(dst)
    if any(s['name'] == '.rsrc' for s in dst_sections):
        raise ValueError('target already has .rsrc')
    new_header_off = section_table + len(dst_sections) * 40
    if new_header_off + 40 > size_headers:
        raise ValueError('not enough PE header space for another section')

    new_va = align(max(s['va'] + max(s['vsize'], s['raw_size']) for s in dst_sections), section_alignment)
    if new_va != rsrc['va']:
        raise ValueError(f'resource RVA mismatch: source 0x{rsrc["va"]:x}, target 0x{new_va:x}')
    new_raw_ptr = align(len(dst), file_alignment)
    if len(dst) < new_raw_ptr:
        dst.extend(b'\0' * (new_raw_ptr - len(dst)))
    dst.extend(rsrc_data)

    header = bytearray(40)
    header[:8] = b'.rsrc\0\0\0'
    struct.pack_into('<IIIIIIHHI', header, 8,
                     rsrc['vsize'], new_va, rsrc['raw_size'], new_raw_ptr,
                     0, 0, 0, 0, rsrc['characteristics'])
    dst[new_header_off:new_header_off+40] = header

    struct.pack_into('<H', dst, coff + 2, len(dst_sections) + 1)
    # SizeOfInitializedData
    old_init = struct.unpack_from('<I', dst, opt + 8)[0]
    struct.pack_into('<I', dst, opt + 8, old_init + rsrc['raw_size'])
    # SizeOfImage
    struct.pack_into('<I', dst, opt + 56, align(new_va + rsrc['vsize'], section_alignment))
    # Checksum is optional for user-mode executables.
    struct.pack_into('<I', dst, opt + 64, 0)
    # Resource data directory, index 2; PE32+ data directories start at opt+112.
    struct.pack_into('<II', dst, opt + 112 + 2 * 8, new_va, rsrc['vsize'])

    output.write_bytes(dst)


if __name__ == '__main__':
    if len(sys.argv) != 4:
        raise SystemExit('usage: inject_resource.py SOURCE_WITH_RSRC TARGET OUTPUT')
    inject(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]))
