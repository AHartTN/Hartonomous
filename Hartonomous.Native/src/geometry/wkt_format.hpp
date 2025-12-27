#pragma once

#include <string>
#include <cstdio>

namespace hartonomous {

/// WKT (Well-Known Text) formatting utilities for PostGIS geometries.
/// Centralizes WKT generation to avoid duplication across geometry types.
struct WktFormat {
    //==========================================================================
    // POINT ZM
    //==========================================================================
    
    /// Format POINT ZM with SRID for PostGIS (integer coordinates)
    static std::string point_zm(int x, int y, int z, int m, int srid = 0) {
        char buf[80];
        std::snprintf(buf, sizeof(buf), 
            "SRID=%d;POINT ZM (%d %d %d %d)", srid, x, y, z, m);
        return buf;
    }
    
    /// Format POINT ZM with SRID for PostGIS (double coordinates)
    static std::string point_zm(double x, double y, double z, double m, int srid = 0) {
        char buf[128];
        std::snprintf(buf, sizeof(buf), 
            "SRID=%d;POINT ZM (%.6f %.6f %.6f %.6f)", srid, x, y, z, m);
        return buf;
    }
    
    /// Format POINT ZM without SRID (pure WKT)
    static std::string point_zm_pure(double x, double y, double z, double m) {
        char buf[100];
        std::snprintf(buf, sizeof(buf), 
            "POINT ZM (%.6f %.6f %.6f %.6f)", x, y, z, m);
        return buf;
    }
    
    /// Write POINT ZM to buffer, return bytes written
    static int point_zm_to_buffer(char* buf, std::size_t size,
                                  double x, double y, double z, double m, 
                                  int srid = 0) {
        return std::snprintf(buf, size, 
            "SRID=%d;POINT ZM (%.6f %.6f %.6f %.6f)", srid, x, y, z, m);
    }
    
    //==========================================================================
    // POINT (2D)
    //==========================================================================
    
    static std::string point(double x, double y, int srid = 0) {
        char buf[80];
        std::snprintf(buf, sizeof(buf), 
            "SRID=%d;POINT (%.6f %.6f)", srid, x, y);
        return buf;
    }
    
    //==========================================================================
    // LINESTRING ZM  
    //==========================================================================
    
    /// Format LINESTRING ZM header (caller adds coordinates)
    static std::string linestring_zm_header(int srid = 0) {
        char buf[32];
        std::snprintf(buf, sizeof(buf), "SRID=%d;LINESTRING ZM (", srid);
        return buf;
    }
    
    /// Format a single coordinate for LINESTRING (with leading comma if not first)
    static void append_coord_zm(std::string& out, double x, double y, double z, double m, 
                                bool first) {
        char buf[64];
        if (first) {
            std::snprintf(buf, sizeof(buf), "%.6f %.6f %.6f %.6f", x, y, z, m);
        } else {
            std::snprintf(buf, sizeof(buf), ", %.6f %.6f %.6f %.6f", x, y, z, m);
        }
        out.append(buf);
    }
    
    //==========================================================================
    // EWKT Parsing helpers
    //==========================================================================
    
    /// Parse SRID from EWKT string (returns 0 if not found)
    static int parse_srid(const std::string& ewkt) {
        if (ewkt.compare(0, 5, "SRID=") != 0) return 0;
        auto semi = ewkt.find(';');
        if (semi == std::string::npos) return 0;
        return std::stoi(ewkt.substr(5, semi - 5));
    }
    
    /// Extract geometry type from WKT/EWKT
    static std::string geometry_type(const std::string& wkt) {
        auto start = wkt.find(';');
        if (start == std::string::npos) start = 0;
        else start++;
        
        // Skip whitespace
        while (start < wkt.size() && wkt[start] == ' ') start++;
        
        auto end = wkt.find('(', start);
        if (end == std::string::npos) return "";
        
        // Trim trailing space
        while (end > start && wkt[end-1] == ' ') end--;
        
        return wkt.substr(start, end - start);
    }
};

} // namespace hartonomous
