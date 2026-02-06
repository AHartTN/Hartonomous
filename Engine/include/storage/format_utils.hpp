#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <cstdint>
#include <string>

namespace Hartonomous {

// Shared formatting utilities for storage layer.
// Eliminates duplicate hash_to_uuid() implementations across stores.

inline constexpr char k_hex_lut[] = "0123456789abcdef";

// Format a BLAKE3 hash as a UUID string (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
inline std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    char buf[37];
    char* p = buf;
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) *p++ = '-';
        *p++ = k_hex_lut[(hash[i] >> 4) & 0xF];
        *p++ = k_hex_lut[hash[i] & 0xF];
    }
    *p = '\0';
    return std::string(buf, 36);
}

// Format a BLAKE3 hash as a bytea hex string with \x prefix for PostgreSQL text COPY
inline std::string hash_to_bytea_hex(const BLAKE3Pipeline::Hash& hash) {
    char buf[2 + 64 + 1]; // \x + 32 bytes * 2 hex chars + null
    buf[0] = '\\';
    buf[1] = 'x';
    char* p = buf + 2;
    for (size_t i = 0; i < hash.size(); ++i) {
        *p++ = k_hex_lut[(hash[i] >> 4) & 0xF];
        *p++ = k_hex_lut[hash[i] & 0xF];
    }
    *p = '\0';
    return std::string(buf, static_cast<size_t>(p - buf));
}

// Format a uint16 as a bytea hex string with \x prefix for PostgreSQL text COPY
inline std::string uint16_to_bytea_hex(uint16_t val) {
    char buf[7]; // \x + 4 hex chars + null
    buf[0] = '\\';
    buf[1] = 'x';
    buf[2] = k_hex_lut[(val >> 12) & 0xF];
    buf[3] = k_hex_lut[(val >> 8) & 0xF];
    buf[4] = k_hex_lut[(val >> 4) & 0xF];
    buf[5] = k_hex_lut[val & 0xF];
    buf[6] = '\0';
    return std::string(buf, 6);
}

// Format a uint32 as a bytea hex string with \x prefix for PostgreSQL text COPY
inline std::string uint32_to_bytea_hex(uint32_t val) {
    char buf[11]; // \x + 8 hex chars + null
    buf[0] = '\\';
    buf[1] = 'x';
    for (int i = 0; i < 4; ++i) {
        uint8_t byte = static_cast<uint8_t>((val >> (24 - i * 8)) & 0xFF);
        buf[2 + i * 2]     = k_hex_lut[(byte >> 4) & 0xF];
        buf[2 + i * 2 + 1] = k_hex_lut[byte & 0xF];
    }
    buf[10] = '\0';
    return std::string(buf, 10);
}

// Format a uint64 as a bytea hex string with \x prefix for PostgreSQL text COPY
inline std::string uint64_to_bytea_hex(uint64_t val) {
    char buf[19]; // \x + 16 hex chars + null
    buf[0] = '\\';
    buf[1] = 'x';
    for (int i = 0; i < 8; ++i) {
        uint8_t byte = static_cast<uint8_t>((val >> (56 - i * 8)) & 0xFF);
        buf[2 + i * 2]     = k_hex_lut[(byte >> 4) & 0xF];
        buf[2 + i * 2 + 1] = k_hex_lut[byte & 0xF];
    }
    buf[18] = '\0';
    return std::string(buf, 18);
}

} // namespace Hartonomous
