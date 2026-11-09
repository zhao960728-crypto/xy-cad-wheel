using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XiaoYang.AutoCADPlugin;

// Minimal ECMA-335 metadata reader. No Assembly.Load, dependency resolution or attribute constructors.
internal sealed class ExtensionDllMetadata
{
    private readonly byte[] _data;
    private readonly uint[] _rows = new uint[64];
    private readonly int[] _offsets = new int[13];
    private int _strings, _blobs, _stringWidth, _blobWidth, _guidWidth;
    private ExtensionDllMetadata(byte[] data) { _data = data; }
    internal static List<string> Read(byte[] data) => new ExtensionDllMetadata(data).Commands();
    private ushort U16(int at) { Require(at, 2); return BitConverter.ToUInt16(_data, at); }
    private uint U32(int at) { Require(at, 4); return BitConverter.ToUInt32(_data, at); }
    private int Index(ref int at, int width) { var value = width == 2 ? U16(at) : checked((int)U32(at)); at += width; return value; }
    private void Require(int at, int count) { if (at < 0 || count < 0 || at > _data.Length - count) throw new InvalidDataException("DLL 元数据范围无效"); }
    private int Table(int number) => _rows[number] < 65536 ? 2 : 4;
    private int Coded(int bits, params int[] tables) => tables.Max(t => _rows[t]) < (1u << (16 - bits)) ? 2 : 4;
    private int Size(int table)
    {
        switch (table)
        {
            case 0: return 2 + _stringWidth + 3 * _guidWidth;
            case 1: return Coded(2, 0, 26, 35, 1) + 2 * _stringWidth;
            case 2: return 4 + 2 * _stringWidth + Coded(2, 2, 1, 27) + Table(4) + Table(6);
            case 3: return Table(4);
            case 4: return 2 + _stringWidth + _blobWidth;
            case 5: return Table(6);
            case 6: return 8 + _stringWidth + _blobWidth + Table(8);
            case 7: return Table(8);
            case 8: return 4 + _stringWidth;
            case 9: return Table(2) + Coded(2, 2, 1, 27);
            case 10: return Coded(3, 2, 1, 26, 6, 27) + _stringWidth + _blobWidth;
            case 11: return 2 + Coded(2, 4, 8, 23) + _blobWidth;
            case 12: return Coded(5, 6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43) + Coded(3, 6, 10) + _blobWidth;
            default: throw new InvalidDataException();
        }
    }
    private string String(int index)
    {
        var at = checked(_strings + index); var start = at;
        Require(at, 1);
        while (at < _data.Length && _data[at] != 0 && at - start <= 1024) at++;
        Require(at, 1);
        return Encoding.UTF8.GetString(_data, start, at - start);
    }
    private int Compressed(ref int at)
    {
        Require(at, 1); var first = _data[at++];
        if ((first & 128) == 0) return first;
        if ((first & 192) == 128) { Require(at, 1); return ((first & 63) << 8) | _data[at++]; }
        if ((first & 224) != 192) throw new InvalidDataException("压缩整数无效");
        Require(at, 3); return ((first & 31) << 24) | (_data[at++] << 16) | (_data[at++] << 8) | _data[at++];
    }
    private int Blob(int index, out int end)
    {
        var at = checked(_blobs + index); var length = Compressed(ref at); Require(at, length); end = checked(at + length); return at;
    }
    private int Rva(uint rva, int sections, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var section = sections + i * 40;
            var start = U32(section + 12); var rawSize = U32(section + 16);
            if (rva >= start && rva - start < rawSize) return checked((int)(U32(section + 20) + rva - start));
        }
        throw new InvalidDataException("DLL RVA 无效");
    }
    private List<string> Commands()
    {
        if (U16(0) != 0x5a4d) throw new InvalidDataException("不是 PE DLL");
        var pe = checked((int)U32(0x3c));
        if (U32(pe) != 0x4550) throw new InvalidDataException("PE 签名无效");
        var optional = pe + 24; var magic = U16(optional);
        if (magic != 0x10b && magic != 0x20b) throw new InvalidDataException("PE 格式不支持");
        var sections = optional + U16(pe + 20); var sectionCount = U16(pe + 6);
        var cliRva = U32(optional + (magic == 0x20b ? 112 : 96) + 14 * 8);
        if (cliRva == 0) return new List<string>();
        var cli = Rva(cliRva, sections, sectionCount);
        var metadata = Rva(U32(cli + 8), sections, sectionCount);
        if (U32(metadata) != 0x424a5342) throw new InvalidDataException("CLI 元数据签名无效");
        var header = checked(metadata + 16 + (int)U32(metadata + 12));
        header = (header + 3) & ~3;
        var streamCount = U16(header + 2); var cursor = header + 4; var tables = 0;
        for (var i = 0; i < streamCount; i++)
        {
            var offset = checked(metadata + (int)U32(cursor)); var nameAt = cursor + 8; var end = nameAt;
            while (true) { Require(end, 1); if (_data[end] == 0) break; end++; if (end - nameAt > 32) throw new InvalidDataException(); }
            var name = Encoding.ASCII.GetString(_data, nameAt, end - nameAt);
            if (name == "#~" || name == "#-") tables = offset;
            else if (name == "#Strings") _strings = offset;
            else if (name == "#Blob") _blobs = offset;
            cursor = (end + 4) & ~3;
        }
        if (tables == 0 || _strings == 0 || _blobs == 0) throw new InvalidDataException("缺少元数据流");
        Require(tables, 24); var heap = _data[tables + 6];
        _stringWidth = (heap & 1) != 0 ? 4 : 2; _guidWidth = (heap & 2) != 0 ? 4 : 2; _blobWidth = (heap & 4) != 0 ? 4 : 2;
        var valid = BitConverter.ToUInt64(_data, tables + 8); cursor = tables + 24;
        for (var i = 0; i < 64; i++) if ((valid & (1ul << i)) != 0) { _rows[i] = U32(cursor); cursor += 4; }
        for (var i = 0; i <= 12; i++) { _offsets[i] = cursor; cursor = checked(cursor + checked((int)_rows[i]) * Size(i)); Require(_offsets[i], cursor - _offsets[i]); }
        var result = new List<string>();
        for (var i = 0; i < _rows[12]; i++)
        {
            cursor = _offsets[12] + i * Size(12);
            var parent = Index(ref cursor, Coded(5, 6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43));
            var constructor = Index(ref cursor, Coded(3, 6, 10)); var value = Index(ref cursor, _blobWidth);
            if ((parent & 31) != 0 || (parent >> 5) == 0 || (parent >> 5) > _rows[6] || (constructor & 7) != 3) continue;
            var member = constructor >> 3; if (member <= 0 || member > _rows[10]) continue;
            var memberAt = _offsets[10] + (member - 1) * Size(10);
            var owner = Index(ref memberAt, Coded(3, 2, 1, 26, 6, 27));
            var memberName = String(Index(ref memberAt, _stringWidth)); var signature = Index(ref memberAt, _blobWidth);
            if (memberName != ".ctor" || (owner & 7) != 1 || (owner >> 3) <= 0 || (owner >> 3) > _rows[1]) continue;
            var typeAt = _offsets[1] + ((owner >> 3) - 1) * Size(1);
            Index(ref typeAt, Coded(2, 0, 26, 35, 1));
            var typeName = String(Index(ref typeAt, _stringWidth)); var ns = String(Index(ref typeAt, _stringWidth));
            if (typeName != "CommandMethodAttribute" || (ns != "Autodesk.AutoCAD.Runtime" && ns != "ZwSoft.ZwCAD.Runtime")) continue;
            var sig = Blob(signature, out var sigEnd); if (sig >= sigEnd) continue;
            var convention = _data[sig++]; if ((convention & 16) != 0) Compressed(ref sig);
            var parameters = Compressed(ref sig); if (sig >= sigEnd || _data[sig++] != 1) continue;
            var strings = 0; while (sig < sigEnd && strings < parameters && _data[sig] == 14) { strings++; sig++; }
            if (strings == 0) continue;
            var blob = Blob(value, out var blobEnd); if (blob + 2 > blobEnd || U16(blob) != 1) continue; blob += 2;
            string command = null;
            for (var n = 0; n < (strings == 1 ? 1 : 2); n++)
            {
                if (blob >= blobEnd) throw new InvalidDataException();
                if (_data[blob] == 255) { blob++; command = null; continue; }
                var length = Compressed(ref blob); if (blob > blobEnd - length) throw new InvalidDataException();
                command = Encoding.UTF8.GetString(_data, blob, length); blob += length;
            }
            if (ExtensionCommandDiscovery.IsCommandName(command)) result.Add(command.ToUpperInvariant());
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
    }
}
