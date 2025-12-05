using Hartonomous.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Services.BPE;

/// <summary>
/// Helper methods for detecting gaps in Hilbert-sorted constant sequences
/// and building dense segments for BPE vocabulary learning
/// </summary>
public static class HilbertGapDetector
{
    /// <summary>
    /// Detect gaps in a Hilbert-sorted sequence of constants
    /// </summary>
    /// <param name="sortedConstants">Constants sorted by Hilbert index</param>
    /// <param name="gapThreshold">Minimum gap size to detect</param>
    /// <param name="logger">Optional logger for trace output</param>
    /// <returns>List of detected gaps</returns>
    public static List<HilbertGap> DetectGaps(
        IReadOnlyList<Constant> sortedConstants,
        ulong gapThreshold,
        ILogger? logger = null)
    {
        if (sortedConstants == null || sortedConstants.Count < 2)
            return new List<HilbertGap>();

        var gaps = new List<HilbertGap>();

        for (int i = 0; i < sortedConstants.Count - 1; i++)
        {
            var current = sortedConstants[i];
            var next = sortedConstants[i + 1];

            if (current.Coordinate == null || next.Coordinate == null)
                continue;

            var gap = next.Coordinate.HilbertHigh - current.Coordinate.HilbertHigh;

            if (gap > gapThreshold)
            {
                var hilbertGap = new HilbertGap
                {
                    StartIndex = i,
                    EndIndex = i + 1,
                    StartHilbert = current.Coordinate.HilbertHigh,
                    EndHilbert = next.Coordinate.HilbertHigh,
                    GapSize = gap,
                    IsSparse = gap > gapThreshold * 10
                };

                gaps.Add(hilbertGap);

                logger?.LogTrace(
                    "Gap detected: [{Start}?{End}] size={Size} sparse={Sparse}",
                    hilbertGap.StartHilbert,
                    hilbertGap.EndHilbert,
                    hilbertGap.GapSize,
                    hilbertGap.IsSparse);
            }
        }

        return gaps;
    }

    /// <summary>
    /// Build dense segments from constants by splitting at gaps
    /// </summary>
    /// <param name="sortedConstants">Constants sorted by Hilbert index</param>
    /// <param name="gaps">Detected gaps to split at</param>
    /// <returns>List of dense constant segments</returns>
    public static List<List<Constant>> BuildSegments(
        IReadOnlyList<Constant> sortedConstants,
        IReadOnlyList<HilbertGap> gaps)
    {
        if (sortedConstants == null || sortedConstants.Count == 0)
            return new List<List<Constant>>();

        if (gaps == null || gaps.Count == 0)
            return new List<List<Constant>> { new List<Constant>(sortedConstants) };

        var segments = new List<List<Constant>>();
        var currentSegment = new List<Constant>();
        var nextGapIndex = 0;

        for (int i = 0; i < sortedConstants.Count; i++)
        {
            currentSegment.Add(sortedConstants[i]);

            // Check if we hit a gap
            if (nextGapIndex < gaps.Count && i == gaps[nextGapIndex].StartIndex)
            {
                if (currentSegment.Count > 1)
                {
                    segments.Add(currentSegment);
                }
                currentSegment = new List<Constant>();
                nextGapIndex++;
            }
        }

        // Add final segment
        if (currentSegment.Count > 1)
        {
            segments.Add(currentSegment);
        }

        return segments;
    }
}
