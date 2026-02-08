#pragma once

#include <string>
#include <vector>
#include <cstdint>

namespace Hartonomous {

/**
 * @brief Thread-safe UTF-8 to UTF-32 conversion.
 * 
 * Optimized for high-throughput ingestion.
 */
inline std::u32string utf8_to_utf32(const std::string& s) {
    std::u32string out;
    out.reserve(s.size());
    for (size_t i = 0; i < s.size(); ) {
        uint8_t c = static_cast<uint8_t>(s[i]);
        char32_t cp = 0;
        size_t len = 0;
        
        if (c < 0x80) { cp = c; len = 1; }
        else if ((c >> 5) == 0x6) { cp = c & 0x1F; len = 2; }
        else if ((c >> 4) == 0xE) { cp = c & 0x0F; len = 3; }
        else if ((c >> 3) == 0x1E) { cp = c & 0x07; len = 4; }
        else { ++i; continue; } // Invalid start byte
        
        for (size_t j = 1; j < len && i + j < s.size(); ++j) {
            uint8_t cc = static_cast<uint8_t>(s[i + j]);
            if ((cc >> 6) != 0x2) { len = 1; break; } // Unexpected byte
            cp = (cp << 6) | (cc & 0x3F);
        }
        
        out.push_back(cp);
        i += len;
    }
    return out;
}

/**
 * @brief Thread-safe UTF-32 to UTF-8 conversion.
 */
inline std::string utf32_to_utf8(const std::u32string& s) {
    std::string out;
    out.reserve(s.size() * 3 / 2);
    for (char32_t cp : s) {
        if (cp < 0x80) {
            out.push_back(static_cast<char>(cp));
        } else if (cp < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }
    return out;
}

} // namespace Hartonomous
