using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Hartonomous.Core.Primitives;

/// <summary>
/// Represents a fixed 128-bit identifier derived from a BLAKE3 hash.
/// Stores the value as a generic 128-bit integer, providing explicit High/Low access
/// and deterministic conversion to/from Guid for database storage.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct HartonomousId : IEquatable<HartonomousId>, IComparable<HartonomousId>
{
    private readonly UInt128 _value;

    public HartonomousId(UInt128 value)
    {
        _value = value;
    }

    public HartonomousId(ulong high, ulong low)
    {
        _value = new UInt128(high, low);
    }

    /// <summary>
    /// Gets the upper 64 bits of the 128-bit ID.
    /// </summary>
    public ulong High => (ulong)(_value >> 64);

    /// <summary>
    /// Gets the lower 64 bits of the 128-bit ID.
    /// </summary>
    public ulong Low => (ulong)_value;

    /// <summary>
    /// Gets the Tier level of this ID (Bits 1-7).
    /// </summary>
    public byte Tier => (byte)((Low >> 1) & 0x7F);

    /// <summary>
    /// Gets whether this ID represents an Atom (Bit 0 == 1).
    /// </summary>
    public bool IsAtom => (Low & 1) == 1;

    /// <summary>
    /// Creates a tiered HartonomousId from a raw BLAKE3 hash.
    /// Enforces: Bit 0 = Parity (1 for Atom, 0 for Trajectory), Bits 1-7 = Tier Level.
    /// </summary>
    public static HartonomousId Create(ReadOnlySpan<byte> hash, byte level)
    {
        if (hash.Length < 16)
            throw new ArgumentException("Hash must be at least 16 bytes.");

        ulong high = BinaryPrimitives.ReadUInt64BigEndian(hash[..8]);
        ulong low = BinaryPrimitives.ReadUInt64BigEndian(hash[8..16]);

        // Clear the first 8 bits (Level + Parity)
        low &= 0xFFFFFFFFFFFFFF00;
        
        // Set Level (Bits 1-7)
        low |= ((ulong)level << 1);
        
        // Set Parity (Bit 0: 1 if Level 0, else 0)
        if (level == 0) low |= 1;

        return new HartonomousId(high, low);
    }
    
    /// <summary>
    /// Creates a HartonomousId from a 16-byte raw buffer (assumes machine endianness if direct cast, 
    /// but for IDs we typically enforce a convention. We default to BigEndian for consistency with Network byte order and standard Hash display).
    /// </summary>
    public static HartonomousId FromBytes(ReadOnlySpan<byte> bytes) => FromBigEndianBytes(bytes);

    private static HartonomousId FromBigEndianBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16)
            throw new ArgumentException("Bytes must be at least 16 bytes long.", nameof(bytes));

        ulong high = BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]);
        ulong low = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..16]);
        return new HartonomousId(high, low);
    }

    /// <summary>
    /// Writes the 128-bit ID to a span as big-endian bytes (standard network/hash order).
    /// </summary>
    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < 16)
            throw new ArgumentException("Destination must be at least 16 bytes long.", nameof(destination));

        BinaryPrimitives.WriteUInt64BigEndian(destination[..8], High);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..16], Low);
    }

    /// <summary>
    /// Converts the ID to a standard Guid.
    /// Note: This performs a byte shuffling to ensure that the string representation of the Guid
    /// matches the hex representation of the original Big-Endian hash.
    /// This is crucial for Postgres storage where UUIDs are big-endian.
    /// </summary>
    public Guid ToGuid()
    {
        // Guid is: int32 (4), int16 (2), int16 (2), byte[8]
        // If we want the Guid.ToString() to match "00112233-4455-6677-8899-aabbccddeeff"
        // and our _value (High/Low) corresponds to that hex sequence...
        
        // 1. Get bytes in Big Endian (the logical order)
        Span<byte> bytes = stackalloc byte[16];
        WriteBytes(bytes);

        // 2. Guid constructor Guid(int a, short b, short c, byte[] d) expects:
        //    a: bytes[0..4] (but as int, so it depends on endianness of system? No, it takes raw values)
        //    Actually, simpler: use the byte-array constructor of Guid.
        //    BUT Guid(byte[]) expects the first 3 parts to be LITTLE ENDIAN relative to the string.
        //    
        //    String: 00112233-4455-6677-8899-aabbccddeeff
        //    Bytes:  33 22 11 00 - 55 44 - 77 66 - 88 99 aa bb cc dd ee ff
        
        // So we need to swap the first 3 parts from our Big Endian layout.
        
        // Swap Part A (0-3)
        byte t0 = bytes[0]; bytes[0] = bytes[3]; bytes[3] = t0;
        byte t1 = bytes[1]; bytes[1] = bytes[2]; bytes[2] = t1;
        
        // Swap Part B (4-5)
        byte t4 = bytes[4]; bytes[4] = bytes[5]; bytes[5] = t4;

        // Swap Part C (6-7)
        byte t6 = bytes[6]; bytes[6] = bytes[7]; bytes[7] = t6;

        // Parts D, E (8-15) are already correct (Big Endian in Guid internal layout)

        return new Guid(bytes);
    }

    public static HartonomousId FromGuid(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);

        // Un-swap to get back to pure Big Endian
        
        // Swap Part A (0-3)
        byte t0 = bytes[0]; bytes[0] = bytes[3]; bytes[3] = t0;
        byte t1 = bytes[1]; bytes[1] = bytes[2]; bytes[2] = t1;
        
        // Swap Part B (4-5)
        byte t4 = bytes[4]; bytes[4] = bytes[5]; bytes[5] = t4;

        // Swap Part C (6-7)
        byte t6 = bytes[6]; bytes[6] = bytes[7]; bytes[7] = t6;

        return FromBigEndianBytes(bytes);
    }

    public override string ToString() => $"{High:x16}{Low:x16}";

    public bool Equals(HartonomousId other) => _value == other._value;
    public override bool Equals(object? obj) => obj is HartonomousId other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public int CompareTo(HartonomousId other) => _value.CompareTo(other._value);

    public static bool operator ==(HartonomousId left, HartonomousId right) => left.Equals(right);
    public static bool operator !=(HartonomousId left, HartonomousId right) => !left.Equals(right);
}
