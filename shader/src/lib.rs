pub mod sdi;
pub mod quantizer;
pub mod quantizer_simd;
pub mod hilbert_indexer;
pub mod copy_loader;
pub mod binary_copy;
pub mod geometry_wkb;
pub mod rle_encoder;
pub mod cpe_builder;
pub mod error;

pub use error::{ShaderError, ShaderResult};
pub use quantizer_simd::{quantize_f32_avx2, quantize_matrix, QuantizedMatrix, CompositionAtom};
pub use binary_copy::{BinaryCopyWriter, AtomRecord, CopyError, CopyResult};
pub use geometry_wkb::{Point4D, LineString4D};
