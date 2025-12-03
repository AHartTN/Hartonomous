using Hartonomous.Db;
using Hartonomous.Db.Entities;
using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Pgvector;
using System.Security.Cryptography;

namespace Hartonomous.Api.Services;

public class TensorService : ITensorService
{
    private readonly HartonomousDbContext _context;

    public TensorService(HartonomousDbContext context)
    {
        _context = context;
    }

    public async Task<TensorChunkDto?> GetChunkByIdAsync(long id)
    {
        var chunk = await _context.TensorChunks.FindAsync(id);
        return chunk == null ? null : MapToDto(chunk);
    }

    public async Task<List<TensorChunkDto>> GetChunksByTensorNameAsync(string tensorName, int skip = 0, int take = 100)
    {
        var chunks = await _context.TensorChunks
            .Where(c => c.TensorName == tensorName)
            .OrderBy(c => c.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return chunks.Select(MapToDto).ToList();
    }

    public async Task<TensorChunkDto> CreateChunkAsync(CreateTensorChunkRequest request)
    {
        var contentHash = ComputeHash(request.BinaryPayload);

        var existing = await _context.TensorChunks.FirstOrDefaultAsync(c => c.ContentHash == contentHash);
        if (existing != null)
        {
            return MapToDto(existing);
        }

        var chunk = new TensorChunk
        {
            AtomId = request.AtomId,
            TensorName = request.TensorName,
            Shape = request.Shape,
            ChunkStart = request.ChunkStart,
            ChunkEnd = request.ChunkEnd,
            BinaryPayload = request.BinaryPayload,
            ContentHash = contentHash,
            StructuralCoordinate = CreateStructuralCoordinate(request.ChunkStart),
            Embedding = request.Embedding != null ? new Vector(request.Embedding.Select(d => (float)d).ToArray()) : null,
            CreatedAt = DateTime.UtcNow
        };

        _context.TensorChunks.Add(chunk);
        await _context.SaveChangesAsync();

        return MapToDto(chunk);
    }

    public async Task<List<TensorChunkDto>> SearchSimilarChunksAsync(TensorSearchRequest request)
    {
        var queryVector = new Vector(request.QueryVector.Select(d => (float)d).ToArray());

        var chunks = await _context.TensorChunks
            .Where(c => c.Embedding != null)
            .OrderBy(c => c.Embedding!.L2Distance(queryVector))
            .Take(request.Limit)
            .ToListAsync();

        return chunks.Select(MapToDto).ToList();
    }

    private static TensorChunkDto MapToDto(TensorChunk chunk)
    {
        return new TensorChunkDto
        {
            Id = chunk.Id,
            AtomId = chunk.AtomId,
            TensorName = chunk.TensorName,
            Shape = chunk.Shape,
            ChunkStart = chunk.ChunkStart,
            ChunkEnd = chunk.ChunkEnd,
            ContentHash = chunk.ContentHash,
            CreatedAt = chunk.CreatedAt
        };
    }

    private static Point CreateStructuralCoordinate(int[] chunkStart)
    {
        var x = chunkStart.Length > 0 ? chunkStart[0] : 0;
        var y = chunkStart.Length > 1 ? chunkStart[1] : 0;
        var z = chunkStart.Length > 2 ? chunkStart[2] : 0;

        return new Point(x, y, z) { SRID = 4326 };
    }

    private static string ComputeHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash);
    }
}
