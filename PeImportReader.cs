using System.Text;

namespace GamePivot;

internal sealed record PeImportResult(bool Is64Bit, IReadOnlyList<string> Imports);

internal static class PeImportReader
{
    private sealed record Section(uint VirtualAddress, uint VirtualSize, uint RawAddress, uint RawSize);

    public static PeImportResult Read(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 0x40 || ReadUInt16(data, 0) != 0x5A4D)
        {
            throw new InvalidDataException("The file is not a Windows PE executable.");
        }

        var peOffset = ReadInt32(data, 0x3C);
        EnsureRange(data, peOffset, 24);
        if (ReadUInt32(data, peOffset) != 0x00004550)
        {
            throw new InvalidDataException("The PE signature is invalid.");
        }

        var coffOffset = peOffset + 4;
        var sectionCount = ReadUInt16(data, coffOffset + 2);
        var optionalHeaderSize = ReadUInt16(data, coffOffset + 16);
        var optionalOffset = coffOffset + 20;
        EnsureRange(data, optionalOffset, optionalHeaderSize);

        var magic = ReadUInt16(data, optionalOffset);
        var is64Bit = magic == 0x20B;
        if (!is64Bit && magic != 0x10B)
        {
            throw new InvalidDataException("Unsupported PE optional header.");
        }

        var dataDirectoryOffset = optionalOffset + (is64Bit ? 112 : 96);
        var sections = ReadSections(data, optionalOffset + optionalHeaderSize, sectionCount);
        var imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ReadImportDirectory(data, sections, dataDirectoryOffset, imports);
        ReadDelayImportDirectory(data, sections, dataDirectoryOffset, imports);

        return new PeImportResult(
            is64Bit,
            imports.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<Section> ReadSections(byte[] data, int offset, int count)
    {
        var sections = new List<Section>(count);
        for (var index = 0; index < count; index++)
        {
            var sectionOffset = offset + index * 40;
            EnsureRange(data, sectionOffset, 40);
            sections.Add(new Section(
                ReadUInt32(data, sectionOffset + 12),
                ReadUInt32(data, sectionOffset + 8),
                ReadUInt32(data, sectionOffset + 20),
                ReadUInt32(data, sectionOffset + 16)));
        }

        return sections;
    }

    private static void ReadImportDirectory(
        byte[] data,
        IReadOnlyList<Section> sections,
        int dataDirectoryOffset,
        ISet<string> imports)
    {
        var importEntry = dataDirectoryOffset + 8;
        EnsureRange(data, importEntry, 8);
        var importRva = ReadUInt32(data, importEntry);
        if (importRva == 0)
        {
            return;
        }

        var descriptorOffset = RvaToOffset(importRva, sections);
        for (var index = 0; index < 4096; index++)
        {
            var current = descriptorOffset + index * 20;
            EnsureRange(data, current, 20);
            var originalThunk = ReadUInt32(data, current);
            var timestamp = ReadUInt32(data, current + 4);
            var forwarder = ReadUInt32(data, current + 8);
            var nameRva = ReadUInt32(data, current + 12);
            var firstThunk = ReadUInt32(data, current + 16);

            if (originalThunk == 0 && timestamp == 0 && forwarder == 0 &&
                nameRva == 0 && firstThunk == 0)
            {
                break;
            }

            if (nameRva != 0)
            {
                imports.Add(ReadAsciiString(data, RvaToOffset(nameRva, sections)));
            }
        }
    }

    private static void ReadDelayImportDirectory(
        byte[] data,
        IReadOnlyList<Section> sections,
        int dataDirectoryOffset,
        ISet<string> imports)
    {
        var delayEntry = dataDirectoryOffset + 13 * 8;
        EnsureRange(data, delayEntry, 8);
        var delayRva = ReadUInt32(data, delayEntry);
        if (delayRva == 0)
        {
            return;
        }

        var descriptorOffset = RvaToOffset(delayRva, sections);
        for (var index = 0; index < 4096; index++)
        {
            var current = descriptorOffset + index * 32;
            EnsureRange(data, current, 32);
            var attributes = ReadUInt32(data, current);
            var nameRva = ReadUInt32(data, current + 4);
            var moduleHandle = ReadUInt32(data, current + 8);

            if (attributes == 0 && nameRva == 0 && moduleHandle == 0)
            {
                break;
            }

            if ((attributes & 1) == 1 && nameRva != 0)
            {
                imports.Add(ReadAsciiString(data, RvaToOffset(nameRva, sections)));
            }
        }
    }

    private static int RvaToOffset(uint rva, IReadOnlyList<Section> sections)
    {
        foreach (var section in sections)
        {
            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
            {
                return checked((int)(section.RawAddress + (rva - section.VirtualAddress)));
            }
        }

        throw new InvalidDataException($"RVA 0x{rva:X8} does not map to a PE section.");
    }

    private static string ReadAsciiString(byte[] data, int offset)
    {
        EnsureRange(data, offset, 1);
        var end = offset;
        while (end < data.Length && data[end] != 0 && end - offset < 1024)
        {
            end++;
        }

        if (end == data.Length)
        {
            throw new InvalidDataException("Unterminated import name.");
        }

        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private static void EnsureRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("The PE file contains an invalid offset.");
        }
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        EnsureRange(data, offset, 2);
        return BitConverter.ToUInt16(data, offset);
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        EnsureRange(data, offset, 4);
        return BitConverter.ToUInt32(data, offset);
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        EnsureRange(data, offset, 4);
        return BitConverter.ToInt32(data, offset);
    }
}
