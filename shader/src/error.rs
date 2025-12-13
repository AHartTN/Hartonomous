use thiserror::Error;

#[derive(Error, Debug)]
pub enum ShaderError {
    #[error("Database error: {0}")]
    Database(#[from] postgres::Error),
    
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),
    
    #[error("Serialization error: {0}")]
    Serialization(#[from] serde_json::Error),
    
    #[error("Invalid atom value: {0}")]
    InvalidAtomValue(String),
    
    #[error("Quantization error: {0}")]
    Quantization(String),
    
    #[error("Configuration error: {0}")]
    Config(String),
}

pub type ShaderResult<T> = Result<T, ShaderError>;
