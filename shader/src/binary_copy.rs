/*!
 * PostgreSQL Binary COPY Protocol Writer
 * 
 * Enterprise-grade bulk loader:
 * - Binary format (not text CSV)
 * - Zero serialization overhead
 * - Preserves exact byte representations
 * - Network byte order (big-endian)
 */

use byteorder::{BigEndian, WriteBytesExt};
use std::io::{self, Write};
use thiserror::Error;

#[derive(Error, Debug)]
pub enum CopyError {
    #[error("IO error: {0}")]
    Io(#[from] io::Error),
    
    #[error("Invalid data: {0}")]
    InvalidData(String),
}

pub type CopyResult<T> = Result<T, CopyError>;

/// Binary COPY protocol writer for PostgreSQL
pub struct BinaryCopyWriter<W: Write> {
    writer: W,
    records_written: usize,
}

/// Atom record for COPY stream
pub struct AtomRecord {
    pub atom_id: Vec<u8>,         // bytea (32 bytes BLAKE3)
    pub atom_class: i16,           // smallint (0=constant, 1=composition)
    pub modality: i16,             // smallint (2=neural)
    pub subtype: String,           // varchar
    pub atomic_value: Option<Vec<u8>>,  // bytea (nullable)
    pub geom_wkb: Vec<u8>,         // geometry (PostGIS WKB)
    pub hilbert_index: i64,        // bigint
    pub metadata: String,          // jsonb
}

impl<W: Write> BinaryCopyWriter<W> {
    pub fn new(writer: W) -> Self {
        Self {
            writer,
            records_written: 0,
        }
    }
    
    /// Start binary COPY stream
    pub fn start_copy(&mut self) -> CopyResult<()> {
        // Binary COPY signature: "PGCOPY\n\xff\r\n\0"
        self.writer.write_all(b"PGCOPY\n\xff\r\n\0")?;
        
        // Flags field (32-bit): 0 = no OID
        self.writer.write_u32::<BigEndian>(0)?;
        
        // Header extension length (32-bit): 0 = no extensions
        self.writer.write_u32::<BigEndian>(0)?;
        
        Ok(())
    }
    
    /// Write single atom record
    pub fn write_record(&mut self, record: &AtomRecord) -> CopyResult<()> {
        // Field count (16-bit): 8 columns
        self.writer.write_i16::<BigEndian>(8)?;
        
        // Field 1: atom_id (bytea)
        self.write_bytea(&record.atom_id)?;
        
        // Field 2: atom_class (smallint)
        self.write_i16(record.atom_class)?;
        
        // Field 3: modality (smallint)
        self.write_i16(record.modality)?;
        
        // Field 4: subtype (varchar)
        self.write_text(&record.subtype)?;
        
        // Field 5: atomic_value (bytea, nullable)
        match &record.atomic_value {
            Some(val) => self.write_bytea(val)?,
            None => self.write_null()?,
        }
        
        // Field 6: geom (geometry WKB)
        self.write_bytea(&record.geom_wkb)?;
        
        // Field 7: hilbert_index (bigint)
        self.write_i64(record.hilbert_index)?;
        
        // Field 8: metadata (jsonb)
        self.write_jsonb(&record.metadata)?;
        
        self.records_written += 1;
        Ok(())
    }
    
    /// End binary COPY stream
    pub fn end_copy(&mut self) -> CopyResult<usize> {
        // File trailer: -1 (16-bit)
        self.writer.write_i16::<BigEndian>(-1)?;
        
        self.writer.flush()?;
        Ok(self.records_written)
    }
    
    // Helper methods for field encoding
    
    fn write_null(&mut self) -> CopyResult<()> {
        // NULL = field length -1
        self.writer.write_i32::<BigEndian>(-1)?;
        Ok(())
    }
    
    fn write_i16(&mut self, value: i16) -> CopyResult<()> {
        // Field length: 2 bytes
        self.writer.write_i32::<BigEndian>(2)?;
        // Value
        self.writer.write_i16::<BigEndian>(value)?;
        Ok(())
    }
    
    fn write_i64(&mut self, value: i64) -> CopyResult<()> {
        // Field length: 8 bytes
        self.writer.write_i32::<BigEndian>(8)?;
        // Value
        self.writer.write_i64::<BigEndian>(value)?;
        Ok(())
    }
    
    fn write_text(&mut self, value: &str) -> CopyResult<()> {
        let bytes = value.as_bytes();
        // Field length
        self.writer.write_i32::<BigEndian>(bytes.len() as i32)?;
        // Value
        self.writer.write_all(bytes)?;
        Ok(())
    }
    
    fn write_bytea(&mut self, value: &[u8]) -> CopyResult<()> {
        // Field length
        self.writer.write_i32::<BigEndian>(value.len() as i32)?;
        // Value
        self.writer.write_all(value)?;
        Ok(())
    }
    
    fn write_jsonb(&mut self, json_str: &str) -> CopyResult<()> {
        let bytes = json_str.as_bytes();
        // JSONB format: version byte (1) + data
        let total_len = 1 + bytes.len();
        
        // Field length
        self.writer.write_i32::<BigEndian>(total_len as i32)?;
        
        // JSONB version 1
        self.writer.write_u8(1)?;
        
        // JSON data
        self.writer.write_all(bytes)?;
        
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;
    
    #[test]
    fn test_binary_copy_format() {
        let mut buffer = Vec::new();
        let mut writer = BinaryCopyWriter::new(&mut buffer);
        
        writer.start_copy().unwrap();
        
        let record = AtomRecord {
            atom_id: vec![0u8; 32],
            atom_class: 0,
            modality: 2,
            subtype: "weight".to_string(),
            atomic_value: Some(vec![0x00, 0x00, 0x40, 0x3F]), // 0.75 as f32
            geom_wkb: vec![0u8; 64], // Simplified WKB
            hilbert_index: 12345,
            metadata: r#"{"type":"test"}"#.to_string(),
        };
        
        writer.write_record(&record).unwrap();
        writer.end_copy().unwrap();
        
        // Verify signature
        assert_eq!(&buffer[0..11], b"PGCOPY\n\xff\r\n\0");
        
        // Verify record count
        assert_eq!(writer.records_written, 1);
    }
}
