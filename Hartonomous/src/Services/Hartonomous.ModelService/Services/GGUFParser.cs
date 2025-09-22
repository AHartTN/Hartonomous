using System.Text;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// GGUF file format parser
/// Handles binary parsing of GGUF model files
/// </summary>
public class GGUFParser
{
    public async Task<ModelStructure> ParseAsync(string filePath)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fileStream);

        // Validate GGUF magic number
        var magic = reader.ReadBytes(4);
        if (Encoding.ASCII.GetString(magic) != "GGUF")
        {
            throw new InvalidDataException("Invalid GGUF file format");
        }

        var version = reader.ReadUInt32();
        var tensorCount = reader.ReadUInt64();
        var metadataCount = reader.ReadUInt64();

        var structure = new ModelStructure
        {
            Version = version,
            TensorCount = tensorCount,
            MetadataCount = metadataCount,
            Metadata = await ReadMetadataAsync(reader, metadataCount),
            Tensors = await ReadTensorsAsync(reader, tensorCount)
        };

        return structure;
    }

    private async Task<Dictionary<string, object>> ReadMetadataAsync(BinaryReader reader, ulong count)
    {
        var metadata = new Dictionary<string, object>();

        for (ulong i = 0; i < count; i++)
        {
            var key = ReadString(reader);
            var valueType = (GGUFValueType)reader.ReadUInt32();
            var value = ReadValue(reader, valueType);
            metadata[key] = value;
        }

        return metadata;
    }

    private async Task<List<TensorInfo>> ReadTensorsAsync(BinaryReader reader, ulong count)
    {
        var tensors = new List<TensorInfo>();

        for (ulong i = 0; i < count; i++)
        {
            var name = ReadString(reader);
            var dimensionCount = reader.ReadUInt32();
            var dimensions = new long[dimensionCount];

            for (uint j = 0; j < dimensionCount; j++)
            {
                dimensions[j] = reader.ReadInt64();
            }

            var type = (GGUFDataType)reader.ReadUInt32();
            var offset = reader.ReadUInt64();

            tensors.Add(new TensorInfo
            {
                Name = name,
                Dimensions = dimensions,
                Type = type,
                Offset = offset
            });
        }

        return tensors;
    }

    private string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        var bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    private object ReadValue(BinaryReader reader, GGUFValueType type)
    {
        return type switch
        {
            GGUFValueType.UINT8 => reader.ReadByte(),
            GGUFValueType.INT8 => reader.ReadSByte(),
            GGUFValueType.UINT16 => reader.ReadUInt16(),
            GGUFValueType.INT16 => reader.ReadInt16(),
            GGUFValueType.UINT32 => reader.ReadUInt32(),
            GGUFValueType.INT32 => reader.ReadInt32(),
            GGUFValueType.UINT64 => reader.ReadUInt64(),
            GGUFValueType.INT64 => reader.ReadInt64(),
            GGUFValueType.FLOAT32 => reader.ReadSingle(),
            GGUFValueType.FLOAT64 => reader.ReadDouble(),
            GGUFValueType.BOOL => reader.ReadBoolean(),
            GGUFValueType.STRING => ReadString(reader),
            _ => throw new NotSupportedException($"Unsupported GGUF value type: {type}")
        };
    }
}

public class ModelStructure
{
    public uint Version { get; set; }
    public ulong TensorCount { get; set; }
    public ulong MetadataCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<TensorInfo> Tensors { get; set; } = new();
}

public class TensorInfo
{
    public string Name { get; set; } = string.Empty;
    public long[] Dimensions { get; set; } = Array.Empty<long>();
    public GGUFDataType Type { get; set; }
    public ulong Offset { get; set; }
}

public enum GGUFValueType : uint
{
    UINT8 = 0, INT8 = 1, UINT16 = 2, INT16 = 3,
    UINT32 = 4, INT32 = 5, UINT64 = 6, INT64 = 7,
    FLOAT32 = 8, FLOAT64 = 9, BOOL = 10, STRING = 11
}

public enum GGUFDataType : uint
{
    F32 = 0, F16 = 1, Q4_0 = 2, Q4_1 = 3, Q5_0 = 6, Q5_1 = 7,
    Q8_0 = 8, Q8_1 = 9, Q2_K = 10, Q3_K = 11, Q4_K = 12, Q5_K = 13, Q6_K = 14, Q8_K = 15
}