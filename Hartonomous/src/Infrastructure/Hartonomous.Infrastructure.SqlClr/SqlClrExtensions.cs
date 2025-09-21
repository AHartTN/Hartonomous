/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the SQL CLR Extensions - utility functions and extensions that
 * provide advanced capabilities for the Hartonomous AI Agent Factory Platform.
 * These functions extend SQL Server 2025's capabilities with .NET 8 innovation.
 *
 * Key Innovations Protected:
 * - SQL Server 2025 VECTOR data type utilities and conversions
 * - Advanced mathematical functions for AI model processing
 * - Multi-tenant security utilities and validation functions
 * - Performance optimization utilities for large-scale operations
 * - Integration helpers for Python.NET and memory-mapped file operations
 *
 * Any attempt to reverse engineer, extract, or replicate these utility
 * algorithms is prohibited by law and subject to legal action.
 */

using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Data.SqlTypes;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.Server;
using Microsoft.SqlServer.Types;

namespace Hartonomous.Infrastructure.SqlClr
{
    /// <summary>
    /// Extension functions and utilities for SQL CLR operations
    /// Provides advanced capabilities for AI model processing and analysis
    /// </summary>
    public static class SqlClrExtensions
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #region Vector Data Type Utilities

        /// <summary>
        /// Converts a float array to SQL Server 2025 VECTOR type
        /// Enables native vector operations in SQL Server
        /// </summary>
        /// <param name="values">Float array values</param>
        /// <returns>SQL VECTOR representation</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "CreateVector")]
        public static SqlString CreateVector(SqlString values)
        {
            try
            {
                if (values.IsNull || string.IsNullOrEmpty(values.Value))
                    return SqlString.Null;

                // Parse comma-separated float values
                var floatValues = values.Value
                    .Split(',')
                    .Select(v => float.Parse(v.Trim(), CultureInfo.InvariantCulture))
                    .ToArray();

                // Format as SQL Server VECTOR literal
                var vectorLiteral = $"VECTOR('[{string.Join(",", floatValues.Select(f => f.ToString("G", CultureInfo.InvariantCulture)))}]')";
                return new SqlString(vectorLiteral);
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error creating vector: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Calculates cosine similarity between two vectors
        /// Optimized for SQL Server 2025 VECTOR operations
        /// </summary>
        /// <param name="vector1">First vector as comma-separated values</param>
        /// <param name="vector2">Second vector as comma-separated values</param>
        /// <returns>Cosine similarity score</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "VectorCosineSimilarity")]
        public static SqlDouble VectorCosineSimilarity(SqlString vector1, SqlString vector2)
        {
            try
            {
                if (vector1.IsNull || vector2.IsNull)
                    return SqlDouble.Null;

                var v1 = ParseVector(vector1.Value);
                var v2 = ParseVector(vector2.Value);

                if (v1.Length != v2.Length)
                    throw new ArgumentException("Vectors must have the same dimension");

                var dotProduct = 0.0;
                var magnitude1 = 0.0;
                var magnitude2 = 0.0;

                for (var i = 0; i < v1.Length; i++)
                {
                    dotProduct += v1[i] * v2[i];
                    magnitude1 += v1[i] * v1[i];
                    magnitude2 += v2[i] * v2[i];
                }

                magnitude1 = Math.Sqrt(magnitude1);
                magnitude2 = Math.Sqrt(magnitude2);

                if (magnitude1 == 0 || magnitude2 == 0)
                    return new SqlDouble(0);

                return new SqlDouble(dotProduct / (magnitude1 * magnitude2));
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error calculating cosine similarity: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Calculates Euclidean distance between two vectors
        /// </summary>
        /// <param name="vector1">First vector</param>
        /// <param name="vector2">Second vector</param>
        /// <returns>Euclidean distance</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "VectorEuclideanDistance")]
        public static SqlDouble VectorEuclideanDistance(SqlString vector1, SqlString vector2)
        {
            try
            {
                if (vector1.IsNull || vector2.IsNull)
                    return SqlDouble.Null;

                var v1 = ParseVector(vector1.Value);
                var v2 = ParseVector(vector2.Value);

                if (v1.Length != v2.Length)
                    throw new ArgumentException("Vectors must have the same dimension");

                var sumSquaredDifferences = 0.0;
                for (var i = 0; i < v1.Length; i++)
                {
                    var diff = v1[i] - v2[i];
                    sumSquaredDifferences += diff * diff;
                }

                return new SqlDouble(Math.Sqrt(sumSquaredDifferences));
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error calculating Euclidean distance: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Normalizes a vector to unit length
        /// </summary>
        /// <param name="vector">Vector to normalize</param>
        /// <returns>Normalized vector</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "VectorNormalize")]
        public static SqlString VectorNormalize(SqlString vector)
        {
            try
            {
                if (vector.IsNull)
                    return SqlString.Null;

                var v = ParseVector(vector.Value);
                var magnitude = Math.Sqrt(v.Sum(x => x * x));

                if (magnitude == 0)
                    return vector; // Return original if zero vector

                var normalized = v.Select(x => x / magnitude).ToArray();
                return new SqlString(string.Join(",", normalized.Select(f => f.ToString("G", CultureInfo.InvariantCulture))));
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error normalizing vector: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Mathematical Functions

        /// <summary>
        /// Calculates Shannon entropy of a data array
        /// Useful for model complexity analysis
        /// </summary>
        /// <param name="data">Comma-separated data values</param>
        /// <returns>Shannon entropy</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "CalculateEntropy")]
        public static SqlDouble CalculateEntropy(SqlString data)
        {
            try
            {
                if (data.IsNull || string.IsNullOrEmpty(data.Value))
                    return SqlDouble.Null;

                var values = data.Value.Split(',').Select(v => v.Trim()).ToArray();
                var frequency = new Dictionary<string, int>();

                // Count frequencies
                foreach (var value in values)
                {
                    frequency[value] = frequency.GetValueOrDefault(value, 0) + 1;
                }

                // Calculate entropy
                var entropy = 0.0;
                var total = values.Length;

                foreach (var freq in frequency.Values)
                {
                    var probability = (double)freq / total;
                    if (probability > 0)
                    {
                        entropy -= probability * Math.Log2(probability);
                    }
                }

                return new SqlDouble(entropy);
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error calculating entropy: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Calculates statistical moments (mean, variance, skewness, kurtosis)
        /// </summary>
        /// <param name="values">Comma-separated numeric values</param>
        /// <returns>JSON with statistical moments</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "CalculateStatisticalMoments")]
        public static SqlString CalculateStatisticalMoments(SqlString values)
        {
            try
            {
                if (values.IsNull || string.IsNullOrEmpty(values.Value))
                    return SqlString.Null;

                var data = values.Value
                    .Split(',')
                    .Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture))
                    .ToArray();

                var n = data.Length;
                var mean = data.Average();
                var variance = data.Sum(x => Math.Pow(x - mean, 2)) / n;
                var stdDev = Math.Sqrt(variance);

                // Calculate skewness and kurtosis
                var skewness = 0.0;
                var kurtosis = 0.0;

                if (stdDev > 0)
                {
                    var moment3 = data.Sum(x => Math.Pow((x - mean) / stdDev, 3)) / n;
                    var moment4 = data.Sum(x => Math.Pow((x - mean) / stdDev, 4)) / n;

                    skewness = moment3;
                    kurtosis = moment4 - 3; // Excess kurtosis
                }

                var result = new
                {
                    mean,
                    variance,
                    standardDeviation = stdDev,
                    skewness,
                    kurtosis,
                    count = n
                };

                return new SqlString(JsonSerializer.Serialize(result, JsonOptions));
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error calculating statistical moments: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Performs fast Fourier transform analysis on data
        /// Simplified implementation for frequency analysis
        /// </summary>
        /// <param name="data">Time series data</param>
        /// <returns>Frequency domain characteristics</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "AnalyzeFrequencyDomain")]
        public static SqlString AnalyzeFrequencyDomain(SqlString data)
        {
            try
            {
                if (data.IsNull || string.IsNullOrEmpty(data.Value))
                    return SqlString.Null;

                var values = data.Value
                    .Split(',')
                    .Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture))
                    .ToArray();

                // Simple frequency analysis without full FFT
                var result = new
                {
                    dominantFrequency = FindDominantFrequency(values),
                    spectralCentroid = CalculateSpectralCentroid(values),
                    spectralRolloff = CalculateSpectralRolloff(values),
                    spectralFlux = CalculateSpectralFlux(values)
                };

                return new SqlString(JsonSerializer.Serialize(result, JsonOptions));
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error analyzing frequency domain: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Security and Validation Functions

        /// <summary>
        /// Validates and sanitizes user input for tenant operations
        /// </summary>
        /// <param name="userId">User ID to validate</param>
        /// <param name="operation">Operation being performed</param>
        /// <returns>Validation result</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.Read,
            IsDeterministic = false,
            Name = "ValidateUserOperation")]
        public static SqlBoolean ValidateUserOperation(SqlInt32 userId, SqlString operation)
        {
            try
            {
                if (userId.IsNull || operation.IsNull)
                    return SqlBoolean.False;

                // Validate user exists and is active
                using (var connection = new SqlConnection("context connection=true"))
                {
                    connection.Open();

                    var query = @"
                        SELECT COUNT(*)
                        FROM dbo.Users u
                        INNER JOIN dbo.UserPermissions up ON u.UserId = up.UserId
                        WHERE u.UserId = @UserId
                        AND u.IsActive = 1
                        AND up.Operation = @Operation
                        AND up.IsGranted = 1";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId.Value);
                        command.Parameters.AddWithValue("@Operation", operation.Value);

                        var count = (int)command.ExecuteScalar();
                        return new SqlBoolean(count > 0);
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error validating user operation: {ex.Message}");
                return SqlBoolean.False;
            }
        }

        /// <summary>
        /// Generates secure hash for sensitive data
        /// </summary>
        /// <param name="input">Input to hash</param>
        /// <param name="salt">Salt for hashing</param>
        /// <returns>Secure hash</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "GenerateSecureHash")]
        public static SqlString GenerateSecureHash(SqlString input, SqlString salt)
        {
            try
            {
                if (input.IsNull)
                    return SqlString.Null;

                var saltBytes = string.IsNullOrEmpty(salt.Value) ? new byte[16] : Encoding.UTF8.GetBytes(salt.Value);
                var inputBytes = Encoding.UTF8.GetBytes(input.Value);

                using (var sha256 = SHA256.Create())
                {
                    var combined = new byte[inputBytes.Length + saltBytes.Length];
                    Buffer.BlockCopy(inputBytes, 0, combined, 0, inputBytes.Length);
                    Buffer.BlockCopy(saltBytes, 0, combined, inputBytes.Length, saltBytes.Length);

                    var hash = sha256.ComputeHash(combined);
                    return new SqlString(Convert.ToBase64String(hash));
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error generating secure hash: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Validates file path for security compliance
        /// </summary>
        /// <param name="filePath">File path to validate</param>
        /// <returns>True if path is safe</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "ValidateFilePath")]
        public static SqlBoolean ValidateFilePath(SqlString filePath)
        {
            try
            {
                if (filePath.IsNull || string.IsNullOrEmpty(filePath.Value))
                    return SqlBoolean.False;

                var path = filePath.Value;

                // Check for path traversal attempts
                if (path.Contains("..") || path.Contains("~"))
                    return SqlBoolean.False;

                // Check for invalid characters
                var invalidChars = Path.GetInvalidPathChars();
                if (path.Any(c => invalidChars.Contains(c)))
                    return SqlBoolean.False;

                // Check for reserved names
                var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" };
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (reservedNames.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)))
                    return SqlBoolean.False;

                return SqlBoolean.True;
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error validating file path: {ex.Message}");
                return SqlBoolean.False;
            }
        }

        #endregion

        #region Data Conversion and Formatting

        /// <summary>
        /// Converts JSON to formatted table output
        /// </summary>
        /// <param name="jsonData">JSON data to convert</param>
        /// <returns>Formatted table representation</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "JsonToTable")]
        public static SqlString JsonToTable(SqlString jsonData)
        {
            try
            {
                if (jsonData.IsNull || string.IsNullOrEmpty(jsonData.Value))
                    return SqlString.Null;

                var document = JsonDocument.Parse(jsonData.Value);
                var result = new StringBuilder();

                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    // Handle array of objects
                    var properties = new HashSet<string>();

                    // First pass: collect all property names
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in element.EnumerateObject())
                            {
                                properties.Add(prop.Name);
                            }
                        }
                    }

                    // Create header
                    result.AppendLine(string.Join("\t", properties));

                    // Create data rows
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        var values = new List<string>();
                        foreach (var prop in properties)
                        {
                            if (element.TryGetProperty(prop, out var value))
                            {
                                values.Add(FormatJsonValue(value));
                            }
                            else
                            {
                                values.Add("");
                            }
                        }
                        result.AppendLine(string.Join("\t", values));
                    }
                }
                else if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Handle single object
                    result.AppendLine("Property\tValue");
                    foreach (var prop in document.RootElement.EnumerateObject())
                    {
                        result.AppendLine($"{prop.Name}\t{FormatJsonValue(prop.Value)}");
                    }
                }

                return new SqlString(result.ToString());
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error converting JSON to table: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Formats large numbers with appropriate units (K, M, B, T)
        /// </summary>
        /// <param name="number">Number to format</param>
        /// <returns>Formatted number with units</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "FormatLargeNumber")]
        public static SqlString FormatLargeNumber(SqlDouble number)
        {
            try
            {
                if (number.IsNull)
                    return SqlString.Null;

                var value = Math.Abs(number.Value);
                var sign = number.Value < 0 ? "-" : "";

                if (value >= 1e12)
                    return new SqlString($"{sign}{value / 1e12:F1}T");
                else if (value >= 1e9)
                    return new SqlString($"{sign}{value / 1e9:F1}B");
                else if (value >= 1e6)
                    return new SqlString($"{sign}{value / 1e6:F1}M");
                else if (value >= 1e3)
                    return new SqlString($"{sign}{value / 1e3:F1}K");
                else
                    return new SqlString($"{sign}{value:F0}");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error formatting large number: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Extracts file extension and validates it
        /// </summary>
        /// <param name="fileName">File name to process</param>
        /// <returns>Validated file extension</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = true,
            Name = "ExtractFileExtension")]
        public static SqlString ExtractFileExtension(SqlString fileName)
        {
            try
            {
                if (fileName.IsNull || string.IsNullOrEmpty(fileName.Value))
                    return SqlString.Null;

                var extension = Path.GetExtension(fileName.Value).ToLowerInvariant();

                // Validate against known model file extensions
                var validExtensions = new[] { ".safetensors", ".bin", ".pt", ".pth", ".onnx", ".h5", ".pkl", ".json" };

                if (validExtensions.Contains(extension))
                    return new SqlString(extension);
                else
                    return new SqlString("unknown");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error extracting file extension: {ex.Message}");
                return new SqlString("error");
            }
        }

        #endregion

        #region Performance Monitoring

        /// <summary>
        /// Monitors system performance metrics
        /// </summary>
        /// <returns>JSON with performance metrics</returns>
        [SqlFunction(
            DataAccess = DataAccessKind.None,
            IsDeterministic = false,
            Name = "GetPerformanceMetrics")]
        public static SqlString GetPerformanceMetrics()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();

                var metrics = new
                {
                    timestamp = DateTime.UtcNow,
                    memoryUsageMB = process.WorkingSet64 / 1024.0 / 1024.0,
                    peakMemoryMB = process.PeakWorkingSet64 / 1024.0 / 1024.0,
                    cpuTimeMs = process.TotalProcessorTime.TotalMilliseconds,
                    threadCount = process.Threads.Count,
                    gcCollections = new
                    {
                        gen0 = GC.CollectionCount(0),
                        gen1 = GC.CollectionCount(1),
                        gen2 = GC.CollectionCount(2)
                    },
                    gcMemoryKB = GC.GetTotalMemory(false) / 1024.0
                };

                return new SqlString(JsonSerializer.Serialize(metrics, JsonOptions));
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error getting performance metrics: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Private Helper Methods

        private static double[] ParseVector(string vectorString)
        {
            return vectorString
                .Split(',')
                .Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture))
                .ToArray();
        }

        private static string FormatJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetDouble().ToString("G", CultureInfo.InvariantCulture),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => element.ToString()
            };
        }

        private static double FindDominantFrequency(double[] data)
        {
            // Simplified frequency analysis
            var maxAmplitude = 0.0;
            var dominantFreq = 0.0;

            for (var freq = 1; freq < data.Length / 2; freq++)
            {
                var amplitude = CalculateAmplitudeAtFrequency(data, freq);
                if (amplitude > maxAmplitude)
                {
                    maxAmplitude = amplitude;
                    dominantFreq = freq;
                }
            }

            return dominantFreq;
        }

        private static double CalculateAmplitudeAtFrequency(double[] data, int frequency)
        {
            var real = 0.0;
            var imag = 0.0;

            for (var i = 0; i < data.Length; i++)
            {
                var angle = 2.0 * Math.PI * frequency * i / data.Length;
                real += data[i] * Math.Cos(angle);
                imag += data[i] * Math.Sin(angle);
            }

            return Math.Sqrt(real * real + imag * imag);
        }

        private static double CalculateSpectralCentroid(double[] data)
        {
            var weightedSum = 0.0;
            var magnitudeSum = 0.0;

            for (var i = 1; i < data.Length / 2; i++)
            {
                var magnitude = Math.Abs(data[i]);
                weightedSum += i * magnitude;
                magnitudeSum += magnitude;
            }

            return magnitudeSum > 0 ? weightedSum / magnitudeSum : 0;
        }

        private static double CalculateSpectralRolloff(double[] data)
        {
            var magnitudes = new double[data.Length / 2];
            var totalMagnitude = 0.0;

            for (var i = 0; i < magnitudes.Length; i++)
            {
                magnitudes[i] = Math.Abs(data[i]);
                totalMagnitude += magnitudes[i];
            }

            var threshold = 0.85 * totalMagnitude;
            var runningSum = 0.0;

            for (var i = 0; i < magnitudes.Length; i++)
            {
                runningSum += magnitudes[i];
                if (runningSum >= threshold)
                {
                    return i;
                }
            }

            return magnitudes.Length - 1;
        }

        private static double CalculateSpectralFlux(double[] data)
        {
            if (data.Length < 4) return 0;

            var flux = 0.0;
            var halfLength = data.Length / 2;

            for (var i = 1; i < halfLength - 1; i++)
            {
                var currentMagnitude = Math.Abs(data[i]);
                var previousMagnitude = Math.Abs(data[i - 1]);
                var diff = currentMagnitude - previousMagnitude;

                if (diff > 0)
                {
                    flux += diff;
                }
            }

            return flux / (halfLength - 2);
        }

        #endregion
    }
}