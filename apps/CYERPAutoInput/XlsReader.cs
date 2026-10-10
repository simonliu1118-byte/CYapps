using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CYERPAutoInput;

/// <summary>
/// Minimal read-only reader for legacy Excel 97–2003 (.xls, BIFF8) order exports, including
/// the password-protected kind MO店+ produces (RC4 CryptoAPI, MS-OFFCRYPTO 2.3.5). Returns
/// the first worksheet as rows of cell text, like <see cref="XlsxReader"/>. No third-party
/// dependency and no Excel installation. Other encryption methods are rejected, never guessed.
/// </summary>
internal static class XlsReader
{
    private const ushort RecBof = 0x0809, RecEof = 0x000A, RecFilePass = 0x002F, RecBoundSheet = 0x0085,
        RecSst = 0x00FC, RecContinue = 0x003C, RecLabelSst = 0x00FD, RecNumber = 0x0203, RecRk = 0x027E,
        RecMulRk = 0x00BD, RecLabel = 0x0204, RecFormula = 0x0006, RecString = 0x0207, RecBoolErr = 0x0205;

    // Records that MS-XLS 2.2.10 keeps in plain text inside an encrypted workbook stream.
    private static readonly HashSet<ushort> NeverEncrypted = [RecBof, RecFilePass, 0x0194, 0x0195, 0x00E1, 0x0196, 0x0138];

    public static List<string[]> ReadFirstSheet(Stream xls, string? password)
    {
        using var copy = new MemoryStream();
        xls.CopyTo(copy);
        var file = copy.ToArray();
        var stream = CompoundFile.ReadStream(file, "Workbook") ?? CompoundFile.ReadStream(file, "Book")
            ?? throw new InvalidDataException("不是 Excel 97–2003 活頁簿（找不到 Workbook）。");
        Decrypt(stream, password);
        return ParseFirstSheet(stream);
    }

    /// <summary>True when the workbook stream carries a FilePass record (password protected).</summary>
    public static bool IsEncrypted(byte[] file)
    {
        var stream = CompoundFile.ReadStream(file, "Workbook") ?? CompoundFile.ReadStream(file, "Book");
        if (stream is null) return false;
        foreach (var (type, _, _) in Records(stream))
        {
            if (type == RecFilePass) return true;
            if (type == RecBoundSheet) return false; // FilePass precedes the sheet list
        }
        return false;
    }

    private static IEnumerable<(ushort Type, int DataOffset, int Size)> Records(byte[] stream, int start = 0)
    {
        var pos = start;
        while (pos + 4 <= stream.Length)
        {
            var type = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(pos));
            var size = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(pos + 2));
            if (pos + 4 + size > stream.Length) throw new InvalidDataException("Excel 檔案內容不完整。");
            yield return (type, pos + 4, size);
            pos += 4 + size;
        }
    }

    // ---- decryption -------------------------------------------------------------------

    private static void Decrypt(byte[] stream, string? password)
    {
        Rc4Key? key = null;
        foreach (var (type, offset, size) in Records(stream).ToList())
        {
            if (key is null)
            {
                if (type == RecFilePass) key = ReadFilePass(stream.AsSpan(offset, size), password);
                else if (type == RecBoundSheet) return; // not encrypted
                continue;
            }
            if (NeverEncrypted.Contains(type)) continue;
            // BoundSheet8.lbPlyPos (the sheet's stream offset) stays plain.
            var skip = type == RecBoundSheet ? 4 : 0;
            key.Apply(stream, offset + skip, size - skip);
        }
    }

    private static Rc4Key ReadFilePass(ReadOnlySpan<byte> data, string? password)
    {
        var encryptionType = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (encryptionType != 1)
            throw new InvalidDataException("這個 Excel 使用不支援的保護方式（XOR）；請確認是平台原始匯出檔。");
        var major = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var minor = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (major is not (2 or 3 or 4) || minor != 2)
            throw new InvalidDataException($"這個 Excel 使用不支援的加密版本（{major}.{minor}）；目前只支援 RC4 CryptoAPI。");
        if (string.IsNullOrEmpty(password))
            throw new InvalidDataException("這個 Excel 有密碼保護，請先在「設定」填入匯出檔密碼。");

        // EncryptionHeader (after Flags + HeaderSize) then EncryptionVerifier.
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        var header = data.Slice(14, headerSize);
        var keyBits = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
        if (keyBits == 0) keyBits = 40;
        var verifier = data[(14 + headerSize)..];
        var saltSize = BinaryPrimitives.ReadInt32LittleEndian(verifier);
        var salt = verifier.Slice(4, saltSize).ToArray();
        var encryptedVerifier = verifier.Slice(4 + saltSize, 16);
        var hashSize = BinaryPrimitives.ReadInt32LittleEndian(verifier[(20 + saltSize)..]);
        var encryptedHash = verifier.Slice(24 + saltSize, hashSize);

        var key = new Rc4Key(password, salt, keyBits);
        var check = new byte[16 + hashSize];
        encryptedVerifier.CopyTo(check);
        encryptedHash.CopyTo(check.AsSpan(16));
        key.Apply(check, 0, check.Length); // one continuous RC4 run under block 0
        var expected = SHA1.HashData(check.AsSpan(0, 16));
        if (!expected.AsSpan().SequenceEqual(check.AsSpan(16, Math.Min(hashSize, expected.Length))))
            throw new InvalidDataException("Excel 密碼不正確，請在「設定」確認匯出檔密碼。");
        return key;
    }

    /// <summary>
    /// RC4 CryptoAPI keystream addressed by stream position: every 1024-byte block of the
    /// workbook stream uses its own key SHA1(SHA1(salt + password) + blockNumber).
    /// </summary>
    private sealed class Rc4Key(string password, byte[] salt, int keyBits)
    {
        private const int BlockSize = 1024;
        private readonly byte[] _h0 = SHA1.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
        private readonly Dictionary<int, byte[]> _blocks = [];

        public void Apply(byte[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var pos = offset + i;
                buffer[pos] ^= Keystream(pos / BlockSize)[pos % BlockSize];
            }
        }

        private byte[] Keystream(int block)
        {
            if (_blocks.TryGetValue(block, out var cached)) return cached;
            var blockBytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(blockBytes, block);
            var hash = SHA1.HashData([.. _h0, .. blockBytes]);
            var key = new byte[keyBits == 40 ? 16 : keyBits / 8];
            Array.Copy(hash, key, keyBits == 40 ? 5 : key.Length);
            return _blocks[block] = Rc4(key, BlockSize);
        }

        private static byte[] Rc4(byte[] key, int length)
        {
            var s = new byte[256];
            for (var i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 0xFF;
                (s[i], s[j]) = (s[j], s[i]);
            }
            var output = new byte[length];
            for (int n = 0, i = 0, j = 0; n < length; n++)
            {
                i = (i + 1) & 0xFF;
                j = (j + s[i]) & 0xFF;
                (s[i], s[j]) = (s[j], s[i]);
                output[n] = s[(s[i] + s[j]) & 0xFF];
            }
            return output;
        }
    }

    // ---- BIFF8 cells ------------------------------------------------------------------

    private static List<string[]> ParseFirstSheet(byte[] stream)
    {
        string[] sst = [];
        int? sheetOffset = null;
        var all = Records(stream).ToList();
        for (var i = 0; i < all.Count; i++)
        {
            var (type, offset, size) = all[i];
            if (type == RecBoundSheet && sheetOffset is null && stream[offset + 5] == 0) // dt 0 = worksheet
                sheetOffset = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(offset));
            else if (type == RecSst)
            {
                var segments = new List<ArraySegment<byte>> { new(stream, offset, size) };
                while (i + 1 < all.Count && all[i + 1].Type == RecContinue)
                {
                    i++;
                    segments.Add(new ArraySegment<byte>(stream, all[i].DataOffset, all[i].Size));
                }
                sst = ReadSst(segments);
            }
            else if (type == RecEof) break; // end of the workbook globals
        }
        if (sheetOffset is null) throw new InvalidDataException("Excel 檔案沒有工作表。");

        var cells = new SortedDictionary<int, SortedDictionary<int, string>>();
        void Put(int row, int col, string text)
        {
            if (!cells.TryGetValue(row, out var line)) cells[row] = line = new SortedDictionary<int, string>();
            line[col] = text;
        }

        var sheet = Records(stream, sheetOffset.Value).GetEnumerator();
        if (!sheet.MoveNext() || sheet.Current.Type != RecBof) throw new InvalidDataException("Excel 工作表位置不正確。");
        (int Row, int Col)? pendingFormula = null;
        while (sheet.MoveNext())
        {
            var (type, offset, size) = sheet.Current;
            var data = stream.AsSpan(offset, size);
            if (type == RecEof) break;
            var row = size >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(data) : 0;
            var col = size >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) : 0;
            switch (type)
            {
                case RecLabelSst:
                    var index = BinaryPrimitives.ReadInt32LittleEndian(data[6..]);
                    Put(row, col, index >= 0 && index < sst.Length ? sst[index] : string.Empty);
                    break;
                case RecNumber:
                    Put(row, col, FormatNumber(BinaryPrimitives.ReadDoubleLittleEndian(data[6..])));
                    break;
                case RecRk:
                    Put(row, col, FormatNumber(DecodeRk(BinaryPrimitives.ReadUInt32LittleEndian(data[6..]))));
                    break;
                case RecMulRk:
                    var first = col;
                    var count = (size - 6) / 6;
                    for (var k = 0; k < count; k++)
                        Put(row, first + k, FormatNumber(DecodeRk(BinaryPrimitives.ReadUInt32LittleEndian(data[(4 + k * 6 + 2)..]))));
                    break;
                case RecLabel:
                    Put(row, col, ReadUnicodeString(data[6..], out _));
                    break;
                case RecBoolErr:
                    Put(row, col, data[7] == 0 ? (data[6] != 0 ? "TRUE" : "FALSE") : string.Empty);
                    break;
                case RecFormula:
                    var value = data.Slice(6, 8);
                    if (value[6] == 0xFF && value[7] == 0xFF)
                    {
                        switch (value[0])
                        {
                            case 0: pendingFormula = (row, col); break; // text follows in a String record
                            case 1: Put(row, col, value[2] != 0 ? "TRUE" : "FALSE"); break;
                            default: Put(row, col, string.Empty); break;
                        }
                    }
                    else Put(row, col, FormatNumber(BinaryPrimitives.ReadDoubleLittleEndian(value)));
                    break;
                case RecString when pendingFormula is { } at:
                    Put(at.Row, at.Col, ReadUnicodeString(data, out _));
                    pendingFormula = null;
                    break;
            }
        }

        var rows = new List<string[]>();
        foreach (var (rowIndex, line) in cells)
        {
            while (rows.Count < rowIndex) rows.Add([]);
            var values = new string[line.Keys.Max() + 1];
            for (var c = 0; c < values.Length; c++) values[c] = line.TryGetValue(c, out var v) ? v : string.Empty;
            rows.Add(values);
        }
        return rows;
    }

    /// <summary>XLUnicodeString: cch(2), flags(1), characters (1 byte each unless fHighByte).</summary>
    private static string ReadUnicodeString(ReadOnlySpan<byte> data, out int length)
    {
        var cch = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var high = (data[2] & 1) != 0;
        var bytes = high ? cch * 2 : cch;
        length = 3 + bytes;
        return high ? Encoding.Unicode.GetString(data.Slice(3, bytes)) : Latin1(data.Slice(3, bytes));
    }

    private static string Latin1(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    private static double DecodeRk(uint rk)
    {
        double value;
        if ((rk & 2) != 0) value = (int)rk >> 2;
        else value = BitConverter.Int64BitsToDouble((long)(rk & 0xFFFFFFFC) << 32);
        return (rk & 1) != 0 ? value / 100 : value;
    }

    internal static string FormatNumber(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Shared string table. A string's characters may continue in the next CONTINUE record,
    /// which then starts with a fresh flags byte (high byte or not) for the rest.
    /// </summary>
    private static string[] ReadSst(List<ArraySegment<byte>> segments)
    {
        var reader = new SegmentReader(segments);
        reader.ReadInt32(); // cstTotal
        var unique = reader.ReadInt32();
        var result = new string[Math.Max(0, unique)];
        for (var n = 0; n < result.Length && !reader.AtEnd; n++)
        {
            int cch = reader.ReadUInt16();
            var flags = reader.ReadByte();
            var high = (flags & 1) != 0;
            var runs = (flags & 8) != 0 ? reader.ReadUInt16() : 0;
            var ext = (flags & 4) != 0 ? reader.ReadInt32() : 0;
            var text = new StringBuilder(cch);
            while (text.Length < cch)
            {
                if (reader.SegmentRemaining == 0)
                {
                    reader.NextSegment();
                    high = (reader.ReadByte() & 1) != 0;
                }
                var take = Math.Min(cch - text.Length, high ? reader.SegmentRemaining / 2 : reader.SegmentRemaining);
                var chunk = reader.ReadBytes(high ? take * 2 : take);
                text.Append(high ? Encoding.Unicode.GetString(chunk) : Latin1(chunk));
            }
            reader.Skip(runs * 4 + ext);
            result[n] = text.ToString();
        }
        return result;
    }

    private sealed class SegmentReader(List<ArraySegment<byte>> segments)
    {
        private int _segment;
        private int _pos;

        public bool AtEnd => _segment >= segments.Count || (_segment == segments.Count - 1 && _pos >= segments[_segment].Count);
        public int SegmentRemaining => _segment < segments.Count ? segments[_segment].Count - _pos : 0;

        public void NextSegment()
        {
            _segment++;
            _pos = 0;
            if (_segment >= segments.Count) throw new InvalidDataException("Excel 字串表不完整。");
        }

        public byte ReadByte()
        {
            while (SegmentRemaining == 0) NextSegment();
            return segments[_segment][_pos++];
        }

        public byte[] ReadBytes(int count)
        {
            var result = new byte[count];
            for (var i = 0; i < count; i++) result[i] = ReadByte();
            return result;
        }

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));
        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4));

        public void Skip(int count)
        {
            for (var i = 0; i < count; i++) ReadByte();
        }
    }
}

/// <summary>Read-only OLE compound file (CFB) access: returns one top-level stream's bytes.</summary>
internal static class CompoundFile
{
    private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private const uint EndOfChain = 0xFFFFFFFE;

    public static byte[]? ReadStream(byte[] file, string name)
    {
        if (file.Length < 512 || !file.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("不是 Excel 97–2003 (.xls) 檔案。");
        var sectorSize = 1 << BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x1E));
        var miniSectorSize = 1 << BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x20));
        var fatSectorCount = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(0x2C));
        var firstDirectory = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x30));
        var miniCutoff = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x38));
        var firstMiniFat = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x3C));
        var firstDifat = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x44));

        int Offset(uint sector) => (int)((sector + 1) * sectorSize);

        // FAT sector list: 109 entries in the header, then the DIFAT chain.
        var fatSectors = new List<uint>();
        for (var i = 0; i < 109 && fatSectors.Count < fatSectorCount; i++)
            fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x4C + i * 4)));
        var perDifat = sectorSize / 4 - 1;
        for (var difat = firstDifat; difat < EndOfChain && fatSectors.Count < fatSectorCount;)
        {
            var at = Offset(difat);
            for (var i = 0; i < perDifat && fatSectors.Count < fatSectorCount; i++)
                fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + i * 4)));
            difat = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + perDifat * 4));
        }
        var fat = new List<uint>();
        foreach (var sector in fatSectors)
        {
            var at = Offset(sector);
            for (var i = 0; i < sectorSize / 4; i++) fat.Add(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + i * 4)));
        }

        byte[] Chain(uint start, long length, Func<uint, int> offset, int size, IReadOnlyList<uint> table)
        {
            var result = new byte[length];
            var written = 0L;
            var guard = 0;
            for (var sector = start; sector < EndOfChain && written < length; sector = table[(int)sector])
            {
                if (++guard > table.Count || sector >= table.Count) throw new InvalidDataException("Excel 檔案結構損毀。");
                var take = (int)Math.Min(size, length - written);
                Array.Copy(file, offset(sector), result, written, take);
                written += take;
            }
            if (written < length) throw new InvalidDataException("Excel 檔案結構損毀。");
            return result;
        }

        // Directory: 128-byte entries along the directory chain.
        var directory = new List<byte[]>();
        var guardDir = 0;
        for (var sector = firstDirectory; sector < EndOfChain; sector = fat[(int)sector])
        {
            if (++guardDir > fat.Count || sector >= fat.Count) throw new InvalidDataException("Excel 檔案結構損毀。");
            for (var i = 0; i < sectorSize / 128; i++) directory.Add(file.AsSpan(Offset(sector) + i * 128, 128).ToArray());
        }
        if (directory.Count == 0) return null;
        var root = directory[0];

        foreach (var entry in directory)
        {
            if (entry[0x42] != 2) continue; // stream objects only
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(0x40));
            if (nameLength < 2) continue;
            var entryName = Encoding.Unicode.GetString(entry, 0, nameLength - 2);
            if (!entryName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

            var start = BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(0x74));
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(0x78));
            if (size >= miniCutoff) return Chain(start, size, s => Offset(s), sectorSize, fat);

            // Small streams live in the mini stream (the root entry's data) via the mini FAT.
            var miniStream = Chain(BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(0x74)),
                (long)BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(0x78)), s => Offset(s), sectorSize, fat);
            var miniFat = new List<uint>();
            var guardMini = 0;
            for (var sector = firstMiniFat; sector < EndOfChain; sector = fat[(int)sector])
            {
                if (++guardMini > fat.Count || sector >= fat.Count) throw new InvalidDataException("Excel 檔案結構損毀。");
                for (var i = 0; i < sectorSize / 4; i++)
                    miniFat.Add(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(Offset(sector) + i * 4)));
            }
            var result = new byte[size];
            var written = 0L;
            for (var sector = start; sector < EndOfChain && written < size; sector = miniFat[(int)sector])
            {
                if (sector >= miniFat.Count) throw new InvalidDataException("Excel 檔案結構損毀。");
                var take = (int)Math.Min(miniSectorSize, size - written);
                Array.Copy(miniStream, sector * miniSectorSize, result, written, take);
                written += take;
            }
            return result;
        }
        return null;
    }
}
