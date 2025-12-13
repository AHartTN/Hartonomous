use postgres::{Client, NoTls};
use std::io::Write;
use crate::error::ShaderResult;

/// Atom for bulk loading
#[derive(Debug, Clone)]
pub struct Atom {
    pub atom_id: Vec<u8>,
    pub atom_class: i16,
    pub modality: i16,
    pub subtype: Option<String>,
    pub atomic_value: Option<Vec<u8>>,
    pub x: f64,
    pub y: f64,
    pub z: f64,
    pub m: f64,
    pub hilbert_index: i64,
    pub metadata: Option<serde_json::Value>,
}

/// COPY protocol bulk loader
pub struct CopyLoader {
    client: Client,
    batch_size: usize,
}

impl CopyLoader {
    pub fn new(connection_string: &str, batch_size: usize) -> ShaderResult<Self> {
        let client = Client::connect(connection_string, NoTls)?;
        
        Ok(Self {
            client,
            batch_size,
        })
    }
    
    /// Bulk load atoms using COPY protocol
    pub fn load_atoms(&mut self, atoms: &[Atom]) -> ShaderResult<u64> {
        let mut total_inserted = 0u64;
        
        for chunk in atoms.chunks(self.batch_size) {
            total_inserted += self.load_batch(chunk)?;
        }
        
        Ok(total_inserted)
    }
    
    fn load_batch(&mut self, atoms: &[Atom]) -> ShaderResult<u64> {
        // Use text-based COPY for simplicity and cross-platform compatibility
        let mut rows = Vec::new();
        
        for atom in atoms {
            let atom_id_hex = hex::encode(&atom.atom_id);
            let subtype = atom.subtype.as_ref().map(|s| s.as_str()).unwrap_or("\\N");
            let value_hex = atom.atomic_value.as_ref()
                .map(|v| format!("\\\\x{}", hex::encode(v)))
                .unwrap_or_else(|| "\\N".to_string());
            let metadata = atom.metadata.as_ref()
                .map(|m| m.to_string().replace('\n', " "))
                .unwrap_or_else(|| "\\N".to_string());
            
            let row = format!(
                "\\\\x{}\t{}\t{}\t{}\t{}\tSRID=4326;POINT ZM({} {} {} {})\t{}\t{}",
                atom_id_hex,
                atom.atom_class,
                atom.modality,
                subtype,
                value_hex,
                atom.x, atom.y, atom.z, atom.m,
                atom.hilbert_index,
                metadata
            );
            rows.push(row);
        }
        
        let copy_data = rows.join("\n");
        
        let copy_stmt = "COPY atom (
            atom_id, atom_class, modality, subtype, atomic_value, 
            geom, hilbert_index, metadata
        ) FROM STDIN";
        
        let mut writer = self.client.copy_in(copy_stmt)?;
        writer.write_all(copy_data.as_bytes())?;
        
        let count = writer.finish()?;
        Ok(count)
    }
}

/// Create WKB for POINTZM geometry
fn create_pointzm_wkb(x: f64, y: f64, z: f64, m: f64) -> Vec<u8> {
    let mut wkb = Vec::with_capacity(61);
    
    // Byte order (little endian)
    wkb.push(0x01);
    
    // Geometry type (POINTZM = 3001 with SRID flag)
    let geom_type: u32 = 0x20000000 | 0x80000000 | 0x40000000 | 1; // SRID | Z | M | POINT
    wkb.extend_from_slice(&geom_type.to_le_bytes());
    
    // SRID (4326)
    wkb.extend_from_slice(&4326u32.to_le_bytes());
    
    // Coordinates
    wkb.extend_from_slice(&x.to_le_bytes());
    wkb.extend_from_slice(&y.to_le_bytes());
    wkb.extend_from_slice(&z.to_le_bytes());
    wkb.extend_from_slice(&m.to_le_bytes());
    
    wkb
}

/// Serialize row for COPY binary format
fn serialize_row(row: &[Option<&[u8]>]) -> Vec<u8> {
    let mut data = Vec::new();
    
    // Field count
    data.extend_from_slice(&(row.len() as i16).to_be_bytes());
    
    for field in row {
        match field {
            Some(bytes) => {
                // Length + data
                data.extend_from_slice(&(bytes.len() as i32).to_be_bytes());
                data.extend_from_slice(bytes);
            }
            None => {
                // NULL marker (-1)
                data.extend_from_slice(&(-1i32).to_be_bytes());
            }
        }
    }
    
    data
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_wkb_generation() {
        let wkb = create_pointzm_wkb(0.5, 0.5, 0.0, 1.0);
        // WKB format: 1 byte (endian) + 4 (type) + 4 (SRID) + 8*4 (XYZM) = 41 bytes
        assert_eq!(wkb.len(), 41);
        assert_eq!(wkb[0], 0x01); // Little endian
    }
}
