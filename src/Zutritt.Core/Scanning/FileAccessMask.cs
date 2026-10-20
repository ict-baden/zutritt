// SPDX-License-Identifier: MIT
namespace Zutritt.Core.Scanning;

/// <summary>Zugriffsmasken für Dateien und Freigaben (winnt.h) und die Generic-Abbildung von Windows.</summary>
internal static class FileAccessMask
{
    public const uint ReadData = 0x0001;          // Ordner: auflisten
    public const uint WriteData = 0x0002;         // Ordner: Dateien anlegen
    public const uint AppendData = 0x0004;        // Ordner: Unterordner anlegen
    public const uint ReadEa = 0x0008;
    public const uint WriteEa = 0x0010;
    public const uint Execute = 0x0020;           // Ordner: durchqueren
    public const uint DeleteChild = 0x0040;
    public const uint ReadAttributes = 0x0080;
    public const uint WriteAttributes = 0x0100;
    public const uint Delete = 0x00010000;
    public const uint ReadControl = 0x00020000;
    public const uint WriteDac = 0x00040000;
    public const uint WriteOwner = 0x00080000;
    public const uint Synchronize = 0x00100000;

    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint GenericExecute = 0x20000000;
    public const uint GenericAll = 0x10000000;

    // Genau die Werte, mit denen Windows beim Vererben generische Rechte von Dateien auflöst (FILE_GENERIC_*).
    public const uint FileGenericRead = 0x00120089;
    public const uint FileGenericWrite = 0x00120116;
    public const uint FileGenericExecute = 0x001200A0;
    public const uint FileAllAccess = 0x001F01FF;

    /// <summary>GENERIC_*-Bits wie Windows in Dateirechte auflösen und entfernen.</summary>
    public static uint MapGeneric(uint mask)
    {
        var mapped = mask & 0x0FFFFFFF;
        if ((mask & GenericRead) != 0) mapped |= FileGenericRead;
        if ((mask & GenericWrite) != 0) mapped |= FileGenericWrite;
        if ((mask & GenericExecute) != 0) mapped |= FileGenericExecute;
        if ((mask & GenericAll) != 0) mapped |= FileAllAccess;
        return mapped;
    }
}
