/// REAL DATABASE INTEGRATION TESTS
///
/// These tests ACTUALLY use the database.
/// They write data to PostgreSQL, clear memory, read it back, and verify.
/// If these pass, the system actually works end-to-end.
///
/// Uses default connection: postgresql://hartonomous:hartonomous@localhost:5433/hartonomous
/// Override with: HARTONOMOUS_DB_URL environment variable

#include <catch2/catch_test_macros.hpp>
#include "db/database_store.hpp"
#include "db/schema_manager.hpp"
#include "db/seeder.hpp"
#include "db/connection.hpp"
#include "atoms/database_encoder.hpp"
#include "atoms/pair_encoding_engine.hpp"
#include "atoms/codepoint_atom_table.hpp"
#include <fstream>
#include <cstdlib>
#include <iomanip>
#include <sstream>

using namespace hartonomous;
using namespace hartonomous::db;

// No longer skip - we have default credentials now
#define REQUIRE_DB() do {} while(0)

// =============================================================================
// TEST FIXTURE: Single setup, isolated tests
// Uses SchemaManager for proper validation - no suppression
// =============================================================================
class DatabaseTestFixture {
protected:
    static bool setup_done_;
    static SchemaStatus schema_status_;

public:
    DatabaseTestFixture() {
        // One-time setup: validate and repair schema, then seed atoms
        if (!setup_done_) {
            SchemaManager mgr;
            schema_status_ = mgr.ensure_schema();
            
            if (schema_status_.has_errors()) {
                throw std::runtime_error("Schema validation failed: " + schema_status_.summary());
            }
            
            Seeder seeder(true);
            seeder.ensure_schema();  // Seeds atoms
            setup_done_ = true;
        }

        // Per-test: clean composition table only (atoms persist)
        DatabaseStore db;
        PgResult res(PQexec(db.connection(), "TRUNCATE composition"));
        db.clear_cache();
    }

    DatabaseStore fresh_db() {
        DatabaseStore db;
        PgResult res(PQexec(db.connection(), "TRUNCATE composition"));
        db.clear_cache();
        return db;
    }
    
    static const SchemaStatus& get_schema_status() { return schema_status_; }
};

bool DatabaseTestFixture::setup_done_ = false;
SchemaStatus DatabaseTestFixture::schema_status_;

// SHA256 for bit-perfect verification (same as bench_moby.cpp)
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
            for (int i = 0; i < 8; i++) len_bytes[i] = static_cast<uint8_t>(bits >> (56 - i*8));
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
    
    std::string sha256(const std::string& data) {
        SHA256 ctx;
        ctx.init();
        ctx.update(reinterpret_cast<const uint8_t*>(data.data()), data.size());
        return ctx.final_hex();
    }
}

// =============================================================================
// DATABASE ROUND-TRIP: The test that actually matters
// Uses DatabaseEncoder which writes DIRECTLY to PostgreSQL
// =============================================================================

TEST_CASE_METHOD(DatabaseTestFixture, "Database round-trip with DatabaseEncoder", "[database][integration]") {
    REQUIRE_DB();
    
    auto db = fresh_db();
    
    SECTION("Simple string: file → DB → file with SHA256 verification") {
        std::string input = "Hello, World! This is a real database round-trip test.";
        std::string orig_hash = sha256(input);
        
        DatabaseEncoder encoder(db);
        NodeRef root = encoder.ingest(input);
        
        db.clear_cache();
        
        auto decoded = db.decode(root);
        std::string result(decoded.begin(), decoded.end());
        std::string decoded_hash = sha256(result);
        
        INFO("Original SHA256: " << orig_hash);
        INFO("Decoded SHA256:  " << decoded_hash);
        REQUIRE(orig_hash == decoded_hash);
    }
    
    SECTION("Repetitive data: 4000 bytes of 'abcd'") {
        std::string input;
        for (int i = 0; i < 1000; ++i) input += "abcd";
        std::string orig_hash = sha256(input);
        
        DatabaseEncoder encoder(db);
        NodeRef root = encoder.ingest(input);
        
        db.clear_cache();
        auto decoded = db.decode(root);
        std::string result(decoded.begin(), decoded.end());
        std::string decoded_hash = sha256(result);
        
        INFO("Original SHA256: " << orig_hash);
        INFO("Decoded SHA256:  " << decoded_hash);
        REQUIRE(orig_hash == decoded_hash);
    }
    
    SECTION("Pseudo-random ASCII text: 10KB") {
        // Use only valid ASCII characters (0x20-0x7E)
        std::vector<std::uint8_t> input(10000);
        std::uint32_t seed = 0xDEADBEEF;
        for (auto& b : input) {
            seed = seed * 1103515245 + 12345;
            b = static_cast<std::uint8_t>(0x20 + ((seed >> 16) % 95));  // printable ASCII
        }
        std::string orig_hash = sha256(input);
        
        DatabaseEncoder encoder(db);
        NodeRef root = encoder.ingest(input.data(), input.size());
        
        db.clear_cache();
        auto decoded = db.decode(root);
        std::string decoded_hash = sha256(decoded);
        
        INFO("Original SHA256: " << orig_hash);
        INFO("Decoded SHA256:  " << decoded_hash);
        REQUIRE(orig_hash == decoded_hash);
    }
}

TEST_CASE_METHOD(DatabaseTestFixture, "Moby Dick: full database round-trip with SHA256", "[database][integration][moby]") {
    REQUIRE_DB();
    
    std::string path = std::string(TEST_DATA_DIR) + "/moby_dick.txt";
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    
    if (!file.good()) {
        SKIP("Test data not available");
    }
    
    auto size = file.tellg();
    REQUIRE(size > 1000000);
    
    file.seekg(0);
    std::vector<std::uint8_t> original(static_cast<std::size_t>(size));
    file.read(reinterpret_cast<char*>(original.data()), size);
    
    std::string orig_hash = sha256(original);
    
    auto db = fresh_db();
    
    auto start_ingest = std::chrono::high_resolution_clock::now();
    DatabaseEncoder encoder(db);
    NodeRef root = encoder.ingest(original.data(), original.size());
    auto end_ingest = std::chrono::high_resolution_clock::now();
    auto ingest_ms = std::chrono::duration_cast<std::chrono::milliseconds>(end_ingest - start_ingest).count();
    
    std::cerr << "\n=== MOBY DICK DATABASE ROUND-TRIP ===\n";
    std::cerr << "Original size:  " << original.size() << " bytes\n";
    std::cerr << "Original SHA:   " << orig_hash << "\n";
    std::cerr << "Compositions:   " << encoder.composition_count() << "\n";
    std::cerr << "Ingest + DB:    " << ingest_ms << " ms\n";
    
    db.clear_cache();
    
    auto start_decode = std::chrono::high_resolution_clock::now();
    auto decoded = db.decode(root);
    auto end_decode = std::chrono::high_resolution_clock::now();
    auto decode_ms = std::chrono::duration_cast<std::chrono::milliseconds>(end_decode - start_decode).count();
    
    std::string decoded_hash = sha256(decoded);
    
    std::cerr << "Decoded size:   " << decoded.size() << " bytes\n";
    std::cerr << "Decoded SHA:    " << decoded_hash << "\n";
    std::cerr << "Decode time:    " << decode_ms << " ms\n";
    std::cerr << "BIT PERFECT:    " << (orig_hash == decoded_hash ? "YES" : "NO - FAIL") << "\n";
    std::cerr << "=====================================\n\n";
    
    REQUIRE(decoded.size() == original.size());
    REQUIRE(orig_hash == decoded_hash);
}

// =============================================================================
// DATABASE PERSISTENCE: Data survives across connections
// =============================================================================

TEST_CASE_METHOD(DatabaseTestFixture, "Data persists across database connections", "[database][persistence]") {
    REQUIRE_DB();
    
    std::string input = "Persistence test data - this should survive reconnection";
    std::string orig_hash = sha256(input);
    NodeRef root;
    
    {
        auto db1 = fresh_db();
        DatabaseEncoder encoder(db1);
        root = encoder.ingest(input);
    }
    // Connection 1 closed, encoder destroyed
    
    {
        DatabaseStore db2;  // Fresh connection, no cache
        auto decoded = db2.decode(root);
        std::string result(decoded.begin(), decoded.end());
        std::string decoded_hash = sha256(result);
        
        INFO("Original SHA256: " << orig_hash);
        INFO("Decoded SHA256:  " << decoded_hash);
        REQUIRE(orig_hash == decoded_hash);
    }
}

// =============================================================================
// DETERMINISM: Same content produces same root across sessions
// =============================================================================

TEST_CASE_METHOD(DatabaseTestFixture, "Same content produces same root ID across sessions", "[database][determinism]") {
    REQUIRE_DB();
    
    std::string input = "Determinism test: same input = same output, always";
    
    NodeRef root1, root2;
    
    {
        auto db1 = fresh_db();
        DatabaseEncoder encoder1(db1);
        root1 = encoder1.ingest(input);
    }
    
    {
        auto db2 = fresh_db();
        DatabaseEncoder encoder2(db2);
        root2 = encoder2.ingest(input);
    }
    
    REQUIRE(root1.id_high == root2.id_high);
    REQUIRE(root1.id_low == root2.id_low);
}

// =============================================================================
// ATOM SEEDING: Database has seeded atoms
// =============================================================================

TEST_CASE_METHOD(DatabaseTestFixture, "Atoms exist in database after seeding", "[database][atoms]") {
    REQUIRE_DB();
    
    DatabaseStore db;
    
    // Fixture already seeded atoms - just verify they exist
    auto count = db.atom_count();
    REQUIRE(count >= 256);
    
    // Verify basic ASCII atoms can be found (codepoints 0-127)
    const auto& atoms = CodepointAtomTable::instance();
    for (int cp = 0; cp < 128; ++cp) {
        NodeRef atom = atoms.ref(cp);
        REQUIRE(db.is_atom_in_db(atom.id_high, atom.id_low));
    }
}
