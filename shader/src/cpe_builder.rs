use std::collections::HashMap;

/// Constant Pair Encoding for hierarchy construction
/// 
/// Identifies frequent pairs and creates composition atoms at higher Z levels
#[derive(Debug)]
pub struct CpeBuilder {
    frequency_threshold: u32,
}

impl CpeBuilder {
    pub fn new(frequency_threshold: u32) -> Self {
        Self { frequency_threshold }
    }
    
    /// Build hierarchy from atom sequence
    /// 
    /// # Arguments
    /// * `atom_ids` - Ordered sequence of atoms
    /// 
    /// # Returns
    /// Vec of (pair_atoms, composition_id, frequency)
    pub fn build_pairs(&self, atom_ids: &[Vec<u8>]) -> Vec<(Vec<u8>, Vec<u8>, Vec<u8>, u32)> {
        if atom_ids.len() < 2 {
            return Vec::new();
        }
        
        // Count pair frequencies
        let mut pair_counts: HashMap<(Vec<u8>, Vec<u8>), u32> = HashMap::new();
        
        for window in atom_ids.windows(2) {
            let pair = (window[0].clone(), window[1].clone());
            *pair_counts.entry(pair).or_insert(0) += 1;
        }
        
        // Filter by threshold and generate composition IDs
        let mut pairs = Vec::new();
        
        for ((first, second), count) in pair_counts {
            if count >= self.frequency_threshold {
                // Generate deterministic composition ID from constituents
                let mut comp_id = Vec::with_capacity(first.len() + second.len());
                comp_id.extend_from_slice(&first);
                comp_id.extend_from_slice(&second);
                
                // Hash to 32 bytes
                let hash = blake3::hash(&comp_id);
                let comp_hash = hash.as_bytes().to_vec();
                
                pairs.push((first, second, comp_hash, count));
            }
        }
        
        pairs.sort_by(|a, b| b.3.cmp(&a.3)); // Sort by frequency descending
        pairs
    }
    
    /// Calculate Z level based on composition depth
    pub fn calculate_z_level(&self, depth: u32) -> f64 {
        (depth as f64).min(3.0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_pair_detection() {
        let builder = CpeBuilder::new(2);
        
        let sequence = vec![
            vec![1], vec![2],
            vec![1], vec![2],
            vec![1], vec![2],
            vec![3], vec![4],
        ];
        
        let pairs = builder.build_pairs(&sequence);
        
        // Should find (1,2) with count >= 2
        assert!(!pairs.is_empty());
        assert_eq!(pairs[0].3, 3); // Appears 3 times
    }
    
    #[test]
    fn test_z_level_calculation() {
        let builder = CpeBuilder::new(1);
        
        assert_eq!(builder.calculate_z_level(0), 0.0);
        assert_eq!(builder.calculate_z_level(1), 1.0);
        assert_eq!(builder.calculate_z_level(2), 2.0);
        assert_eq!(builder.calculate_z_level(3), 3.0);
        assert_eq!(builder.calculate_z_level(10), 3.0); // Capped at 3
    }
}
