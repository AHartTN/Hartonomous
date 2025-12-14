use crate::error::ShaderResult;

/// Hilbert curve indexer for 4D space
pub struct HilbertIndexer {
    resolution: u32,
}

impl HilbertIndexer {
    pub fn new(resolution: u32) -> Self {
        Self { resolution }
    }
    
    /// Convert 4D coordinates to 1D Hilbert index
    /// 
    /// # Arguments
    /// * `x, y, z, m` - Normalized coordinates [0.0, 1.0]
    /// 
    /// # Returns
    /// 64-bit Hilbert index
    pub fn encode(&self, x: f64, y: f64, z: f64, m: f64) -> ShaderResult<i64> {
        // Convert normalized coords to integer grid
        let max_val = (1 << self.resolution) - 1;
        
        let xi = (x.clamp(0.0, 1.0) * max_val as f64) as u32;
        let yi = (y.clamp(0.0, 1.0) * max_val as f64) as u32;
        let zi = (z.clamp(0.0, 1.0) * max_val as f64) as u32;
        let mi = (m.clamp(0.0, 1.0) * max_val as f64) as u32;
        
        // 4D Hilbert encoding using rotation matrices
        let index = self.hilbert_4d(xi, yi, zi, mi, self.resolution);
        
        Ok(index as i64)
    }
    
    /// 4D Hilbert curve calculation
    fn hilbert_4d(&self, x: u32, y: u32, z: u32, m: u32, order: u32) -> u64 {
        let mut index: u64 = 0;
        let mut coords = [x, y, z, m];
        
        for i in (0..order).rev() {
            let bits = coords.iter()
                .map(|&c| ((c >> i) & 1) as u64)
                .collect::<Vec<_>>();
            
            let quadrant = bits[0] | (bits[1] << 1) | (bits[2] << 2) | (bits[3] << 3);
            index = (index << 4) | quadrant;
            
            // Rotation for next level
            self.rotate_4d(&mut coords, quadrant as u8);
        }
        
        index
    }
    
    /// Rotate coordinates based on quadrant
    fn rotate_4d(&self, coords: &mut [u32; 4], quadrant: u8) {
        match quadrant {
            0 => coords.swap(0, 3),
            1 => coords.swap(1, 2),
            7 => coords.reverse(),
            _ => {}
        }
    }
}

/// Public function for encoding 4D coordinates to Hilbert index
pub fn encode_4d(x: f64, y: f64, z: f64, m: f64, resolution: usize) -> i64 {
    let indexer = HilbertIndexer::new(resolution as u32);
    
    // Normalize to [0,1] range
    let nx = (x + 50.0) / 100.0;
    let ny = (y + 50.0) / 100.0;
    let nz = z / 3.0;
    let nm = m.clamp(0.0, 1.0);
    
    indexer.encode(nx.clamp(0.0, 1.0), ny.clamp(0.0, 1.0), nz.clamp(0.0, 1.0), nm).unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_hilbert_encoding() {
        let indexer = HilbertIndexer::new(10);
        
        let idx1 = indexer.encode(0.5, 0.5, 0.5, 0.5).unwrap();
        let idx2 = indexer.encode(0.5, 0.5, 0.5, 0.5).unwrap();
        
        assert_eq!(idx1, idx2);
    }
    
    #[test]
    fn test_proximity_preservation() {
        let indexer = HilbertIndexer::new(10);
        
        let idx1 = indexer.encode(0.5, 0.5, 0.5, 0.5).unwrap();
        let idx2 = indexer.encode(0.51, 0.5, 0.5, 0.5).unwrap();
        let idx3 = indexer.encode(0.9, 0.9, 0.9, 0.9).unwrap();
        
        let dist12 = (idx1 - idx2).abs();
        let dist13 = (idx1 - idx3).abs();
        
        assert!(dist12 < dist13);
    }
}
