use std::collections::HashMap;
use blake3;

/// Constant Pair Encoding for hierarchy construction
/// 
/// Identifies frequent pairs and creates composition atoms at higher Z levels
#[derive(Debug)]
pub struct CpeBuilder {
    frequency_threshold: u32,
}

/// Composition atom representing a pair or sequence
#[derive(Debug, Clone)]
pub struct Composition {
    pub composition_id: Vec<u8>,
    pub constituent_ids: Vec<Vec<u8>>,
    pub z_level: f64,
    pub frequency: u32,
    pub linestring_coords: Vec<(f64, f64, f64, f64)>, // XYZM coordinates
}

impl CpeBuilder {
    pub fn new(frequency_threshold: u32) -> Self {
        Self { frequency_threshold }
    }
    
    /// Build hierarchy from atom sequence with their geometric positions
    /// 
    /// # Arguments
    /// * `atoms` - Sequence of (atom_id, x, y, z, m) tuples
    /// 
    /// # Returns
    /// Vec of Composition structures with LINESTRING geometry
    pub fn build_compositions(&self, atoms: &[(Vec<u8>, f64, f64, f64, f64)]) -> Vec<Composition> {
        if atoms.len() < 2 {
            return Vec::new();
        }
        
        // Count pair frequencies with positions
        let mut pair_map: HashMap<(Vec<u8>, Vec<u8>), (u32, Vec<((f64, f64, f64, f64), (f64, f64, f64, f64))>)> = HashMap::new();
        
        for window in atoms.windows(2) {
            let (id1, x1, y1, z1, m1) = &window[0];
            let (id2, x2, y2, z2, m2) = &window[1];
            let pair_key = (id1.clone(), id2.clone());
            
            let entry = pair_map.entry(pair_key).or_insert((0, Vec::new()));
            entry.0 += 1;
            entry.1.push(((*x1, *y1, *z1, *m1), (*x2, *y2, *z2, *m2)));
        }
        
        // Filter by threshold and create compositions
        let mut compositions = Vec::new();
        
        for ((first_id, second_id), (count, positions)) in pair_map {
            if count >= self.frequency_threshold {
                // Generate deterministic composition ID
                let mut comp_input = Vec::with_capacity(first_id.len() + second_id.len());
                comp_input.extend_from_slice(&first_id);
                comp_input.extend_from_slice(&second_id);
                
                let hash = blake3::hash(&comp_input);
                let comp_id = hash.as_bytes().to_vec();
                
                // Calculate Z level (compositions start at Z=1)
                let z_level = 1.0;
                
                // Build LINESTRING coordinates from averaged positions
                let avg_positions = Self::average_positions(&positions);
                
                compositions.push(Composition {
                    composition_id: comp_id,
                    constituent_ids: vec![first_id, second_id],
                    z_level,
                    frequency: count,
                    linestring_coords: avg_positions,
                });
            }
        }
        
        compositions.sort_by(|a, b| b.frequency.cmp(&a.frequency));
        compositions
    }
    
    /// Average positions for multiple occurrences of the same pair
    fn average_positions(positions: &[((f64, f64, f64, f64), (f64, f64, f64, f64))]) -> Vec<(f64, f64, f64, f64)> {
        let count = positions.len() as f64;
        
        let mut sum_first = (0.0, 0.0, 0.0, 0.0);
        let mut sum_second = (0.0, 0.0, 0.0, 0.0);
        
        for (first, second) in positions {
            sum_first.0 += first.0;
            sum_first.1 += first.1;
            sum_first.2 += first.2;
            sum_first.3 += first.3;
            
            sum_second.0 += second.0;
            sum_second.1 += second.1;
            sum_second.2 += second.2;
            sum_second.3 += second.3;
        }
        
        vec![
            (sum_first.0 / count, sum_first.1 / count, sum_first.2 / count, sum_first.3 / count),
            (sum_second.0 / count, sum_second.1 / count, sum_second.2 / count, sum_second.3 / count),
        ]
    }
    
    /// Convert composition to WKT LINESTRING for PostGIS
    pub fn to_linestring_wkt(&self, comp: &Composition) -> String {
        let points: Vec<String> = comp.linestring_coords
            .iter()
            .map(|(x, y, z, m)| format!("{} {} {} {}", x, y, z, m))
            .collect();
        
        format!("LINESTRING ZM({})", points.join(", "))
    }
    
    /// Calculate Z level based on composition depth
    pub fn calculate_z_level(&self, depth: u32) -> f64 {
        (depth as f64).min(3.0)
    }
    
    /// Build multi-level hierarchy by iteratively encoding compositions
    pub fn build_recursive_hierarchy(&self, atoms: &[(Vec<u8>, f64, f64, f64, f64)]) -> Vec<Composition> {
        let mut all_compositions = Vec::new();
        let mut current_level = atoms.to_vec();
        let mut z_level = 1.0;
        
        while current_level.len() >= 2 && z_level <= 3.0 {
            let level_comps = self.build_compositions(&current_level);
            
            if level_comps.is_empty() {
                break; // No more frequent patterns
            }
            
            // Add Z-level specific compositions
            for mut comp in level_comps {
                comp.z_level = z_level;
                
                // Create next-level atom from composition
                // Average the LINESTRING coords for the composition's position
                let avg_x = comp.linestring_coords.iter().map(|(x, _, _, _)| x).sum::<f64>() / comp.linestring_coords.len() as f64;
                let avg_y = comp.linestring_coords.iter().map(|(_, y, _, _)| y).sum::<f64>() / comp.linestring_coords.len() as f64;
                let avg_m = comp.frequency as f64; // Higher frequency = higher salience
                
                current_level.push((comp.composition_id.clone(), avg_x, avg_y, z_level, avg_m));
                all_compositions.push(comp);
            }
            
            z_level += 1.0;
        }
        
        all_compositions
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_pair_detection_with_positions() {
        let builder = CpeBuilder::new(2);
        
        let sequence = vec![
            (vec![1], 10.0, 20.0, 0.0, 1.0),
            (vec![2], 15.0, 25.0, 0.0, 1.0),
            (vec![1], 10.0, 20.0, 0.0, 1.0),
            (vec![2], 15.0, 25.0, 0.0, 1.0),
            (vec![1], 10.0, 20.0, 0.0, 1.0),
            (vec![2], 15.0, 25.0, 0.0, 1.0),
        ];
        
        let comps = builder.build_compositions(&sequence);
        
        // Should find (1,2) composition with frequency >= 2
        assert!(!comps.is_empty());
        assert_eq!(comps[0].frequency, 3);
        assert_eq!(comps[0].linestring_coords.len(), 2); // LINESTRING with 2 points
        assert_eq!(comps[0].z_level, 1.0); // First level
    }
    
    #[test]
    fn test_linestring_wkt_generation() {
        let builder = CpeBuilder::new(1);
        
        let comp = Composition {
            composition_id: vec![1, 2, 3],
            constituent_ids: vec![vec![1], vec![2]],
            z_level: 1.0,
            frequency: 5,
            linestring_coords: vec![
                (10.0, 20.0, 1.0, 1.0),
                (15.0, 25.0, 1.0, 1.0),
            ],
        };
        
        let wkt = builder.to_linestring_wkt(&comp);
        assert!(wkt.starts_with("LINESTRING ZM("));
        assert!(wkt.contains("10 20 1 1"));
        assert!(wkt.contains("15 25 1 1"));
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
    
    #[test]
    fn test_recursive_hierarchy() {
        let builder = CpeBuilder::new(2);
        
        let atoms = vec![
            (vec![1], 0.0, 0.0, 0.0, 1.0),
            (vec![2], 1.0, 1.0, 0.0, 1.0),
            (vec![1], 0.0, 0.0, 0.0, 1.0),
            (vec![2], 1.0, 1.0, 0.0, 1.0),
            (vec![3], 2.0, 2.0, 0.0, 1.0),
        ];
        
        let hierarchy = builder.build_recursive_hierarchy(&atoms);
        
        // Should create at least one composition at Z=1
        assert!(!hierarchy.is_empty());
        assert!(hierarchy.iter().any(|c| c.z_level == 1.0));
    }
}

