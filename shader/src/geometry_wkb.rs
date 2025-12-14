/*!
 * PostGIS Well-Known Binary (WKB) Geometry Generator
 * 
 * Generate 4D geometries (XYZM) in WKB format:
 * - POINT ZM for constant atoms
 * - LINESTRING ZM for composition atoms
 * - Little-endian IEEE 754 binary encoding
 * - SRID 4326 (Cartesian, not geographic)
 */

use byteorder::{LittleEndian, WriteBytesExt};
use std::io::{self, Write};

/// 4D Point geometry (XYZM)
#[derive(Debug, Clone, Copy)]
pub struct Point4D {
    pub x: f64,
    pub y: f64,
    pub z: f64,
    pub m: f64,
}

/// 4D LineString geometry (XYZM)
#[derive(Debug, Clone)]
pub struct LineString4D {
    pub x_start: f64,
    pub y_start: f64,
    pub z_start: f64,
    pub m_start: f64,
    pub x_end: f64,
    pub y_end: f64,
    pub z_end: f64,
    pub m_end: f64,
}

impl Point4D {
    /// Create POINT ZM from weight value and magnitude
    pub fn from_value(value: f32, magnitude: f32) -> Self {
        // Polar coordinate mapping in XY plane
        let angle = if magnitude > 0.0 {
            (value / magnitude).atan()
        } else {
            0.0
        };
        
        let x = (magnitude as f64) * angle.cos() * 50.0;
        let y = (magnitude as f64) * angle.sin() * 50.0;
        let z = 0.0; // Constants at Z=0
        let m = magnitude as f64;
        
        Point4D { x, y, z, m }
    }
    
    /// Generate WKB (Well-Known Binary) representation
    pub fn to_wkb(&self) -> Vec<u8> {
        let mut wkb = Vec::new();
        
        // Byte order: 1 = little-endian
        wkb.write_u8(1).unwrap();
        
        // Geometry type: 3001 = POINT ZM (wkbPointZM)
        wkb.write_u32::<LittleEndian>(3001).unwrap();
        
        // Coordinates (8 bytes each, f64)
        wkb.write_f64::<LittleEndian>(self.x).unwrap();
        wkb.write_f64::<LittleEndian>(self.y).unwrap();
        wkb.write_f64::<LittleEndian>(self.z).unwrap();
        wkb.write_f64::<LittleEndian>(self.m).unwrap();
        
        wkb
    }
}

impl LineString4D {
    /// Create LINESTRING ZM from composition (relationship trajectory)
    pub fn from_composition(
        row: usize,
        col: usize,
        rows: usize,
        cols: usize,
        layer_idx: usize,
        value: f32,
        magnitude: f32,
    ) -> Self {
        // Start point: normalized position in transformation matrix
        let x_start = (row as f64 / rows as f64) * 100.0 - 50.0;
        let y_start = (col as f64 / cols as f64) * 100.0 - 50.0;
        let z_start = 0.5 + (layer_idx as f64 / 12.0) * 0.5; // Layer depth
        let m_start = magnitude as f64;
        
        // End point: weight value position (semantic endpoint)
        let angle = if magnitude > 0.0 {
            (value / magnitude).atan()
        } else {
            0.0
        };
        
        let x_end = (magnitude as f64) * angle.cos() * 50.0;
        let y_end = (magnitude as f64) * angle.sin() * 50.0;
        let z_end = 0.0; // Points to constant atom
        let m_end = magnitude as f64;
        
        LineString4D {
            x_start,
            y_start,
            z_start,
            m_start,
            x_end,
            y_end,
            z_end,
            m_end,
        }
    }
    
    /// Generate WKB (Well-Known Binary) representation
    pub fn to_wkb(&self) -> Vec<u8> {
        let mut wkb = Vec::new();
        
        // Byte order: 1 = little-endian
        wkb.write_u8(1).unwrap();
        
        // Geometry type: 3002 = LINESTRING ZM (wkbLineStringZM)
        wkb.write_u32::<LittleEndian>(3002).unwrap();
        
        // Number of points: 2
        wkb.write_u32::<LittleEndian>(2).unwrap();
        
        // Start point (X, Y, Z, M)
        wkb.write_f64::<LittleEndian>(self.x_start).unwrap();
        wkb.write_f64::<LittleEndian>(self.y_start).unwrap();
        wkb.write_f64::<LittleEndian>(self.z_start).unwrap();
        wkb.write_f64::<LittleEndian>(self.m_start).unwrap();
        
        // End point (X, Y, Z, M)
        wkb.write_f64::<LittleEndian>(self.x_end).unwrap();
        wkb.write_f64::<LittleEndian>(self.y_end).unwrap();
        wkb.write_f64::<LittleEndian>(self.z_end).unwrap();
        wkb.write_f64::<LittleEndian>(self.m_end).unwrap();
        
        wkb
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_point_wkb_structure() {
        let point = Point4D {
            x: 10.5,
            y: 20.5,
            z: 0.0,
            m: 0.75,
        };
        
        let wkb = point.to_wkb();
        
        // Verify header
        assert_eq!(wkb[0], 1); // Little-endian
        
        // Verify geometry type (bytes 1-4, little-endian)
        let geom_type = u32::from_le_bytes([wkb[1], wkb[2], wkb[3], wkb[4]]);
        assert_eq!(geom_type, 3001); // POINT ZM
        
        // Total size: 1 (byte order) + 4 (type) + 32 (4 × f64) = 37 bytes
        assert_eq!(wkb.len(), 37);
    }
    
    #[test]
    fn test_linestring_wkb_structure() {
        let line = LineString4D {
            x_start: 0.0,
            y_start: 0.0,
            z_start: 0.5,
            m_start: 0.75,
            x_end: 10.0,
            y_end: 10.0,
            z_end: 0.0,
            m_end: 0.75,
        };
        
        let wkb = line.to_wkb();
        
        // Verify header
        assert_eq!(wkb[0], 1); // Little-endian
        
        // Verify geometry type
        let geom_type = u32::from_le_bytes([wkb[1], wkb[2], wkb[3], wkb[4]]);
        assert_eq!(geom_type, 3002); // LINESTRING ZM
        
        // Verify number of points
        let num_points = u32::from_le_bytes([wkb[5], wkb[6], wkb[7], wkb[8]]);
        assert_eq!(num_points, 2);
        
        // Total size: 1 (byte order) + 4 (type) + 4 (num points) + 64 (2 points × 4 coords × f64) = 73 bytes
        assert_eq!(wkb.len(), 73);
    }
    
    #[test]
    fn test_from_value_determinism() {
        let p1 = Point4D::from_value(0.75, 0.75);
        let p2 = Point4D::from_value(0.75, 0.75);
        
        assert_eq!(p1.x, p2.x);
        assert_eq!(p1.y, p2.y);
        assert_eq!(p1.z, p2.z);
        assert_eq!(p1.m, p2.m);
        
        let wkb1 = p1.to_wkb();
        let wkb2 = p2.to_wkb();
        
        assert_eq!(wkb1, wkb2);
    }
}
