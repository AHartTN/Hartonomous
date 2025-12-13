use hartonomous_shader::{
    sdi::{SdiGenerator, Modality, SemanticClass, Normalization},
    quantizer::NumericQuantizer,
    hilbert_indexer::HilbertIndexer,
    copy_loader::{CopyLoader, Atom},
    ShaderResult,
};
use std::env;
use std::fs::File;
use std::io::{BufRead, BufReader};
use tracing::{info, error, warn};
use tracing_subscriber;

fn main() -> ShaderResult<()> {
    // Initialize logging
    tracing_subscriber::fmt::init();
    
    let args: Vec<String> = env::args().collect();
    if args.len() < 3 {
        eprintln!("Usage: {} <input_file> <connection_string>", args[0]);
        eprintln!("Example: {} data.txt 'host=localhost dbname=hartonomous user=postgres'", args[0]);
        std::process::exit(1);
    }
    
    let input_file = &args[1];
    let conn_string = &args[2];
    
    info!("Hartonomous Shader starting");
    info!("Input: {}", input_file);
    
    // Initialize components
    let mut sdi_gen = SdiGenerator::new();
    let quantizer = NumericQuantizer::new(2);
    let hilbert = HilbertIndexer::new(10);
    let mut loader = CopyLoader::new(conn_string, 10000)?;
    
    // Process file
    let file = File::open(input_file)?;
    let reader = BufReader::new(file);
    
    let mut atoms = Vec::new();
    let mut line_count = 0;
    
    for line in reader.lines() {
        let line = line?;
        line_count += 1;
        
        // Tokenize
        for token in line.split_whitespace() {
            let token_lower = token.to_lowercase();
            
            // Generate SDI
            let atom_id = sdi_gen.generate_token(&token_lower)?;
            
            // Initial placement (random for now - Cortex will refine)
            let x = (atom_id[0] as f64 / 255.0 - 0.5) * 100.0;
            let y = (atom_id[1] as f64 / 255.0 - 0.5) * 100.0;
            let z = 0.0; // Raw data
            let m = 1.0; // Default salience
            
            // Hilbert index
            let hilbert_idx = hilbert.encode(
                (x + 50.0) / 100.0,
                (y + 50.0) / 100.0,
                0.0,
                m
            )?;
            
            atoms.push(Atom {
                atom_id: atom_id.to_vec(),
                atom_class: 0,
                modality: 2, // Text
                subtype: Some("token".to_string()),
                atomic_value: Some(token_lower.as_bytes().to_vec()),
                x,
                y,
                z,
                m,
                hilbert_index: hilbert_idx,
                metadata: None,
            });
        }
        
        // Batch insert every 10k atoms
        if atoms.len() >= 10000 {
            let inserted = loader.load_atoms(&atoms)?;
            info!("Loaded {} atoms (line {})", inserted, line_count);
            atoms.clear();
        }
    }
    
    // Load remaining
    if !atoms.is_empty() {
        let inserted = loader.load_atoms(&atoms)?;
        info!("Loaded {} atoms (final)", inserted);
    }
    
    info!("Processing complete. {} lines processed", line_count);
    
    Ok(())
}
