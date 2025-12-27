/// Full database benchmark: seed atoms, ingest Moby Dick, write to DB, decode from DB

#include "atoms/database_encoder.hpp"
#include "atoms/byte_atom_table.hpp"
#include "db/database_store.hpp"
#include "db/seeder.hpp"
#include <iostream>
#include <fstream>
#include <chrono>
#include <cstdlib>
#include <iomanip>
#include <sstream>

// Simple SHA256 for verification (public domain implementation)
namespace {
    struct SHA256 {
        uint32_t state[8];
        uint64_t count;
        uint8_t buffer[64];
        
        void init() {
            state[0] = 0x6a09e667; state[1] = 0xbb67ae85;
            state[2] = 0x3c6ef372; state[3] = 0xa54ff53a;
            state[4] = 0x510e527f; state[5] = 0x9b05688c;
            state[6] = 0x1f83d9ab; state[7] = 0x5be0cd19;
            count = 0;
        }
        
        static uint32_t rotr(uint32_t x, int n) { return (x >> n) | (x << (32 - n)); }
        static uint32_t ch(uint32_t x, uint32_t y, uint32_t z) { return (x & y) ^ (~x & z); }
        static uint32_t maj(uint32_t x, uint32_t y, uint32_t z) { return (x & y) ^ (x & z) ^ (y & z); }
        static uint32_t sig0(uint32_t x) { return rotr(x, 2) ^ rotr(x, 13) ^ rotr(x, 22); }
        static uint32_t sig1(uint32_t x) { return rotr(x, 6) ^ rotr(x, 11) ^ rotr(x, 25); }
        static uint32_t gam0(uint32_t x) { return rotr(x, 7) ^ rotr(x, 18) ^ (x >> 3); }
        static uint32_t gam1(uint32_t x) { return rotr(x, 17) ^ rotr(x, 19) ^ (x >> 10); }
        
        void transform(const uint8_t* data) {
            static const uint32_t K[64] = {
                0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
                0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
                0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
                0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
                0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
                0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
                0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
                0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
            };
            uint32_t W[64], a, b, c, d, e, f, g, h, t1, t2;
            for (int i = 0; i < 16; i++)
                W[i] = (data[i*4] << 24) | (data[i*4+1] << 16) | (data[i*4+2] << 8) | data[i*4+3];
            for (int i = 16; i < 64; i++)
                W[i] = gam1(W[i-2]) + W[i-7] + gam0(W[i-15]) + W[i-16];
            a = state[0]; b = state[1]; c = state[2]; d = state[3];
            e = state[4]; f = state[5]; g = state[6]; h = state[7];
            for (int i = 0; i < 64; i++) {
                t1 = h + sig1(e) + ch(e,f,g) + K[i] + W[i];
                t2 = sig0(a) + maj(a,b,c);
                h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            state[0] += a; state[1] += b; state[2] += c; state[3] += d;
            state[4] += e; state[5] += f; state[6] += g; state[7] += h;
        }
        
        void update(const uint8_t* data, size_t len) {
            size_t idx = count % 64;
            count += len;
            if (idx) {
                size_t fill = 64 - idx;
                if (len < fill) { memcpy(buffer + idx, data, len); return; }
                memcpy(buffer + idx, data, fill);
                transform(buffer);
                data += fill; len -= fill;
            }
            while (len >= 64) { transform(data); data += 64; len -= 64; }
            if (len) memcpy(buffer, data, len);
        }
        
        std::string final_hex() {
            uint8_t pad[64] = {0x80};
            size_t idx = count % 64;
            size_t padlen = (idx < 56) ? (56 - idx) : (120 - idx);
            uint64_t bits = count * 8;
            uint8_t len_bytes[8];
            for (int i = 0; i < 8; i++) len_bytes[i] = bits >> (56 - i*8);
            update(pad, padlen);
            update(len_bytes, 8);
            std::ostringstream ss;
            ss << std::hex << std::setfill('0');
            for (int i = 0; i < 8; i++) ss << std::setw(8) << state[i];
            return ss.str();
        }
    };
    
    std::string sha256(const std::vector<uint8_t>& data) {
        SHA256 ctx;
        ctx.init();
        ctx.update(data.data(), data.size());
        return ctx.final_hex();
    }
}

using namespace hartonomous;
using namespace hartonomous::db;

int main() {
    // Check for database connection
    if (!std::getenv("HARTONOMOUS_DB_URL")) {
        std::cerr << "ERROR: HARTONOMOUS_DB_URL not set\n";
        std::cerr << "Example: set HARTONOMOUS_DB_URL=postgresql://hartonomous:hartonomous@localhost:5433/hartonomous\n";
        return 1;
    }

    const char* path = TEST_DATA_DIR "/moby_dick.txt";
    
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file.good()) {
        std::cerr << "Cannot open " << path << "\n";
        return 1;
    }
    
    auto size = file.tellg();
    file.seekg(0);
    std::vector<std::uint8_t> data(static_cast<std::size_t>(size));
    file.read(reinterpret_cast<char*>(data.data()), size);
    
    std::cout << "=== MOBY DICK FULL DATABASE BENCHMARK ===\n";
    std::cout << "Input size: " << data.size() << " bytes\n\n";
    
    auto total_start = std::chrono::high_resolution_clock::now();

    // Phase 1: Database setup + seed atoms
    auto seed_start = std::chrono::high_resolution_clock::now();
    
    Seeder seeder(true);  // quiet
    seeder.ensure_schema();
    seeder.seed_byte_atoms();  // Fast: only 256 atoms for byte data
    
    DatabaseStore db;
    db.clear_all();
    
    auto seed_end = std::chrono::high_resolution_clock::now();
    auto seed_ms = std::chrono::duration_cast<std::chrono::milliseconds>(seed_end - seed_start).count();
    std::cout << "SEED ATOMS:    " << seed_ms << " ms\n";

    // Phase 2: Ingest + write to database
    auto ingest_start = std::chrono::high_resolution_clock::now();
    
    DatabaseEncoder encoder(db);
    NodeRef root = encoder.ingest_timed(data.data(), data.size());
    
    auto ingest_end = std::chrono::high_resolution_clock::now();
    auto ingest_ms = std::chrono::duration_cast<std::chrono::milliseconds>(ingest_end - ingest_start).count();
    
    std::cout << "INGEST + DB:   " << ingest_ms << " ms\n";
    std::cout << "    Encode:    " << encoder.last_encode_ms_ << " ms\n";
    std::cout << "    Merge:     " << encoder.last_merge_ms_ << " ms\n";
    std::cout << "    Tree:      " << encoder.last_tree_ms_ << " ms\n";
    std::cout << "    String:    " << encoder.last_string_build_ms_ << " ms\n";
    std::cout << "    DB COPY:   " << encoder.last_copy_ms_ << " ms\n";
    std::cout << "  Compositions: " << encoder.composition_count() << "\n";
    
    double ratio = static_cast<double>(data.size()) / static_cast<double>(encoder.composition_count());
    std::cout << "  Compression:  " << ratio << " bytes/composition\n";

    // Phase 3: Clear cache and decode from database only
    auto decode_start = std::chrono::high_resolution_clock::now();
    
    db.clear_cache();  // Force DB reads
    auto decoded = db.decode(root);
    
    auto decode_end = std::chrono::high_resolution_clock::now();
    auto decode_ms = std::chrono::duration_cast<std::chrono::milliseconds>(decode_end - decode_start).count();
    
    std::cout << "DB DECODE:     " << decode_ms << " ms\n";
    std::cout << "  Size:         " << decoded.size() << " bytes\n";
    
    // Compute SHA256 hashes for bit-perfect verification
    std::string orig_hash = sha256(data);
    std::string decoded_hash = sha256(decoded);
    
    std::cout << "  Original SHA: " << orig_hash << "\n";
    std::cout << "  Decoded SHA:  " << decoded_hash << "\n";
    
    bool match = (orig_hash == decoded_hash);
    std::cout << "  BIT PERFECT:  " << (match ? "YES" : "NO - MISMATCH") << "\n";

    // Write decoded output to file for manual inspection if desired
    const char* out_path = TEST_DATA_DIR "/moby_dick_decoded.txt";
    std::ofstream out_file(out_path, std::ios::binary);
    out_file.write(reinterpret_cast<const char*>(decoded.data()), decoded.size());
    out_file.close();
    std::cout << "  Written to:   " << out_path << "\n";

    auto total_end = std::chrono::high_resolution_clock::now();
    auto total_ms = std::chrono::duration_cast<std::chrono::milliseconds>(total_end - total_start).count();
    
    std::cout << "\nTOTAL TIME:    " << total_ms << " ms\n";
    std::cout << "==========================================\n";
    
    return (decoded == data) ? 0 : 1;
}
