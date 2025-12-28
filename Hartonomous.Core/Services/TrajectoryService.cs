using System.Globalization;
using System.Text;
using Hartonomous.Core.Models;
using Hartonomous.Core.Native;
using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Core.Services;

/// <summary>
/// Trajectory operations - RLE-compressed paths through semantic space.
/// "Hello" → H(1), e(1), l(2), o(1) - NOT 5 separate records.
/// </summary>
public sealed class TrajectoryService : ITrajectoryService
{
    private readonly IDatabaseService _db;

    public TrajectoryService(IDatabaseService? db = null)
    {
        _db = db ?? DatabaseService.Instance;
    }

    /// <summary>
    /// Build RLE-compressed trajectory from text.
    /// Repeated characters are compressed: "aaa" → a(3).
    /// </summary>
    public TrajectoryPoint[] BuildTrajectory(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return [];

        _db.Initialize();

        var bytes = Encoding.UTF8.GetBytes(text);
        var points = new NativeInterop.NativeTrajectoryPoint[text.Length]; // Max possible points
        
        var status = NativeInterop.BuildTrajectory(bytes, bytes.Length, points, points.Length, out var pointCount);

        if (status < 0)
            throw new InvalidOperationException($"BuildTrajectory failed: error {status}");

        return ConvertPoints(points, pointCount);
    }

    /// <summary>
    /// Store trajectory as relationship between nodes.
    /// The trajectory is stored as a LineStringZM geometry.
    /// </summary>
    public void StoreTrajectory(
        NodeId source,
        NodeId target,
        TrajectoryPoint[] points,
        double weight,
        RelationshipType type = RelationshipType.SemanticLink,
        NodeId context = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length == 0)
            return;

        _db.Initialize();

        var nativePoints = new NativeInterop.NativeTrajectoryPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            nativePoints[i] = new NativeInterop.NativeTrajectoryPoint
            {
                Page = points[i].Page,
                Type = points[i].Type,
                Base = points[i].Base,
                Variant = points[i].Variant,
                Count = points[i].Count
            };
        }

        var status = NativeInterop.StoreTrajectory(
            source.High, source.Low,
            target.High, target.Low,
            nativePoints, points.Length,
            weight,
            (short)type,
            context.High, context.Low);

        if (status < 0)
            throw new InvalidOperationException($"StoreTrajectory failed: error {status}");
    }

    /// <summary>
    /// Convert trajectory back to text (expanding RLE).
    /// </summary>
    public string TrajectoryToText(TrajectoryPoint[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length == 0)
            return string.Empty;

        _db.Initialize();

        var nativePoints = new NativeInterop.NativeTrajectoryPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            nativePoints[i] = new NativeInterop.NativeTrajectoryPoint
            {
                Page = points[i].Page,
                Type = points[i].Type,
                Base = points[i].Base,
                Variant = points[i].Variant,
                Count = points[i].Count
            };
        }

        // Estimate buffer size: sum of all counts * 4 (max UTF-8 bytes per char)
        var estimatedSize = 0u;
        foreach (var p in points)
            estimatedSize += p.Count * 4;
        
        var buffer = new byte[Math.Max(estimatedSize, 1024)];
        
        var status = NativeInterop.TrajectoryToText(nativePoints, points.Length, buffer, buffer.Length, out var textLen);

        if (status == -1)
        {
            // Buffer too small, retry with larger
            buffer = new byte[textLen + 1024];
            status = NativeInterop.TrajectoryToText(nativePoints, points.Length, buffer, buffer.Length, out textLen);
        }

        if (status < 0)
            throw new InvalidOperationException($"TrajectoryToText failed: error {status}");

        return Encoding.UTF8.GetString(buffer, 0, textLen);
    }

    /// <summary>
    /// Get RLE representation as string: "H(1)e(1)l(2)o(1)"
    /// </summary>
    public static string ToRleString(TrajectoryPoint[] points)
    {
        var sb = new StringBuilder();
        foreach (var p in points)
        {
            var cp = ToCodepoint(p);
            if (cp >= 32 && cp <= 126)
                sb.Append((char)cp);
            else
                sb.Append(CultureInfo.InvariantCulture, $"\\u{cp:X4}");
            
            if (p.Count > 1)
                sb.Append(CultureInfo.InvariantCulture, $"(x{p.Count})");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Get expanded length (total characters after RLE expansion).
    /// </summary>
    public static int GetExpandedLength(TrajectoryPoint[] points)
    {
        var length = 0;
        foreach (var p in points)
            length += (int)p.Count;
        return length;
    }

    private static int ToCodepoint(TrajectoryPoint p)
    {
        // Simplified inverse of semantic decomposition for common cases
        // Full implementation would need SemanticDecompose.to_codepoint logic
        if (p.Variant == 0)
            return p.Base;
        return p.Base; // Fallback
    }

    private static TrajectoryPoint[] ConvertPoints(NativeInterop.NativeTrajectoryPoint[] native, int count)
    {
        var result = new TrajectoryPoint[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new TrajectoryPoint(
                native[i].Page,
                native[i].Type,
                native[i].Base,
                native[i].Variant,
                native[i].Count);
        }
        return result;
    }
}
