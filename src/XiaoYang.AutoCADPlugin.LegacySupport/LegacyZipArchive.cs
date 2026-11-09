using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// CLR 2 adapter for the small XLSX archives used by command import/export.
// Reads stored/deflated ZIP entries, verifies lengths/CRC, writes stored ZIPs.
// Installation/update ZIPs continue to use the existing CLR 4 updater.
namespace System.IO.Compression;

internal enum ZipArchiveMode { Read, Create }
internal enum CompressionLevel { Optimal }

internal sealed class ZipArchive : IDisposable
{
    private const long MaximumEntryBytes = 64 * 1024 * 1024;
    private readonly Stream _stream;
    private readonly ZipArchiveMode _mode;
    private readonly bool _leaveOpen;
    private readonly List<ZipArchiveEntry> _entries = new();
    private bool _disposed;
    internal IEnumerable<ZipArchiveEntry> Entries => _entries.AsReadOnly();

    internal ZipArchive(Stream stream, ZipArchiveMode mode, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _mode = mode; _leaveOpen = leaveOpen;
        if (!stream.CanSeek) throw new NotSupportedException("Excel ZIP input must be seekable.");
        if (mode == ZipArchiveMode.Read) ReadDirectory();
    }
    internal ZipArchiveEntry GetEntry(string name) => _entries.Find(entry => entry.FullName == name);
    internal ZipArchiveEntry CreateEntry(string name, CompressionLevel level)
    {
        if (_mode != ZipArchiveMode.Create || _disposed) throw new InvalidOperationException("Archive is not writable.");
        if (GetEntry(name) != null) throw new InvalidDataException("Duplicate ZIP entry.");
        var entry = new ZipArchiveEntry(this, name); _entries.Add(entry); return entry;
    }
    private void ReadDirectory()
    {
        if (_stream.Length < 22) throw new InvalidDataException("Excel ZIP directory is missing.");
        var size = (int)Math.Min(_stream.Length, 65557);
        var tail = new byte[size]; _stream.Position = _stream.Length - size; ReadExactly(_stream, tail);
        var end = -1;
        for (var index = size - 22; index >= 0; index--)
            if (BitConverter.ToUInt32(tail, index) == 0x06054b50 && index + 22 + BitConverter.ToUInt16(tail, index + 20) == size) { end = index; break; }
        if (end < 0) throw new InvalidDataException("Excel ZIP directory is invalid.");
        var count = BitConverter.ToUInt16(tail, end + 10);
        var centralLength = BitConverter.ToUInt32(tail, end + 12);
        var centralOffset = BitConverter.ToUInt32(tail, end + 16);
        if (BitConverter.ToUInt16(tail, end + 4) != 0 || BitConverter.ToUInt16(tail, end + 6) != 0 ||
            BitConverter.ToUInt16(tail, end + 8) != count || count > 10000 || centralOffset == uint.MaxValue ||
            (long)centralOffset + centralLength > _stream.Length - size + end)
            throw new InvalidDataException("Split/ZIP64 or oversized Excel archives are unsupported.");
        _stream.Position = centralOffset;
        var reader = new BinaryReader(_stream, Encoding.UTF8);
        long total = 0;
        for (var index = 0; index < count; index++)
        {
            if (reader.ReadUInt32() != 0x02014b50) throw new InvalidDataException("ZIP central entry is invalid.");
            reader.ReadUInt16(); reader.ReadUInt16();
            var flags = reader.ReadUInt16(); var method = reader.ReadUInt16();
            reader.ReadUInt32(); var crc = reader.ReadUInt32();
            var compressed = reader.ReadUInt32(); var length = reader.ReadUInt32();
            var nameLength = reader.ReadUInt16(); var extraLength = reader.ReadUInt16(); var commentLength = reader.ReadUInt16();
            var disk = reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt32(); var offset = reader.ReadUInt32();
            total += length;
            if ((flags & 1) != 0 || (method != 0 && method != 8) || disk != 0 ||
                length > MaximumEntryBytes || compressed > MaximumEntryBytes || total > MaximumEntryBytes * 4 ||
                offset >= centralOffset || nameLength == 0)
                throw new InvalidDataException("Encrypted, oversized or unsupported Excel ZIP entry.");
            var nameBytes = reader.ReadBytes(nameLength);
            if (nameBytes.Length != nameLength) throw new EndOfStreamException();
            var name = ((flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(437)).GetString(nameBytes);
            if (GetEntry(name) != null) throw new InvalidDataException("Duplicate Excel ZIP entry.");
            _stream.Seek(extraLength + commentLength, SeekOrigin.Current);
            _entries.Add(new ZipArchiveEntry(this, name) { Method = method, Crc = crc, Length = length, CompressedLength = compressed, Offset = offset });
        }
        if (_stream.Position != (long)centralOffset + centralLength) throw new InvalidDataException("ZIP central directory length mismatch.");
    }
    internal Stream Open(ZipArchiveEntry entry)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ZipArchive));
        if (_mode == ZipArchiveMode.Create)
        {
            if (entry.Opened) throw new InvalidOperationException("ZIP entry was already opened.");
            entry.Opened = true;
            return new EntryWriter(data => { if (data.Length > MaximumEntryBytes) throw new InvalidDataException("Excel entry is too large."); entry.Data = data; });
        }
        _stream.Position = entry.Offset;
        var reader = new BinaryReader(_stream, Encoding.UTF8);
        if (reader.ReadUInt32() != 0x04034b50) throw new InvalidDataException("ZIP local entry is invalid.");
        reader.ReadUInt16(); var flags = reader.ReadUInt16(); var method = reader.ReadUInt16();
        reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32();
        var nameLength = reader.ReadUInt16(); var extraLength = reader.ReadUInt16();
        if ((flags & 1) != 0 || method != entry.Method) throw new InvalidDataException("ZIP entry metadata mismatch.");
        _stream.Seek(nameLength + extraLength, SeekOrigin.Current);
        if (_stream.Position + entry.CompressedLength > _stream.Length) throw new InvalidDataException("ZIP entry is truncated.");
        var compressed = new byte[entry.CompressedLength]; ReadExactly(_stream, compressed);
        using var input = new MemoryStream(compressed, false);
        using var output = new MemoryStream();
        using (var decoded = entry.Method == 8 ? (Stream)new DeflateStream(input, CompressionMode.Decompress) : input)
        {
            var buffer = new byte[8192]; int read;
            while ((read = decoded.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > entry.Length) throw new InvalidDataException("Excel ZIP entry expands beyond its declared length.");
                output.Write(buffer, 0, read);
            }
        }
        var bytes = output.ToArray();
        if (bytes.LongLength != entry.Length || ComputeCrc(bytes) != entry.Crc) throw new InvalidDataException("Excel ZIP length/CRC check failed.");
        return new MemoryStream(bytes, false);
    }
    public void Dispose()
    {
        if (_disposed) return;
        try { if (_mode == ZipArchiveMode.Create) WriteArchive(); }
        finally { _disposed = true; if (!_leaveOpen) _stream.Dispose(); }
    }
    private void WriteArchive()
    {
        var writer = new BinaryWriter(_stream, Encoding.UTF8);
        foreach (var entry in _entries)
        {
            if (entry.Data == null) throw new InvalidOperationException("Excel ZIP entry is still open.");
            entry.Offset = checked((uint)_stream.Position); entry.Crc = ComputeCrc(entry.Data);
            var name = Encoding.UTF8.GetBytes(entry.FullName);
            writer.Write(0x04034b50u); writer.Write((ushort)20); writer.Write((ushort)0x800); writer.Write((ushort)0);
            writer.Write(0u); writer.Write(entry.Crc); writer.Write((uint)entry.Data.Length); writer.Write((uint)entry.Data.Length);
            writer.Write(checked((ushort)name.Length)); writer.Write((ushort)0); writer.Write(name); writer.Write(entry.Data);
        }
        var start = checked((uint)_stream.Position);
        foreach (var entry in _entries)
        {
            var name = Encoding.UTF8.GetBytes(entry.FullName);
            writer.Write(0x02014b50u); writer.Write((ushort)20); writer.Write((ushort)20); writer.Write((ushort)0x800); writer.Write((ushort)0);
            writer.Write(0u); writer.Write(entry.Crc); writer.Write((uint)entry.Data.Length); writer.Write((uint)entry.Data.Length);
            writer.Write((ushort)name.Length); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(0u); writer.Write(entry.Offset); writer.Write(name);
        }
        var length = checked((uint)_stream.Position - start);
        writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(checked((ushort)_entries.Count)); writer.Write(checked((ushort)_entries.Count));
        writer.Write(length); writer.Write(start); writer.Write((ushort)0); writer.Flush();
    }
    private static void ReadExactly(Stream input, byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length) { var read = input.Read(bytes, offset, bytes.Length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
    }
    private static uint ComputeCrc(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1; }
        return ~crc;
    }
    private sealed class EntryWriter : MemoryStream
    {
        private readonly Action<byte[]> _save; private bool _saved;
        internal EntryWriter(Action<byte[]> save) { _save = save; }
        protected override void Dispose(bool disposing) { if (disposing && !_saved) { _saved = true; _save(ToArray()); } base.Dispose(disposing); }
    }
}

internal sealed class ZipArchiveEntry
{
    private readonly ZipArchive _archive;
    internal string FullName { get; }
    internal ushort Method;
    internal uint Crc, Length, CompressedLength, Offset;
    internal bool Opened;
    internal byte[] Data;
    internal ZipArchiveEntry(ZipArchive archive, string name) { _archive = archive; FullName = name; }
    internal Stream Open() => _archive.Open(this);
}
