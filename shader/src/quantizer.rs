

/// Numeric quantization engine
pub struct NumericQuantizer {
    precision: u32,
}

impl NumericQuantizer {
    pub fn new(precision: u32) -> Self {
        Self { precision }
    }
    
    /// Quantize float to fixed precision
    pub fn quantize(&self, value: f64) -> f64 {
        let scale = 10_f64.powi(self.precision as i32);
        (value * scale).round() / scale
    }
    
    /// Quantize with error tracking
    pub fn quantize_with_error(&self, value: f64) -> (f64, f64) {
        let quantized = self.quantize(value);
        let error = (value - quantized).abs();
        (quantized, error)
    }
}

/// Text tokenizer (simple whitespace-based for MVP)
pub struct TextTokenizer;

impl TextTokenizer {
    pub fn new() -> Self {
        Self
    }
    
    /// Tokenize text into words
    pub fn tokenize(&self, text: &str) -> Vec<String> {
        text.split_whitespace()
            .map(|s| s.to_lowercase())
            .collect()
    }
}

impl Default for TextTokenizer {
    fn default() -> Self {
        Self::new()
    }
}

/// Image pixel quantizer
pub struct ImageQuantizer {
    palette_size: usize,
}

impl ImageQuantizer {
    pub fn new(palette_size: usize) -> Self {
        Self { palette_size }
    }
    
    /// Quantize RGB to palette index
    pub fn quantize_pixel(&self, r: u8, g: u8, b: u8) -> u8 {
        // Simple uniform quantization
        let levels = (self.palette_size as f64).powf(1.0 / 3.0) as u8;
        let step = 256u16 / levels as u16;
        
        let r_q = ((r as u16 / step) * step) as u8;
        let g_q = ((g as u16 / step) * step) as u8;
        let b_q = ((b as u16 / step) * step) as u8;
        
        ((r_q as u32 + g_q as u32 + b_q as u32) % 256) as u8
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_numeric_quantization() {
        let quantizer = NumericQuantizer::new(2);
        
        assert_eq!(quantizer.quantize(3.14159), 3.14);
        assert_eq!(quantizer.quantize(2.71828), 2.72);
    }
    
    #[test]
    fn test_quantization_deduplication() {
        let quantizer = NumericQuantizer::new(1);
        
        // Test values that round to same quantized value
        let q1 = quantizer.quantize(1.13);
        let q2 = quantizer.quantize(1.14);
        let q3 = quantizer.quantize(1.11);
        
        // All should round to 1.1
        assert_eq!(q1, 1.1);
        assert_eq!(q2, 1.1);
        assert_eq!(q3, 1.1);
    }
    
    #[test]
    fn test_text_tokenization() {
        let tokenizer = TextTokenizer::new();
        
        let tokens = tokenizer.tokenize("Hello World Test");
        assert_eq!(tokens, vec!["hello", "world", "test"]);
    }
}
