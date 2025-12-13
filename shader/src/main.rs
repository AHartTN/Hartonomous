use hartonomous_shader::{
    sdi::SdiGenerator,
    quantizer::NumericQuantizer,
    hilbert_indexer::HilbertIndexer,
    copy_loader::{CopyLoader, Atom},
    ShaderResult,
};
use std::env;
use std::fs::File;
use std::io::{BufRead, BufReader};
use tracing::info;
use tracing_subscriber;

fn main() -> ShaderResult<()> {
    tracing_subscriber::fmt::init();
    
    let args: Vec<String> = env::args().collect();
    if args.len() < 3 {
        eprintln!("Usage: {} <input_file> <connection_string>", args[0]);
        eprintln!("Example: {} data.txt 'host=localhost dbname=hartonomous user=hartonomous'", args[0]);
        std::process::exit(1);
    }
    
    let input_file = &args[1];
    let conn_string = &args[2];
    
    info!("Hartonomous Shader starting");
    info!("Input: {}", input_file);
    
    let mut sdi_gen = SdiGenerator::new();
    let hilbert = HilbertIndexer::new(10);
    let mut loader = CopyLoader::new(conn_string, 10000)?;
    
    let file = File::open(input_file)?;
    let reader = BufReader::new(file);
    
    let mut line_count = 0;
    
    for line in reader.lines() {
        let line = line?;
        line_count += 1;
        
        for token in line.split_whitespace() {
            let token_lower = token.to_lowercase();
            
            let atom_id = sdi_gen.generate_token(&token_lower)?;
            
            let x = (atom_id[0] as f64 / 255.0 - 0.5) * 100.0;
            let y = (atom_id[1] as f64 / 255.0 - 0.5) * 100.0;
            let z = 0.0;
            let m = 1.0;
            
            let hilbert_idx = hilbert.encode(
                (x + 50.0) / 100.0,
                (y + 50.0) / 100.0,
                0.0,
                m
            )?;
            
            loader.queue_atom(Atom {
                atom_id: atom_id.to_vec(),
                atom_class: 0,
                modality: 2,
                subtype: Some("token".to_string()),
                atomic_value: Some(token_lower.as_bytes().to_vec()),
                x,
                y,
                z,
                m,
                hilbert_index: hilbert_idx,
                metadata: None,
            })?;
        }
    }
    
    let total = loader.flush()?;
    info!("Processing complete. {} lines, {} atoms loaded", line_count, total);
    
    Ok(())
}
