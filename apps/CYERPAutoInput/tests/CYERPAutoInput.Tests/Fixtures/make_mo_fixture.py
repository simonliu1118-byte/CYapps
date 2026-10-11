"""Builds the fake MO店+ OrderExport fixtures used by the unit tests.

All data here is invented (order numbers, item codes, names); the header row is MO店+'s
public column layout. The workbook is written with xlwt, then protected with RC4 CryptoAPI
the way MO店+ exports are (FilePass v4.2), and stored in a minimal OLE compound file.

    pip install xlwt msoffcrypto-tool xlrd
    python make_mo_fixture.py          # writes mo_export_40bit.xls and mo_export_128bit.xls

Each output is checked by decrypting it with msoffcrypto-tool and reading it back with
xlrd, so the fixture does not depend on CYERPAutoInput's own decryption code.
"""
import hashlib
import io
import os
import struct

import msoffcrypto
import olefile
import xlrd
import xlwt

PASSWORD = "fixture-pass-0001"
HERE = os.path.dirname(os.path.abspath(__file__))

HEADER = ['訂單編號', '訂單編號(序號)', '訂單狀態', '銷退原因', '退貨數量', '訂單金額依品項', '訂單金額加總', '訂單類別', '轉單日', '最晚出貨日期',
          '訂單鑑賞期', '訂單結算日', '出貨/回收日期', '廠退日期', '超取廠退訊息', '商品貨態', '商品貨態時間', '商品屬性', '分期數', '商品編號',
          '商品名稱', '規格1', '規格2', '商品原廠編號', '數量', '優惠代碼', '收件人姓名', '收件人電話', '收件人地址', '客戶編號', '物流商', '物流單號',
          '配送方式', '自訂物流退貨地址', '三方出貨回收地址', '備註(店家自行備註)', '客取貨門市代碼', '客取貨門市名稱', '退貨門市代碼', '退貨門市名稱',
          '商品售價', '全站商店抵用券', 'mo點/mo幣', '單品折價券(商品自折)', '行銷活動促銷(商品自折)', '單店抵用券(商品自折)', '客人支付運費',
          '平台補貼運費', '商品滿額免運費', '預估平台代扣運費(鑑賞期後:平台代扣運費)', '成交手續費', '預購商品服務費', '物流隱碼服務費',
          '金流與系統處理費', '發票處理費', '活動服務費', '(預估)手續費_調整時間', '(預估)手續費_調整原因', '(預估)手續費_調整金額',
          '退貨退款_調整時間', '退貨退款_調整原因', '退貨退款_調整金額', '取消訂單_調整時間', '取消訂單_調整原因', '取消訂單_調整金額',
          '累計調整金額', '訂單預估收入', '訂單進帳金額', '代收金額', '應稅(免稅)', '支付方式',
          '開立發票金額_依品項(若您是自行開立發票，請依此金額開立予消費者)', '開立發票金額加總(若您是自行開立發票，請依此金額開立予消費者)',
          '發票開立統編', '統編', '公司名稱', '開立發票方', '發票號碼', '發票日期']

# (order, seq, code, spec, qty, list price, invoice amount, order invoice total, carrier, tracking,
#  store note, customer fee, subsidy, free shipping, buyer tax id)
ROWS = [
    ('90000000000001', '001', 'X00001', '無', 1, 500, 480, 525, '7-11店出', 'T0000001', '', 45, 0, 0, ''),
    ('90000000000002', '001', 'X00002', '長款', 1, 100, 99, 198, '全家店出', '', '', 45, -45, 0, ''),
    ('90000000000002', '002', 'X00002', '長款', 1, 100, 99, 198, '全家店出', '', '', '', '', '', ''),
    ('90000000000003', '001', 'X00003+ X00004', '組合', 1, 360, 360, 360, '7-11店出', 'T0000003', '', 45, -45, 0, ''),
    ('90000000000004', '001', '', '缺貨號', 1, 270, 270, 335, '第三方物流-新竹貨運', '', '', 65, 0, 0, ''),
    ('90000000000005', '001', 'X00005', '無', 2, 300, 560, 560, '第三方物流-新竹貨運', '', '請先出貨', 65, 0, -65, '12345678'),
    ('90000000000006', '001', 'X00006', '無', 1, 200, 150, 199, '黑貓宅急便', '', '', 49, 0, 0, ''),
]
# Filler orders with long, unique names push the shared string table past one record,
# so the reader's CONTINUE handling is exercised.
for n in range(60):
    sn = f"9100000000{n:04d}"
    ROWS.append((sn, '001', f"Y{n:05d}", '測試規格' + '長' * 40 + str(n), 1, 100, 100, 100, '7-11店出', f"F{n:07d}", '', 45, -45, 0, ''))


def workbook_stream():
    book = xlwt.Workbook(encoding='utf-8')
    sheet = book.add_sheet('訂單明細')
    for c, name in enumerate(HEADER):
        sheet.write(0, c, name)
    col = {name: i for i, name in enumerate(HEADER)}
    for r, (sn, seq, code, spec, qty, price, inv, total, carrier, tracking, note, fee, subsidy, free, ban) in enumerate(ROWS, start=1):
        values = {
            '訂單編號': sn, '訂單編號(序號)': seq, '訂單狀態': '待出貨', '商品名稱': '測試商品' + spec, '規格1': spec,
            '商品原廠編號': code, '數量': float(qty), '收件人姓名': '王*明', '物流商': carrier, '物流單號': tracking,
            '備註(店家自行備註)': note, '商品售價': float(price), '客人支付運費': fee, '平台補貼運費': subsidy, '商品滿額免運費': free,
            '開立發票金額_依品項(若您是自行開立發票，請依此金額開立予消費者)': float(inv),
            '開立發票金額加總(若您是自行開立發票，請依此金額開立予消費者)': float(total),
            '發票開立統編': ban, '應稅(免稅)': '應稅',
        }
        for name, value in values.items():
            if value != '':
                sheet.write(r, col[name], float(value) if isinstance(value, int) else value)
    buf = io.BytesIO()
    book.save(buf)
    stream = olefile.OleFileIO(buf.getvalue()).openstream('Workbook').read()
    end = 0  # xlwt pads the stream; keep whole records only
    for rtype, pos, size in records(stream):
        if rtype == 0 and size == 0:
            break
        end = pos + 4 + size
    return stream[:end]


def rc4(key, data):
    s = list(range(256))
    j = 0
    for i in range(256):
        j = (j + s[i] + key[i % len(key)]) & 0xFF
        s[i], s[j] = s[j], s[i]
    out = bytearray()
    i = j = 0
    for b in data:
        i = (i + 1) & 0xFF
        j = (j + s[i]) & 0xFF
        s[i], s[j] = s[j], s[i]
        out.append(b ^ s[(s[i] + s[j]) & 0xFF])
    return bytes(out)


def block_key(salt, key_bits, block):
    h0 = hashlib.sha1(salt + PASSWORD.encode('utf-16le')).digest()
    h = hashlib.sha1(h0 + struct.pack('<I', block)).digest()
    return h[:5] + b'\0' * 11 if key_bits == 40 else h[:key_bits // 8]


def filepass_record(salt, key_bits):
    csp = 'Microsoft Base Cryptographic Provider v1.0\0'.encode('utf-16le')
    header = struct.pack('<8I', 4, 0, 0x6801, 0x8004, key_bits, 1, 0, 0) + csp
    verifier = bytes(range(16))
    encrypted = rc4(block_key(salt, key_bits, 0), verifier + hashlib.sha1(verifier).digest())
    body = (struct.pack('<HHH', 1, 4, 2) + struct.pack('<II', 4, len(header)) + header +
            struct.pack('<I', 16) + salt + encrypted[:16] + struct.pack('<I', 20) + encrypted[16:])
    return struct.pack('<HH', 0x2F, len(body)) + body


def records(stream):
    pos = 0
    while pos + 4 <= len(stream):
        rtype, size = struct.unpack_from('<HH', stream, pos)
        yield rtype, pos, size
        pos += 4 + size


def protect(stream, key_bits):
    salt = bytes((7 * i + key_bits) & 0xFF for i in range(16))
    fp = filepass_record(salt, key_bits)
    first = next(records(stream))
    insert_at = first[1] + 4 + first[2]
    data = bytearray(stream[:insert_at] + fp + stream[insert_at:])
    plain = {0x0809, 0x002F, 0x0194, 0x0195, 0x00E1, 0x0196, 0x0138}
    keystreams = {}

    def ks(block):
        if block not in keystreams:
            keystreams[block] = rc4(block_key(salt, key_bits, block), bytes(1024))
        return keystreams[block]

    for rtype, pos, size in list(records(bytes(data))):
        if rtype == 0x0085:  # BoundSheet8: shift lbPlyPos past the inserted FilePass
            struct.pack_into('<I', data, pos + 4, struct.unpack_from('<I', data, pos + 4)[0] + len(fp))
    seen_filepass = False
    for rtype, pos, size in list(records(bytes(data))):
        if rtype == 0x002F:
            seen_filepass = True
            continue
        if not seen_filepass or rtype in plain:
            continue
        start = pos + 4 + (4 if rtype == 0x0085 else 0)
        for p in range(start, pos + 4 + size):
            data[p] ^= ks(p // 1024)[p % 1024]
    return bytes(data)


def compound_file(stream):
    sector = 512
    stream = stream + b'\0' * (-len(stream) % sector)
    n_stream = len(stream) // sector
    n_fat = 1
    while (n_fat + 1 + n_stream) > n_fat * (sector // 4):
        n_fat += 1
    end, free, fatsect = 0xFFFFFFFE, 0xFFFFFFFF, 0xFFFFFFFD
    fat = [fatsect] * n_fat + [end]  # FAT sectors, then the directory sector
    first_stream = len(fat)
    fat += [first_stream + i + 1 for i in range(n_stream - 1)] + [end]
    fat += [free] * (n_fat * (sector // 4) - len(fat))

    def entry(name, etype, child, start, size):
        raw = name.encode('utf-16le') + b'\0\0'
        return (raw.ljust(64, b'\0') + struct.pack('<HBB', len(raw), etype, 1) +
                struct.pack('<III', free, free, child) + b'\0' * 16 + b'\0' * 4 + b'\0' * 16 +
                struct.pack('<IQ', start, size))
    directory = entry('Root Entry', 5, 1, end, 0) + entry('Workbook', 2, free, first_stream, len_stream)
    directory += (b'\0' * 64 + struct.pack('<HBB', 0, 0, 0) + struct.pack('<III', free, free, free) + b'\0' * 48) * 2
    header = (bytes([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]) + b'\0' * 16 +
              struct.pack('<HHHHH', 0x3E, 3, 0xFFFE, 9, 6) + b'\0' * 6 +
              struct.pack('<IIIIIIIII', 0, n_fat, n_fat, 0, 4096, end, 0, end, 0))
    difat = [i for i in range(n_fat)] + [free] * (109 - n_fat)
    header += struct.pack('<109I', *difat)
    return header + struct.pack(f'<{len(fat)}I', *fat) + directory + stream


for bits in (40, 128):
    raw = workbook_stream()
    len_stream = len(raw) + len(filepass_record(b'\0' * 16, bits))
    xls = compound_file(protect(raw, bits))
    path = os.path.join(HERE, f'mo_export_{bits}bit.xls')
    with open(path, 'wb') as f:
        f.write(xls)
    office = msoffcrypto.OfficeFile(io.BytesIO(xls))
    office.load_key(password=PASSWORD)
    out = io.BytesIO()
    office.decrypt(out)
    sheet = xlrd.open_workbook(file_contents=out.getvalue()).sheet_by_index(0)
    assert sheet.nrows == len(ROWS) + 1 and sheet.cell_value(1, 0) == ROWS[0][0], 'round trip failed'
    print(path, len(xls), 'bytes, verified with msoffcrypto + xlrd')
