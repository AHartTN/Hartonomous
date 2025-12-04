using Hartonomous.Core.Domain.Common;
using System.Security.Cryptography;
using System.Text;

namespace Hartonomous.Core.Domain.ValueObjects;

/// <summary>
/// SHA-256 hash value object for content-addressable storage
/// Immutable 256-bit hash with proper equality semantics
/// </summary>
public sealed class Hash256 : ValueObject
{
    public const int ByteLength = 32;
    public const int HexLength = 64;
    
    private readonly byte[] _bytes;
    
    public byte[] Bytes => (byte[])_bytes.Clone();
    public string Hex => Convert.ToHexString(_bytes).ToLowerInvariant();
    
    private Hash256(byte[] bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException($"Hash must be exactly {ByteLength} bytes", nameof(bytes));
        }
        _bytes = (byte[])bytes.Clone();
    }
    
    public static Hash256 FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length != ByteLength)
        {
            throw new ArgumentException($"Hash must be exactly {ByteLength} bytes", nameof(bytes));
        }
        return new Hash256(bytes);
    }
    
    public static Hash256 FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new ArgumentException("Hash hex string cannot be null or empty", nameof(hex));
        }
        
        hex = hex.Replace("-", "").Replace(" ", "").ToLowerInvariant();
        
        if (hex.Length != HexLength)
        {
            throw new ArgumentException($"Hash hex string must be exactly {HexLength} characters", nameof(hex));
        }
        
        var bytes = Convert.FromHexString(hex);
        return new Hash256(bytes);
    }
    
    public static Hash256 Compute(byte[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        
        var hashBytes = SHA256.HashData(data);
        return new Hash256(hashBytes);
    }
    
    public static Hash256 Compute(string data, Encoding? encoding = null)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        
        encoding ??= Encoding.UTF8;
        var bytes = encoding.GetBytes(data);
        return Compute(bytes);
    }
    
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Hex;
    }
    
    public override string ToString() => Hex;
}
