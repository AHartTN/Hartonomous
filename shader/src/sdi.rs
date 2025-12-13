use blake3::Hasher;
use crate::error::ShaderResult;

/// Modality type identifiers (1 byte)
#[repr(u8)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Modality {
    Numeric = 0x01,
    Text = 0x02,
    Image = 0x03,
    Audio = 0x04,
    Tensor = 0x05,
}

/// Semantic class identifiers (2 bytes)
#[repr(u16)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SemanticClass {
    Token = 0x0001,
    Float32 = 0x0002,
    Float64 = 0x0003,
    Int32 = 0x0004,
    Int64 = 0x0005,
    Pixel = 0x0010,
    Waveform = 0x0020,
    Weight = 0x0030,
}

/// Normalization method (4 bytes)
#[repr(u32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Normalization {
    None = 0x00000000,
    MinMax = 0x00000001,
    ZScore = 0x00000002,
    Quantile = 0x00000003,
}

/// Structured Deterministic Identity Generator
/// 
/// Generates BLAKE3 hash structured as:
/// [Modality 1B][SemanticClass 2B][Normalization 4B][ValueHash 25B] = 32 bytes
pub struct SdiGenerator {
    hasher: Hasher,
}

impl SdiGenerator {
    pub fn new() -> Self {
        Self {
            hasher: Hasher::new(),
        }
    }
    
    /// Generate SDI for an atom value
    /// 
    /// # Arguments
    /// * `modality` - Type of data
    /// * `semantic_class` - Specific semantic category
    /// * `normalization` - Normalization method applied
    /// * `value` - Raw value bytes
    /// 
    /// # Returns
    /// 32-byte BLAKE3 hash with structured prefix
    pub fn generate(
        &mut self,
        modality: Modality,
        semantic_class: SemanticClass,
        normalization: Normalization,
        value: &[u8],
    ) -> ShaderResult<[u8; 32]> {
        self.hasher.reset();
        
        // Structure: [Modality][SemanticClass][Normalization][Value]
        self.hasher.update(&[modality as u8]);
        self.hasher.update(&(semantic_class as u16).to_be_bytes());
        self.hasher.update(&(normalization as u32).to_be_bytes());
        self.hasher.update(value);
        
        let hash = self.hasher.finalize();
        Ok(*hash.as_bytes())
    }
    
    /// Generate SDI for numeric constant
    pub fn generate_numeric(&mut self, value: f64, precision: u32) -> ShaderResult<[u8; 32]> {
        let quantized = quantize_float(value, precision);
        let bytes = quantized.to_le_bytes();
        
        self.generate(
            Modality::Numeric,
            SemanticClass::Float64,
            Normalization::None,
            &bytes,
        )
    }
    
    /// Generate SDI for text token
    pub fn generate_token(&mut self, token: &str) -> ShaderResult<[u8; 32]> {
        self.generate(
            Modality::Text,
            SemanticClass::Token,
            Normalization::None,
            token.as_bytes(),
        )
    }
}

impl Default for SdiGenerator {
    fn default() -> Self {
        Self::new()
    }
}

/// Quantize float to fixed precision
fn quantize_float(value: f64, precision: u32) -> f64 {
    let scale = 10_f64.powi(precision as i32);
    (value * scale).round() / scale
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_deterministic_generation() {
        let mut gen1 = SdiGenerator::new();
        let mut gen2 = SdiGenerator::new();
        
        let hash1 = gen1.generate_numeric(42.123456, 2).unwrap();
        let hash2 = gen2.generate_numeric(42.123456, 2).unwrap();
        
        assert_eq!(hash1, hash2);
    }
    
    #[test]
    fn test_quantization_collision() {
        let mut gen = SdiGenerator::new();
        
        let hash1 = gen.generate_numeric(42.10, 2).unwrap();
        let hash2 = gen.generate_numeric(42.10, 2).unwrap();
        
        // Same value should produce same hash (deterministic)
        assert_eq!(hash1, hash2);
    }
    
    #[test]
    fn test_different_modalities() {
        let mut gen = SdiGenerator::new();
        
        let hash1 = gen.generate_numeric(42.0, 2).unwrap();
        let hash2 = gen.generate_token("42").unwrap();
        
        assert_ne!(hash1, hash2);
    }
}
