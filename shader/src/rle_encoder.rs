

/// Run-Length Encoding for temporal sequences
#[derive(Debug, Clone)]
pub struct RleEncoder;

impl RleEncoder {
    pub fn new() -> Self {
        Self
    }
    
    /// Encode sequence with run lengths as M dimension
    /// 
    /// # Arguments
    /// * `atom_ids` - Sequence of atom identifiers
    /// 
    /// # Returns
    /// Vec of (atom_id, run_length) tuples
    pub fn encode(&self, atom_ids: &[Vec<u8>]) -> Vec<(Vec<u8>, u32)> {
        if atom_ids.is_empty() {
            return Vec::new();
        }
        
        let mut encoded = Vec::new();
        let mut current = atom_ids[0].clone();
        let mut count = 1u32;
        
        for atom_id in &atom_ids[1..] {
            if atom_id == &current {
                count += 1;
            } else {
                encoded.push((current.clone(), count));
                current = atom_id.clone();
                count = 1;
            }
        }
        
        encoded.push((current, count));
        encoded
    }
    
    /// Calculate salience (M dimension) from run length
    /// 
    /// Higher frequency = higher salience
    pub fn run_length_to_salience(&self, run_length: u32, max_run: u32) -> f64 {
        (run_length as f64 / max_run as f64).min(1.0)
    }
}

impl Default for RleEncoder {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_rle_basic() {
        let encoder = RleEncoder::new();
        
        let sequence = vec![
            vec![1, 2, 3],
            vec![1, 2, 3],
            vec![1, 2, 3],
            vec![4, 5, 6],
            vec![4, 5, 6],
            vec![7, 8, 9],
        ];
        
        let encoded = encoder.encode(&sequence);
        
        assert_eq!(encoded.len(), 3);
        assert_eq!(encoded[0].1, 3); // First atom repeats 3 times
        assert_eq!(encoded[1].1, 2); // Second atom repeats 2 times
        assert_eq!(encoded[2].1, 1); // Third atom appears once
    }
    
    #[test]
    fn test_salience_calculation() {
        let encoder = RleEncoder::new();
        
        let low_salience = encoder.run_length_to_salience(1, 100);
        let high_salience = encoder.run_length_to_salience(100, 100);
        
        assert!(low_salience < 0.1);
        assert_eq!(high_salience, 1.0);
    }
}
