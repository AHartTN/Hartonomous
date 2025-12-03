using Hartonomous.Db;
using Hartonomous.Db.Entities;
using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hartonomous.Api.Services;

public class AtomService : IAtomService
{
    private readonly HartonomousDbContext _context;

    public AtomService(HartonomousDbContext context)
    {
        _context = context;
    }

    public async Task<AtomDto?> GetAtomByIdAsync(long id)
    {
        var atom = await _context.Atoms.FindAsync(id);
        return atom == null ? null : MapToDto(atom);
    }

    public async Task<AtomDto?> GetAtomByHashAsync(string contentHash)
    {
        var atom = await _context.Atoms.FirstOrDefaultAsync(a => a.ContentHash == contentHash);
        return atom == null ? null : MapToDto(atom);
    }

    public async Task<List<AtomDto>> GetAtomsByTypeAsync(string atomType, int skip = 0, int take = 100)
    {
        var atoms = await _context.Atoms
            .Where(a => a.AtomType == atomType)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return atoms.Select(MapToDto).ToList();
    }

    public async Task<AtomDto> CreateAtomAsync(CreateAtomRequest request)
    {
        if (request.AtomicValue != null && request.AtomicValue.Length > 64)
        {
            throw new ArgumentException("AtomicValue cannot exceed 64 bytes. Use composition for larger data.");
        }

        var contentHash = ComputeHash(request.AtomicValue, request.AtomType, request.Metadata);

        var existing = await _context.Atoms.FirstOrDefaultAsync(a => a.ContentHash == contentHash);
        if (existing != null)
        {
            return MapToDto(existing);
        }

        var atom = new Atom
        {
            ContentHash = contentHash,
            AtomicValue = request.AtomicValue,
            AtomType = request.AtomType,
            ParentAtomId = request.ParentAtomId,
            Metadata = request.Metadata != null ? JsonSerializer.Serialize(request.Metadata) : null,
            CreatedAt = DateTime.UtcNow
        };

        _context.Atoms.Add(atom);
        await _context.SaveChangesAsync();

        return MapToDto(atom);
    }

    public async Task<bool> DeleteAtomAsync(long id)
    {
        var atom = await _context.Atoms.FindAsync(id);
        if (atom == null) return false;

        _context.Atoms.Remove(atom);
        await _context.SaveChangesAsync();
        return true;
    }

    private static AtomDto MapToDto(Atom atom)
    {
        return new AtomDto
        {
            Id = atom.Id,
            ContentHash = atom.ContentHash,
            AtomicValue = atom.AtomicValue,
            AtomType = atom.AtomType,
            ParentAtomId = atom.ParentAtomId,
            CreatedAt = atom.CreatedAt,
            Metadata = atom.Metadata != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(atom.Metadata) : null
        };
    }

    private static string ComputeHash(byte[]? value, string type, Dictionary<string, object>? metadata)
    {
        using var sha256 = SHA256.Create();
        var combined = new List<byte>();

        if (value != null) combined.AddRange(value);
        combined.AddRange(Encoding.UTF8.GetBytes(type));
        if (metadata != null) combined.AddRange(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)));

        var hash = sha256.ComputeHash(combined.ToArray());
        return Convert.ToHexString(hash);
    }
}
