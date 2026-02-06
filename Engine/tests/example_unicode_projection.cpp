/**
 * @file example_unicode_projection.cpp
 * @brief Example program demonstrating Unicode → 4D projection pipeline
 *
 * This program shows how to:
 * 1. Take a Unicode string
 * 2. Project each codepoint to 4D hypersphere
 * 3. Generate Hilbert curve indices
 * 4. Visualize the results
 */

#include <unicode/codepoint_projection.hpp>
#include <geometry/hopf_fibration.hpp>
#include <iostream>
#include <iomanip>
#include <string>
#include <vector>
#include <sstream>

using namespace hartonomous::unicode;
using namespace hartonomous::geometry;

// Helper: Display HilbertIndex as hex
static std::string hilbert_to_hex(const std::array<uint8_t, 16>& idx) {
    std::ostringstream ss;
    for (int i = 0; i < 16; ++i) {
        ss << std::hex << std::setw(2) << std::setfill('0') << static_cast<int>(idx[i]);
    }
    return ss.str();
}
using Vec3 = HopfFibration::Vec3;

void print_separator() {
    std::cout << std::string(80, '=') << "\n";
}

void print_hash(const std::array<uint8_t, 16>& hash) {
    std::cout << "0x";
    for (size_t i = 0; i < std::min(size_t(8), hash.size()); ++i) {
        std::cout << std::hex << std::setw(2) << std::setfill('0')
                  << (int)hash[i];
    }
    std::cout << std::dec << "..." << std::setfill(' ');
}

void project_and_display(const std::u32string& text) {
    print_separator();
    std::cout << "Projecting: \"";

    // Convert to UTF-8 for display (simplified)
    for (char32_t cp : text) {
        if (cp < 128) {
            std::cout << (char)cp;
        } else {
            std::cout << "U+" << std::hex << (uint32_t)cp << std::dec;
        }
    }
    std::cout << "\"\n";
    print_separator();

    std::cout << "\n";
    std::cout << std::left;
    std::cout << std::setw(10) << "Char"
              << std::setw(20) << "Hash (first 8 bytes)"
              << std::setw(45) << "4D Position (x, y, z, w)"
              << std::setw(30) << "Hilbert Index (hi lo)"
              << "\n";
    std::cout << std::string(105, '-') << "\n";

    for (size_t i = 0; i < text.size(); ++i) {
        char32_t cp = text[i];
        auto result = CodepointProjection::project(cp);

        // Display character
        std::cout << std::setw(10);
        if (cp < 128 && cp >= 32) {
            std::cout << "'" << (char)cp << "'";
        } else if (cp == U' ') {
            std::cout << "'<space>'";
        } else {
            std::cout << "U+" << std::hex << (uint32_t)cp << std::dec;
        }

        // Display hash (first 8 bytes)
        std::cout << std::setw(20);
        std::cout << "  ";
        print_hash(result.hash);

        // Display 4D position
        std::cout << "  " << std::setw(45);
        std::cout << "(" << std::fixed << std::setprecision(4)
                  << result.s3_position[0] << ", "
                  << result.s3_position[1] << ", "
                  << result.s3_position[2] << ", "
                  << result.s3_position[3] << ")";

        // Display Hilbert index
        std::cout << "  " << hilbert_to_hex(result.hilbert_index);

        std::cout << "\n";
    }

    std::cout << "\n";
}

void demonstrate_hopf_projection(const std::u32string& text) {
    print_separator();
    std::cout << "Hopf Fibration: 4D → 3D Visualization\n";
    print_separator();

    std::cout << "\n";
    std::cout << std::left;
    std::cout << std::setw(10) << "Char"
              << std::setw(45) << "4D Position (S³)"
              << std::setw(35) << "3D Projection (S²)"
              << "\n";
    std::cout << std::string(90, '-') << "\n";

    for (size_t i = 0; i < text.size(); ++i) {
        char32_t cp = text[i];
        auto result = CodepointProjection::project(cp);

        // Project to 3D via Hopf fibration
        Vec3 s2_position = HopfFibration::forward(result.s3_position);

        // Display character
        std::cout << std::setw(10);
        if (cp < 128 && cp >= 32) {
            std::cout << "'" << (char)cp << "'";
        } else if (cp == U' ') {
            std::cout << "'<space>'";
        } else {
            std::cout << "U+" << std::hex << (uint32_t)cp << std::dec;
        }

        // Display 4D position
        std::cout << "  " << std::setw(45);
        std::cout << "(" << std::fixed << std::setprecision(4)
                  << result.s3_position[0] << ", "
                  << result.s3_position[1] << ", "
                  << result.s3_position[2] << ", "
                  << result.s3_position[3] << ")";

        // Display 3D projection
        std::cout << "  →  (" << std::fixed << std::setprecision(4)
                  << s2_position[0] << ", "
                  << s2_position[1] << ", "
                  << s2_position[2] << ")";

        std::cout << "\n";
    }

    std::cout << "\n";
}

void analyze_hilbert_ordering(const std::u32string& text) {
    print_separator();
    std::cout << "Hilbert Curve Ordering Analysis\n";
    print_separator();

    std::vector<std::pair<char32_t, CodepointProjection::HilbertCurve::HilbertIndex>> char_indices;

    for (char32_t cp : text) {
        auto result = CodepointProjection::project(cp);
        char_indices.push_back({cp, result.hilbert_index});
    }

    // Sort by Hilbert index
    std::sort(char_indices.begin(), char_indices.end(),
              [](const auto& a, const auto& b) { return a.second < b.second; });

    std::cout << "\nCharacters sorted by Hilbert index (spatial ordering):\n\n";
    std::cout << std::setw(35) << "Hilbert Index (hex)" << "  Character\n";
    std::cout << std::string(50, '-') << "\n";

    for (const auto& [cp, index] : char_indices) {
        std::cout << hilbert_to_hex(index) << "  ";
        if (cp < 128 && cp >= 32) {
            std::cout << "'" << (char)cp << "'";
        } else if (cp == U' ') {
            std::cout << "'<space>'";
        } else {
            std::cout << "U+" << std::hex << (uint32_t)cp << std::dec;
        }
        std::cout << "\n";
    }

    std::cout << "\n";
}

int main() {
    std::cout << "\n";
    print_separator();
    std::cout << "Hartonomous Unicode → 4D Projection Example\n";
    print_separator();
    std::cout << "\n";

    // Example 1: "Call me Ishmael" (the classic test)
    std::u32string example1 = U"Call me Ishmael";
    project_and_display(example1);
    demonstrate_hopf_projection(example1);
    analyze_hilbert_ordering(example1);

    // Example 2: Mixed Unicode (English + Chinese)
    std::u32string example2 = U"Hello 世界";
    project_and_display(example2);

    // Example 3: Emoji
    std::u32string example3 = U"😀🎉🚀";
    project_and_display(example3);

    // Summary
    print_separator();
    std::cout << "Pipeline Summary\n";
    print_separator();
    std::cout << "\n";
    std::cout << "1. Unicode Codepoint\n";
    std::cout << "   ↓\n";
    std::cout << "2. BLAKE3 Hash (16 bytes, content-addressable)\n";
    std::cout << "   ↓\n";
    std::cout << "3. Super Fibonacci → 4D Position on S³\n";
    std::cout << "   ↓\n";
    std::cout << "4. Hilbert Curve → Spatial Index (ONE-WAY)\n";
    std::cout << "   ↓\n";
    std::cout << "5. Database Storage (PostgreSQL + PostGIS)\n";
    std::cout << "\n";
    std::cout << "Benefits:\n";
    std::cout << "  • Content-addressable: Same character = Same hash = Stored once\n";
    std::cout << "  • Spatial indexing: O(log N) queries via B-tree/GiST\n";
    std::cout << "  • Visualization: Hopf fibration for 3D rendering\n";
    std::cout << "  • Deduplication: Global across all content\n";
    std::cout << "\n";
    print_separator();

    return 0;
}
