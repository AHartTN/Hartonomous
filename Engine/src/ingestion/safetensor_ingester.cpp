#include <ingestion/safetensor_ingester.hpp>
#include <fstream>
#include <filesystem>
#include <nlohmann/json.hpp>
#include <endian.h>

namespace Hartonomous {

SafeTensorIngester::SafeTensorIngester(PostgresConnection& db) : db_(db) {}

SafeTensorIngester::ParsedSafeTensor SafeTensorIngester::parse_safetensor(const std::string& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) throw std::runtime_error("Failed to open: " + path);

    uint64_t header_size;
    file.read(reinterpret_cast<char*>(&header_size), 8);
    header_size = le64toh(header_size);

    std::vector<char> header_json(header_size);
    file.read(header_json.data(), header_size);

    auto json = nlohmann::json::parse(header_json.begin(), header_json.end());

    ParsedSafeTensor result;
    result.metadata_json = json.dump();

    size_t current_offset = 0;
    for (auto& [key, value] : json.items()) {
        if (key == "__metadata__") continue;

        TensorInfo info;
        info.name = key;
        info.dtype = static_cast<SafeTensorDType>(value["dtype"].get<int>());
        info.shape = value["shape"].get<std::vector<uint64_t>>();

        std::vector<uint64_t> data_offsets = value["data_offsets"].get<std::vector<uint64_t>>();
        info.data_offset = data_offsets[0];
        info.data_size = data_offsets[1] - data_offsets[0];

        result.tensors[key] = info;

        if (data_offsets[1] > current_offset) {
            current_offset = data_offsets[1];
        }
    }

    result.tensor_data.resize(current_offset);
    file.read(reinterpret_cast<char*>(result.tensor_data.data()), current_offset);

    return result;
}

void SafeTensorIngester::store_content_record(const std::string& path,
                                               const std::vector<uint8_t>& data,
                                               const std::string& mime_type,
                                               SafeTensorStats& stats) {
    auto hash = BLAKE3Pipeline::hash(data.data(), data.size());

    std::ostringstream uuid_str;
    uuid_str << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) uuid_str << '-';
        uuid_str << std::setw(2) << static_cast<int>(hash[i]);
    }

    std::ostringstream sql;
    sql << "INSERT INTO Content (Id, TenantId, UserId, ContentType, ContentHash, "
        << "ContentSize, ContentMimeType, ContentSource) VALUES ("
        << "'" << uuid_str.str() << "',"
        << "'00000000-0000-0000-0000-000000000000',"
        << "'00000000-0000-0000-0000-000000000000',"
        << "E'\\\\x0001',"
        << "E'\\\\x";
    for (size_t i = 0; i < hash.size(); ++i) {
        sql << std::hex << std::setw(2) << std::setfill('0') << static_cast<int>(hash[i]);
    }
    sql << "'," << data.size() << ",'" << mime_type << "','" << path << "') "
        << "ON CONFLICT (Id) DO NOTHING";

    db_.execute(sql.str());
    stats.content_records++;
    stats.total_bytes += data.size();
}

SafeTensorStats SafeTensorIngester::ingest_file(const std::string& path) {
    SafeTensorStats stats;

    auto parsed = parse_safetensor(path);
    stats.num_tensors = parsed.tensors.size();

    PostgresConnection::Transaction txn(db_);

    std::ifstream file(path, std::ios::binary | std::ios::ate);
    size_t file_size = file.tellg();
    file.seekg(0);
    std::vector<uint8_t> file_data(file_size);
    file.read(reinterpret_cast<char*>(file_data.data()), file_size);

    store_content_record(path, file_data, "application/safetensors", stats);

    for (auto& [name, info] : parsed.tensors) {
        std::vector<uint8_t> tensor_data(
            parsed.tensor_data.begin() + info.data_offset,
            parsed.tensor_data.begin() + info.data_offset + info.data_size
        );
        store_content_record(path + "/" + name, tensor_data, "application/octet-stream", stats);
    }

    txn.commit();
    return stats;
}

SafeTensorStats SafeTensorIngester::ingest_directory(const std::string& dir_path) {
    SafeTensorStats total_stats;

    for (const auto& entry : std::filesystem::recursive_directory_iterator(dir_path)) {
        if (entry.is_regular_file() && entry.path().extension() == ".safetensors") {
            auto stats = ingest_file(entry.path().string());
            total_stats.num_tensors += stats.num_tensors;
            total_stats.total_bytes += stats.total_bytes;
            total_stats.content_records += stats.content_records;
        }
    }

    return total_stats;
}

}
