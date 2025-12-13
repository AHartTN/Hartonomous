# Shader Implementation Specification

**Component:** External Preprocessing Pipeline
**Language:** Rust (recommended) or C++
**Purpose:** Transform raw data into structured atoms before database insertion

---

## Architecture Overview

```
Raw Data Streams
       ↓
┌──────────────────┐
│  Quantization    │ Map continuous → finite Constants
└────────┬─────────┘
         ↓
┌──────────────────┐
│  SDI Generation  │ BLAKE3 deterministic hashing
└────────┬─────────┘
         ↓
┌──────────────────┐
│  4D Projection   │ LMDS or deterministic placement
└────────┬─────────┘
         ↓
┌──────────────────┐
│  Hilbert Index   │ 4D → 1D space-filling curve
└────────┬─────────┘
         ↓
┌──────────────────┐
│  RLE Encoding    │ Time → M dimension
└────────┬─────────┘
         ↓
┌──────────────────┐
│  CPE Building    │ Hierarchy construction
└────────┬─────────┘
         ↓
┌──────────────────┐
│  COPY Protocol   │ Bulk load to PostgreSQL
└──────────────────┘
```

---

## Module 1: Quantization Engine

### 1.1 Numeric Quantization

```rust
pub struct NumericQuantizer {
    precision: u32,  // Decimal places
}

impl NumericQuantizer {
    pub fn quantize_float(&self, value: f64) -> f64 {
        let scale = 10_f64.powi(self.precision as i32);
        (value * scale).round() / scale
    }

    pub fn quantize_to_constant(&self, value: f64) -> QuantizedConstant {
        let quantized = self.quantize_float(value);

        QuantizedConstant {
            modality: Modality::Numeric,
            subtype: "Float64".to_string(),
            value: quantized.to_le_bytes().to_vec(),
            metadata: QuantizationMetadata {
                precision: self.precision,
                original_value: value,
                error: (value - quantized).abs(),
            },
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_quantization_deduplication() {
        let quantizer = NumericQuantizer { precision: 2 };

        let q1 = quantizer.quantize_float(0.751239);
        let q2 = quantizer.quantize_float(0.749999);
        let q3 = quantizer.quantize_float(0.754321);

        assert_eq!(q1, 0.75);
        assert_eq!(q2, 0.75);
        assert_eq!(q3, 0.75);

        // All three map to same constant atom
    }
}
```

### 1.2 Text Tokenization

```rust
use tokenizers::Tokenizer;

pub struct TextQuantizer {
    tokenizer: Tokenizer,
    vocab_size: usize,
}

impl TextQuantizer {
    pub fn from_pretrained(model: &str) -> Result<Self> {
        // Load BPE tokenizer (e.g., cl100k_base for GPT compatibility)
        let tokenizer = Tokenizer::from_pretrained(model)?;
        let vocab_size = tokenizer.get_vocab_size(true);

        Ok(Self {
            tokenizer,
            vocab_size,
        })
    }

    pub fn encode(&self, text: &str) -> Vec<TokenConstant> {
        let encoding = self.tokenizer.encode(text, false).unwrap();

        encoding.get_ids()
            .iter()
            .map(|&token_id| {
                TokenConstant {
                    modality: Modality::Text,
                    subtype: "BPE".to_string(),
                    token_id,
                    token_str: self.tokenizer.id_to_token(token_id).unwrap(),
                }
            })
            .collect()
    }
}
```

### 1.3 Image Quantization

```rust
use image::{DynamicImage, GenericImageView};

pub struct ImageQuantizer {
    palette: Vec<(u8, u8, u8)>,  // Color palette
}

impl ImageQuantizer {
    pub fn new_with_palette_size(palette_size: usize) -> Self {
        // Use k-means clustering on representative image set
        // to generate palette
        let palette = Self::generate_palette(palette_size);

        Self { palette }
    }

    pub fn quantize_pixel(&self, rgb: (u8, u8, u8)) -> usize {
        // Find nearest palette color (Euclidean distance in RGB space)
        self.palette
            .iter()
            .enumerate()
            .min_by_key(|(_, &palette_color)| {
                Self::color_distance(rgb, palette_color)
            })
            .map(|(index, _)| index)
            .unwrap()
    }

    fn color_distance(c1: (u8, u8, u8), c2: (u8, u8, u8)) -> u32 {
        let dr = c1.0 as i32 - c2.0 as i32;
        let dg = c1.1 as i32 - c2.1 as i32;
        let db = c1.2 as i32 - c2.2 as i32;

        (dr * dr + dg * dg + db * db) as u32
    }

    pub fn decompose_image(&self, img: &DynamicImage) -> Vec<PixelConstant> {
        let (width, height) = img.dimensions();
        let mut pixels = Vec::new();

        for y in 0..height {
            for x in 0..width {
                let pixel = img.get_pixel(x, y);
                let rgb = (pixel[0], pixel[1], pixel[2]);
                let palette_index = self.quantize_pixel(rgb);

                pixels.push(PixelConstant {
                    modality: Modality::Image,
                    subtype: "RGB".to_string(),
                    color_index: palette_index,
                    position: (x, y),
                });
            }
        }

        pixels
    }
}
```

---

## Module 2: SDI Generation

### 2.1 Hash Structure

```rust
use blake3::Hasher;

pub struct SDI {
    raw_hash: [u8; 32],
}

impl SDI {
    pub fn generate(
        modality: u8,
        semantic_class: u16,
        normalization: u32,
        value: &[u8],
    ) -> Self {
        let mut hasher = Hasher::new();

        // Structured input
        hasher.update(&[modality]);
        hasher.update(&semantic_class.to_be_bytes());
        hasher.update(&normalization.to_be_bytes());
        hasher.update(value);

        // Deterministic hash
        let hash = hasher.finalize();

        SDI {
            raw_hash: *hash.as_bytes(),
        }
    }

    pub fn modality(&self) -> u8 {
        self.raw_hash[0]
    }

    pub fn semantic_class(&self) -> u16 {
        u16::from_be_bytes([self.raw_hash[1], self.raw_hash[2]])
    }

    pub fn normalization(&self) -> u32 {
        u32::from_be_bytes([
            self.raw_hash[3],
            self.raw_hash[4],
            self.raw_hash[5],
            self.raw_hash[6],
        ])
    }

    pub fn value_signature(&self) -> &[u8] {
        &self.raw_hash[7..32]
    }

    pub fn as_bytes(&self) -> &[u8; 32] {
        &self.raw_hash
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_determinism() {
        let sdi1 = SDI::generate(1, 100, 0, b"test_value");
        let sdi2 = SDI::generate(1, 100, 0, b"test_value");

        assert_eq!(sdi1.as_bytes(), sdi2.as_bytes());
    }

    #[test]
    fn test_uniqueness() {
        let sdi1 = SDI::generate(1, 100, 0, b"value_a");
        let sdi2 = SDI::generate(1, 100, 0, b"value_b");

        assert_ne!(sdi1.as_bytes(), sdi2.as_bytes());
    }

    #[test]
    fn test_structure_preservation() {
        let sdi = SDI::generate(42, 1234, 5678, b"data");

        assert_eq!(sdi.modality(), 42);
        assert_eq!(sdi.semantic_class(), 1234);
        assert_eq!(sdi.normalization(), 5678);
    }
}
```

---

## Module 3: 4D Projection

### 3.1 Initial Placement (Cold Start)

```rust
use rand::{SeedableRng, Rng};
use rand_chacha::ChaCha20Rng;

pub struct InitialProjector {
    scale: f64,  // Coordinate range (e.g., 1,000,000 for X/Y)
}

impl InitialProjector {
    pub fn project(&self, sdi: &SDI) -> (f64, f64, f64, f64) {
        // Use hash as deterministic seed
        let seed_bytes: [u8; 8] = sdi.as_bytes()[0..8].try_into().unwrap();
        let seed = u64::from_le_bytes(seed_bytes);

        let mut rng = ChaCha20Rng::seed_from_u64(seed);

        // Generate coordinates in [0, 1]
        let x = rng.gen::<f64>();
        let y = rng.gen::<f64>();
        let z = rng.gen::<f64>();
        let m = rng.gen::<f64>();

        // Scale to desired range
        (
            x * self.scale,           // X: 0 to scale
            y * self.scale,           // Y: 0 to scale
            z * (self.scale / 1000.0), // Z: 0 to scale/1000 (hierarchy)
            m * (self.scale / 1000.0), // M: 0 to scale/1000 (salience)
        )
    }
}
```

### 3.2 LMDS Projection (Advanced)

```rust
use nalgebra::{DMatrix, DVector};

pub struct LMDSProjector {
    landmarks: Vec<SDI>,
    landmark_coords: DMatrix<f64>,
    l_pseudoinverse: DMatrix<f64>,
    delta_mu: DVector<f64>,
}

impl LMDSProjector {
    pub fn new(
        landmarks: Vec<SDI>,
        distance_fn: impl Fn(&SDI, &SDI) -> f64,
    ) -> Self {
        let k = landmarks.len();
        let d = 3;  // Dimensionality (XYZ)

        // Initialize landmark coordinates (from existing atoms or initial projection)
        let mut landmark_coords = DMatrix::zeros(k, d);

        for (i, landmark) in landmarks.iter().enumerate() {
            let coords = Self::get_landmark_coords(landmark);
            landmark_coords.row_mut(i).copy_from_slice(&coords);
        }

        // Calculate pairwise distances
        let mut dist_matrix = DMatrix::zeros(k, k);
        for i in 0..k {
            for j in 0..k {
                let dist = distance_fn(&landmarks[i], &landmarks[j]);
                dist_matrix[(i, j)] = dist * dist;  // Squared distance
            }
        }

        // Calculate delta_mu
        let delta_mu = dist_matrix.row_mean().transpose();

        // Calculate pseudoinverse (via SVD)
        let svd = landmark_coords.svd(true, true);
        let l_pseudoinverse = svd.pseudo_inverse(1e-10).unwrap();

        Self {
            landmarks,
            landmark_coords,
            l_pseudoinverse,
            delta_mu,
        }
    }

    pub fn project(&self, atom: &SDI, distance_fn: impl Fn(&SDI, &SDI) -> f64) -> DVector<f64> {
        // Calculate distances from atom to all landmarks
        let mut delta_a = DVector::zeros(self.landmarks.len());

        for (i, landmark) in self.landmarks.iter().enumerate() {
            let dist = distance_fn(atom, landmark);
            delta_a[i] = dist * dist;  // Squared distance
        }

        // Apply LMDS formula: x_a = -0.5 * L^# * (Δ_a - δ_μ)
        let coords = -0.5 * &self.l_pseudoinverse * (delta_a - &self.delta_mu);

        coords
    }
}
```

---

## Module 4: Hilbert Index Calculation

```rust
use hilbert_2d::h2xy_discrete;

pub struct HilbertIndexer {
    grid_resolution: u32,  // e.g., 1024 for 10-bit Hilbert curve
}

impl HilbertIndexer {
    pub fn calculate_index(&self, x: f64, y: f64, z: f64, m: f64) -> i64 {
        // Normalize coordinates to [0, 1]
        let normalize = |v: f64| v.max(0.0).min(1.0);

        // Quantize to grid
        let qx = (normalize(x) * self.grid_resolution as f64) as u32;
        let qy = (normalize(y) * self.grid_resolution as f64) as u32;
        let qz = (normalize(z) * self.grid_resolution as f64) as u32;
        let qm = (normalize(m) * self.grid_resolution as f64) as u32;

        // Calculate 3 × 2D Hilbert indices
        let h_xy = hilbert_2d::xy2h_discrete(qx, qy, self.grid_resolution, false);
        let h_yz = hilbert_2d::xy2h_discrete(qy, qz, self.grid_resolution, false);
        let h_zm = hilbert_2d::xy2h_discrete(qz, qm, self.grid_resolution, false);

        // Interleave bits
        self.interleave(h_xy, h_yz, h_zm)
    }

    fn interleave(&self, h_xy: u64, h_yz: u64, h_zm: u64) -> i64 {
        // Take 20 bits from each (60 bits total)
        let combined = ((h_xy & 0xFFFFF) << 40)
                     | ((h_yz & 0xFFFFF) << 20)
                     | (h_zm & 0xFFFFF);

        combined as i64
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_locality_preservation() {
        let indexer = HilbertIndexer { grid_resolution: 1024 };

        let idx1 = indexer.calculate_index(0.5, 0.5, 0.5, 0.5);
        let idx2 = indexer.calculate_index(0.51, 0.51, 0.51, 0.51);  // Nearby point

        // Hilbert indices should be close for nearby points
        let diff = (idx1 - idx2).abs();
        assert!(diff < 1000, "Nearby points should have close Hilbert indices");
    }
}
```

---

## Module 5: Run-Length Encoding

```rust
pub struct RLEEncoder;

impl RLEEncoder {
    pub fn encode(atoms: Vec<Atom>) -> Vec<RLEAtom> {
        if atoms.is_empty() {
            return Vec::new();
        }

        let mut encoded = Vec::new();
        let mut current_atom = atoms[0].clone();
        let mut run_length = 1u32;

        for atom in atoms.iter().skip(1) {
            if atom.sdi == current_atom.sdi {
                run_length += 1;
            } else {
                encoded.push(RLEAtom {
                    atom: current_atom,
                    run_length,
                });

                current_atom = atom.clone();
                run_length = 1;
            }
        }

        // Final atom
        encoded.push(RLEAtom {
            atom: current_atom,
            run_length,
        });

        encoded
    }
}

pub struct RLEAtom {
    pub atom: Atom,
    pub run_length: u32,
}

impl RLEAtom {
    pub fn to_geometry(&self) -> (f64, f64, f64, f64) {
        // M dimension = run length
        (
            self.atom.x,
            self.atom.y,
            self.atom.z,
            self.run_length as f64,  // High M = frequently repeated
        )
    }
}
```

---

## Module 6: Constant Pair Encoding

```rust
use std::collections::HashMap;

pub struct CPEBuilder {
    frequency_threshold: usize,
}

pub struct Composition {
    pub sdi: SDI,
    pub children: Vec<SDI>,
    pub z_level: u32,
}

impl CPEBuilder {
    pub fn build_hierarchy(&self, atoms: &[SDI]) -> Vec<Composition> {
        let mut compositions = Vec::new();
        let mut current_level = atoms.to_vec();
        let mut z_level = 1u32;

        loop {
            // Count adjacent pairs
            let pair_frequencies = self.count_pairs(&current_level);

            // Find most frequent pair above threshold
            let top_pair = pair_frequencies
                .iter()
                .max_by_key(|(_, &count)| count)
                .filter(|(_, &count)| count >= self.frequency_threshold);

            if top_pair.is_none() {
                break;  // No more frequent patterns
            }

            let ((left, right), _) = top_pair.unwrap();

            // Create composition
            let comp_sdi = self.create_composition_sdi(*left, *right, z_level);

            compositions.push(Composition {
                sdi: comp_sdi,
                children: vec![*left, *right],
                z_level,
            });

            // Replace all occurrences
            current_level = self.replace_pair(&current_level, *left, *right, comp_sdi);

            z_level += 1;
        }

        compositions
    }

    fn count_pairs(&self, atoms: &[SDI]) -> HashMap<(SDI, SDI), usize> {
        let mut frequencies = HashMap::new();

        for window in atoms.windows(2) {
            let pair = (window[0], window[1]);
            *frequencies.entry(pair).or_insert(0) += 1;
        }

        frequencies
    }

    fn replace_pair(&self, atoms: &[SDI], left: SDI, right: SDI, replacement: SDI) -> Vec<SDI> {
        let mut result = Vec::new();
        let mut i = 0;

        while i < atoms.len() {
            if i + 1 < atoms.len() && atoms[i] == left && atoms[i + 1] == right {
                result.push(replacement);
                i += 2;  // Skip both atoms
            } else {
                result.push(atoms[i]);
                i += 1;
            }
        }

        result
    }

    fn create_composition_sdi(&self, left: SDI, right: SDI, z_level: u32) -> SDI {
        // Composition SDI = hash of child SDIs
        let mut combined = Vec::new();
        combined.extend_from_slice(left.as_bytes());
        combined.extend_from_slice(right.as_bytes());

        SDI::generate(
            0xFF,  // Composition modality
            z_level as u16,
            0,
            &combined,
        )
    }
}
```

---

## Module 7: PostgreSQL COPY Protocol

```rust
use postgres::{Client, NoTls, binary_copy::BinaryCopyInWriter};
use postgres::types::Type;
use byteorder::{WriteBytesExt, LittleEndian};

pub struct BulkLoader {
    client: Client,
}

impl BulkLoader {
    pub fn new(connection_string: &str) -> Result<Self, Box<dyn std::error::Error>> {
        let client = Client::connect(connection_string, NoTls)?;
        Ok(Self { client })
    }

    pub fn load_atoms(&mut self, atoms: Vec<AtomRecord>) -> Result<u64, Box<dyn std::error::Error>> {
        let sink = self.client.copy_in(
            "COPY atom (
                atom_id, atom_class, modality, subtype,
                atomic_value, geom, hilbert_idx, z_index, m_weight
            ) FROM STDIN (FORMAT BINARY)"
        )?;

        let writer = BinaryCopyInWriter::new(sink, &[
            Type::BYTEA,
            Type::INT2,
            Type::VARCHAR,
            Type::VARCHAR,
            Type::BYTEA,
            Type::BYTEA,  // WKB geometry
            Type::INT8,
            Type::INT4,
            Type::FLOAT8,
        ]);

        let mut count = 0u64;

        for atom in atoms {
            writer.write(&[
                &atom.sdi.as_bytes(),
                &(atom.class as i16),
                &atom.modality,
                &atom.subtype,
                &atom.value,
                &atom.to_wkb(),
                &atom.hilbert_idx,
                &(atom.z_level as i32),
                &atom.m_weight,
            ])?;

            count += 1;
        }

        writer.finish()?;

        Ok(count)
    }
}

impl AtomRecord {
    fn to_wkb(&self) -> Vec<u8> {
        let mut wkb = Vec::new();

        // Byte order (1 = little endian)
        wkb.write_u8(1).unwrap();

        // Geometry type with SRID flag
        // POINTZM = 0x000000BD, with SRID = 0x20000000
        wkb.write_u32::<LittleEndian>(0x200000BD).unwrap();

        // SRID (4326 for WGS84, but we use as Cartesian)
        wkb.write_i32::<LittleEndian>(4326).unwrap();

        // Coordinates
        wkb.write_f64::<LittleEndian>(self.x).unwrap();
        wkb.write_f64::<LittleEndian>(self.y).unwrap();
        wkb.write_f64::<LittleEndian>(self.z).unwrap();
        wkb.write_f64::<LittleEndian>(self.m).unwrap();

        wkb
    }
}
```

---

## Module 8: Pipeline Orchestration

```rust
pub struct ShaderPipeline {
    quantizer: NumericQuantizer,
    text_tokenizer: TextQuantizer,
    projector: InitialProjector,
    hilbert_indexer: HilbertIndexer,
    rle_encoder: RLEEncoder,
    cpe_builder: CPEBuilder,
    bulk_loader: BulkLoader,
}

impl ShaderPipeline {
    pub fn process_text_file(&mut self, file_path: &str) -> Result<u64> {
        // 1. Read file
        let content = std::fs::read_to_string(file_path)?;

        // 2. Tokenize
        let tokens = self.text_tokenizer.encode(&content);

        // 3. Generate SDIs for each token
        let mut atoms = Vec::new();
        for token in tokens {
            let sdi = SDI::generate(
                Modality::Text as u8,
                0,  // Semantic class: 0 for raw tokens
                0,  // Normalization: 0
                token.token_str.as_bytes(),
            );

            // 4. Project to 4D
            let (x, y, z, m) = self.projector.project(&sdi);

            // 5. Calculate Hilbert index
            let hilbert_idx = self.hilbert_indexer.calculate_index(x, y, z, m);

            atoms.push(Atom {
                sdi,
                class: AtomClass::Constant,
                modality: "Text".to_string(),
                subtype: "BPE".to_string(),
                value: Some(token.token_str.into_bytes()),
                x, y, z, m,
                hilbert_idx,
                z_level: 0,
                m_weight: m,
            });
        }

        // 6. Run-Length Encoding
        let rle_atoms = self.rle_encoder.encode(atoms);

        // 7. Build hierarchy via CPE
        let compositions = self.cpe_builder.build_hierarchy(
            &rle_atoms.iter().map(|a| a.atom.sdi).collect::<Vec<_>>()
        );

        // 8. Bulk load to database
        let mut all_records = Vec::new();

        // Add constants
        for rle_atom in rle_atoms {
            all_records.push(rle_atom.to_record());
        }

        // Add compositions
        for comp in compositions {
            all_records.push(comp.to_record());
        }

        let count = self.bulk_loader.load_atoms(all_records)?;

        Ok(count)
    }
}
```

---

## Performance Targets

| Operation | Target Throughput |
|-----------|-------------------|
| Text tokenization | > 1 MB/sec |
| SDI generation | > 100K hashes/sec |
| Quantization | > 1M values/sec |
| Hilbert calculation | > 500K indices/sec |
| COPY protocol loading | > 10K atoms/sec sustained |

---

## Testing Strategy

```rust
#[cfg(test)]
mod integration_tests {
    use super::*;

    #[test]
    fn test_end_to_end_text_pipeline() {
        let mut pipeline = ShaderPipeline::new(...);

        // Process small text file
        let count = pipeline.process_text_file("test.txt").unwrap();

        // Verify atoms created
        assert!(count > 0);

        // Verify deduplication
        let count2 = pipeline.process_text_file("test.txt").unwrap();
        assert_eq!(count2, 0);  // All atoms already exist
    }

    #[test]
    fn test_rle_effectiveness() {
        // Create stream with repetition
        let atoms = vec![
            create_atom("A"),
            create_atom("A"),
            create_atom("A"),
            create_atom("B"),
            create_atom("B"),
            create_atom("C"),
        ];

        let encoded = RLEEncoder::encode(atoms);

        assert_eq!(encoded.len(), 3);  // A, B, C
        assert_eq!(encoded[0].run_length, 3);
        assert_eq!(encoded[1].run_length, 2);
        assert_eq!(encoded[2].run_length, 1);
    }
}
```

---

## Deployment Configuration

```toml
# shader.toml

[database]
host = "localhost"
port = 5432
database = "hartonomous"
user = "postgres"
password = "***"

[quantization]
numeric_precision = 2  # Decimal places
image_palette_size = 256  # Colors
text_tokenizer = "cl100k_base"  # BPE model

[projection]
coordinate_scale = 1000000.0  # X/Y range
hierarchy_scale = 1000.0      # Z range
salience_scale = 1000.0       # M range

[hilbert]
grid_resolution = 1024  # 10-bit Hilbert curve

[rle]
enabled = true

[cpe]
enabled = true
frequency_threshold = 10  # Minimum pair frequency

[bulk_loading]
batch_size = 10000  # Atoms per COPY batch
max_retries = 3
```

---

**Complete Shader implementation following this specification enables deterministic, high-throughput ingestion into Hartonomous substrate.**
