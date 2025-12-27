#pragma once

#include <string>
#include <cstdint>
#include <cstring>

namespace hartonomous::db {

/// Efficient builder for PostgreSQL COPY text format.
/// Avoids ostringstream overhead for high-throughput bulk loading.
///
/// COPY format uses:
/// - TAB (\t) as field separator
/// - NEWLINE (\n) as row separator  
/// - \N for NULL values
/// - No quoting for numeric types
class CopyBuilder {
    std::string buffer_;
    bool first_field_ = true;
    
public:
    /// Create builder with optional capacity hint
    explicit CopyBuilder(std::size_t reserve = 0) {
        if (reserve > 0) buffer_.reserve(reserve);
    }
    
    /// Reserve capacity for approximately n rows of avg_row_size bytes
    void reserve(std::size_t n_rows, std::size_t avg_row_size = 50) {
        buffer_.reserve(n_rows * avg_row_size);
    }
    
    /// Get the built buffer
    [[nodiscard]] const std::string& str() const noexcept { return buffer_; }
    [[nodiscard]] std::string&& release() noexcept { return std::move(buffer_); }
    [[nodiscard]] const char* data() const noexcept { return buffer_.data(); }
    [[nodiscard]] std::size_t size() const noexcept { return buffer_.size(); }
    
    /// Clear for reuse
    void clear() {
        buffer_.clear();
        first_field_ = true;
    }
    
    /// Add a 64-bit signed integer field
    CopyBuilder& field(std::int64_t value) {
        separator();
        // Fast integer to string
        char buf[24];
        char* end = buf + sizeof(buf);
        char* ptr = end;
        bool negative = value < 0;
        std::uint64_t abs_val = negative ? -static_cast<std::uint64_t>(value) 
                                         : static_cast<std::uint64_t>(value);
        do {
            *--ptr = '0' + (abs_val % 10);
            abs_val /= 10;
        } while (abs_val > 0);
        if (negative) *--ptr = '-';
        buffer_.append(ptr, end - ptr);
        return *this;
    }
    
    /// Add a 32-bit signed integer field
    CopyBuilder& field(std::int32_t value) {
        return field(static_cast<std::int64_t>(value));
    }
    
    /// Add a 16-bit signed integer field
    CopyBuilder& field(std::int16_t value) {
        return field(static_cast<std::int64_t>(value));
    }
    
    /// Add an unsigned integer field
    CopyBuilder& field(std::uint64_t value) {
        separator();
        char buf[24];
        char* end = buf + sizeof(buf);
        char* ptr = end;
        do {
            *--ptr = '0' + (value % 10);
            value /= 10;
        } while (value > 0);
        buffer_.append(ptr, end - ptr);
        return *this;
    }
    
    /// Add a double field (6 decimal places)
    CopyBuilder& field(double value, int precision = 6) {
        separator();
        char buf[32];
        int len = std::snprintf(buf, sizeof(buf), "%.*f", precision, value);
        buffer_.append(buf, len);
        return *this;
    }
    
    /// Add a string field (no escaping - caller must ensure clean data)
    CopyBuilder& field(const char* value) {
        separator();
        buffer_.append(value);
        return *this;
    }
    
    CopyBuilder& field(const std::string& value) {
        separator();
        buffer_.append(value);
        return *this;
    }
    
    /// Add a NULL field
    CopyBuilder& null() {
        separator();
        buffer_.append("\\N", 2);
        return *this;
    }
    
    /// Add a PostGIS POINTZM as WKT
    CopyBuilder& point_zm(double x, double y, double z, double m) {
        separator();
        char buf[128];
        int len = std::snprintf(buf, sizeof(buf), 
            "SRID=0;POINTZM(%.6f %.6f %.6f %.6f)", x, y, z, m);
        buffer_.append(buf, len);
        return *this;
    }
    
    /// Add a PostGIS POINTZM with integer coordinates (faster for semantic coords)
    CopyBuilder& point_zm_int(int x, int y, int z, int m) {
        separator();
        char buf[64];
        int len = std::snprintf(buf, sizeof(buf), 
            "SRID=0;POINTZM(%d %d %d %d)", x, y, z, m);
        buffer_.append(buf, len);
        return *this;
    }
    
    /// End current row
    CopyBuilder& end_row() {
        buffer_.push_back('\n');
        first_field_ = true;
        return *this;
    }
    
    /// Convenience: add all fields and end row in one call
    template<typename... Args>
    CopyBuilder& row(Args&&... args) {
        (field(std::forward<Args>(args)), ...);
        return end_row();
    }
    
private:
    void separator() {
        if (!first_field_) {
            buffer_.push_back('\t');
        }
        first_field_ = false;
    }
};

} // namespace hartonomous::db
