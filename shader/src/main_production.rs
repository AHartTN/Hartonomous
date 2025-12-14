/*!
 * Hartonomous Shader - Production Safetensors Processor
 * 
 * SIMD-optimized preprocessing pipeline:
 * - Memory-mapped safetensors loading (zero-copy)
 * - AVX2 batch quantization
 * - BLAKE3 SDI generation
 * - 4D Hilbert encoding
 * - PostGIS WKB geometry
 * - Binary PostgreSQL COPY protocol
 * - Rayon multicore parallelism
 */

use clap::Parser;
use std::path::PathBuf;
use std::io::{self, Write};
use std::fs::File;
use memmap2::Mmap;
use rayon::prelude::*;

mod sdi;
mod quantizer;
mod quantizer_simd;
mod hilbert_indexer;
mod binary_copy;
mod geometry_wkb;
mod copy_loader;
mod rle_encoder;
mod cpe_builder;
mod error;

use binary_copy::{BinaryCopyWriter, AtomRecord};
use quantizer_simd::quantize_matrix;
use geometry_wkb::{Point4D, LineString4D};
use error::ShaderResult;

#[derive(Parser)]
#[command(name = "hartonomous-shader")]
#[command(about = "Production safetensors → PostgreSQL COPY pipeline")]
struct Cli {
    /// Path to safetensors model file
    #[arg(short, long)]
    model: PathBuf,

    /// Model identifier name
    #[arg(short, long)]
    name: String,

    /// Quantization precision (decimal places)
    #[arg(short = 'p', long, default_value = "4")]
    precision: u32,

    /// Salience threshold (filter weights below this)
    #[arg(short = 's', long, default_value = "0.0001")]
    salience_threshold: f32,

    /// Number of threads (0 = auto)
    #[arg(short = 'j', long, default_value = "0")]
    threads: usize,

    /// Output file (default: stdout)
    #[arg(short = 'o', long)]
    output: Option<PathBuf>,
}

fn main() -> ShaderResult<()> {
    // Configure thread pool
    let cli = Cli::parse();
    
    let num_threads = if cli.threads == 0 {
        rayon::current_num_threads()
    } else {
        cli.threads
    };

    rayon::ThreadPoolBuilder::new()
        .num_threads(num_threads)
        .build_global()
        .map_err(|e| error::ShaderError::Other(e.to_string()))?;

    eprintln!("[SHADER] Hartonomous Production Pipeline");
    eprintln!("[SHADER] Model: {}", cli.model.display());
    eprintln!("[SHADER] Name: {}", cli.name);
    eprintln!("[SHADER] Precision: {} decimals", cli.precision);
    eprintln!("[SHADER] Salience: {}", cli.salience_threshold);
    eprintln!("[SHADER] Threads: {}", num_threads);

    // Open output stream
    let output: Box<dyn Write> = match &cli.output {
        Some(path) => {
            eprintln!("[SHADER] Output: {}", path.display());
            Box::new(File::create(path)?)
        }
        None => {
            eprintln!("[SHADER] Output: <stdout>");
            Box::new(io::stdout())
        }
    };

    // Process model
    process_safetensors(&cli, output)?;

    eprintln!("[SHADER] Complete");
    Ok(())
}

fn process_safetensors(config: &Cli, mut output: Box<dyn Write>) -> ShaderResult<()> {
    // Memory-map safetensors file
    let file = File::open(&config.model)?;
    let mmap = unsafe { Mmap::map(&file)? };
    
    // Parse safetensors format
    let tensors = safetensors::SafeTensors::deserialize(&mmap)
        .map_err(|e| error::ShaderError::Other(e.to_string()))?;

    let tensor_names: Vec<_> = tensors.names().to_vec();
    eprintln!("[SHADER] Loaded {} tensors", tensor_names.len());

    // Initialize binary COPY writer
    let mut copy_writer = BinaryCopyWriter::new(&mut output);
    copy_writer.start_copy()?;

    // Filter weight matrices
    let weight_keys: Vec<_> = tensor_names.iter()
        .filter(|k| k.contains("weight"))
        .cloned()
        .collect();

    eprintln!("[SHADER] Processing {} weight matrices", weight_keys.len());

    // Track stats
    let total_atoms = std::sync::atomic::AtomicUsize::new(0);
    let total_filtered = std::sync::atomic::AtomicUsize::new(0);

    // Process each matrix sequentially to avoid thread-safety issues with writer
    for key in &weight_keys {
        // Get tensor view
        let view = tensors.tensor(key)
            .map_err(|e: safetensors::SafeTensorError| error::ShaderError::Other(e.to_string()))?;
        
        let shape = view.shape();

        // Skip non-matrix tensors
        if shape.len() != 2 {
            continue;
        }

        let (rows, cols) = (shape[0], shape[1]);
        if rows < 10 || cols < 10 {
            continue;
        }

        // Parse layer structure
        let (layer_idx, component, projection) = parse_bert_key(key)?;

        eprintln!("[SHADER] Layer {layer_idx:2} | {component:12} | {projection:8} | {rows}×{cols}");

        // Extract float32 data
        let data: Vec<f32> = view.data()
            .chunks_exact(4)
            .map(|chunk| f32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]))
            .collect();

        // Quantize and filter
        let result = quantize_matrix(&data, rows, cols, config.precision, config.salience_threshold);

        eprintln!("[SHADER]   → {} constants, {} compositions ({} filtered)",
            result.constants.len(), result.compositions.len(), result.filtered_count);
        
        // Build and write constant atom records
        for (value, magnitude) in &result.constants {
                let sdi = generate_weight_sdi(*value);
                let geom = Point4D::from_value(*value, *magnitude);
                let hilbert = encode_hilbert_point(&geom);

                let record = AtomRecord {
                    atom_id: sdi.to_vec(),
                    atom_class: 0,
                    modality: 2,
                    subtype: "weight".to_string(),
                    atomic_value: Some(value.to_le_bytes().to_vec()),
                    geom_wkb: geom.to_wkb(),
                    hilbert_index: hilbert,
                    metadata: serde_json::json!({"type": "weight_constant", "value": value}).to_string(),
                };
                
                copy_writer.write_record(&record)?;
        }

        // Generate and write composition atom records
        for comp in &result.compositions {
                let sdi = generate_composition_sdi(&config.name, layer_idx, &component, &projection, comp.row, comp.col);
                let geom = LineString4D::from_composition(comp.row, comp.col, rows, cols, layer_idx, comp.value, comp.magnitude);
                let hilbert = encode_hilbert_linestring(&geom);

                let record = AtomRecord {
                    atom_id: sdi.to_vec(),
                    atom_class: 1,
                    modality: 2,
                    subtype: "transformation".to_string(),
                    atomic_value: None,
                    geom_wkb: geom.to_wkb(),
                    hilbert_index: hilbert,
                    metadata: serde_json::json!({
                        "model": config.name,
                        "layer": layer_idx,
                        "component": component,
                        "projection": projection,
                        "from_dim": comp.row,
                        "to_dim": comp.col,
                        "shape": format!("{}x{}", rows, cols)
                    }).to_string(),
                };
                
                copy_writer.write_record(&record)?;
        }
        
        total_atoms.fetch_add(result.constants.len() + result.compositions.len(), std::sync::atomic::Ordering::Relaxed);
        total_filtered.fetch_add(result.filtered_count, std::sync::atomic::Ordering::Relaxed);
    }
    
    copy_writer.end_copy()?;

    let total = total_atoms.load(std::sync::atomic::Ordering::Relaxed);
    let filtered = total_filtered.load(std::sync::atomic::Ordering::Relaxed);
    eprintln!("[SHADER] Total: {} atoms ({} filtered)", total, filtered);

    Ok(())
}

fn parse_bert_key(key: &str) -> ShaderResult<(usize, String, String)> {
    let parts: Vec<&str> = key.split('.').collect();
    
    let layer_idx = parts.iter()
        .position(|&p| p == "layer")
        .and_then(|i| parts.get(i + 1))
        .and_then(|s| s.parse::<usize>().ok())
        .ok_or_else(|| error::ShaderError::Other("Invalid layer index".to_string()))?;

    let component = if parts.contains(&"attention") {
        "attention"
    } else if parts.contains(&"intermediate") || parts.contains(&"output") {
        "ffn"
    } else {
        "other"
    }.to_string();

    let projection = if parts.contains(&"query") {
        "query"
    } else if parts.contains(&"key") {
        "key"
    } else if parts.contains(&"value") {
        "value"
    } else if parts.contains(&"dense") && parts.contains(&"attention") {
        "output"
    } else if parts.contains(&"intermediate") {
        "up"
    } else if parts.contains(&"output") && !parts.contains(&"intermediate") {
        "down"
    } else {
        "other"
    }.to_string();

    Ok((layer_idx, component, projection))
}

fn generate_weight_sdi(value: f32) -> [u8; 32] {
    use blake3::Hasher;
    let mut hasher = Hasher::new();
    hasher.update(&value.to_le_bytes());
    *hasher.finalize().as_bytes()
}

fn generate_composition_sdi(model: &str, layer: usize, component: &str, projection: &str, row: usize, col: usize) -> [u8; 32] {
    use blake3::Hasher;
    let mut hasher = Hasher::new();
    hasher.update(format!("edge:{}:{}:{}:{}:{}:{}", model, layer, component, projection, row, col).as_bytes());
    *hasher.finalize().as_bytes()
}

fn encode_hilbert_point(point: &Point4D) -> i64 {
    hilbert_indexer::encode_4d(point.x, point.y, point.z, point.m, 10)
}

fn encode_hilbert_linestring(line: &LineString4D) -> i64 {
    hilbert_indexer::encode_4d(line.x_start, line.y_start, line.z_start, line.m_start, 10)
}
