#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <string>
#include <vector>
#include <unordered_map>
#include <cstdint>

namespace Hartonomous {

enum class SafeTensorDType : uint8_t {
    F32 = 0, F16 = 1, BF16 = 2,
    I64 = 3, I32 = 4, I16 = 5, I8 = 6,
    U64 = 7, U32 = 8, U16 = 9, U8 = 10,
    BOOL = 11, F64 = 12
};

struct TensorInfo {
    std::string name;
    SafeTensorDType dtype;
    std::vector<uint64_t> shape;
    size_t data_offset;
    size_t data_size;
};

struct SafeTensorStats {
    size_t num_tensors = 0;
    size_t total_bytes = 0;
    size_t content_records = 0;
};

class SafeTensorIngester {
public:
    explicit SafeTensorIngester(PostgresConnection& db);
    SafeTensorStats ingest_file(const std::string& path);
    SafeTensorStats ingest_directory(const std::string& dir_path);

private:
    struct ParsedSafeTensor {
        std::unordered_map<std::string, TensorInfo> tensors;
        std::vector<uint8_t> tensor_data;
        std::string metadata_json;
    };

    ParsedSafeTensor parse_safetensor(const std::string& path);
    void store_content_record(const std::string& path, const std::vector<uint8_t>& data,
                              const std::string& mime_type, SafeTensorStats& stats);

    PostgresConnection& db_;
};

}
