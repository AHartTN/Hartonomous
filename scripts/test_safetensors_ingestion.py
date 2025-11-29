"""
SafeTensors Ingestion Test
Tests atomization of a SafeTensors model with all components:
- Vocabulary (from tokenizer.json)
- Configuration (from config.json)
- Tensor weights (from model.safetensors)

Uses the local cached embedding model (all-MiniLM-L6-v2) for quick validation.
"""
import sys
import asyncio
from pathlib import Path

# Add project root to path
project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))
sys.path.insert(0, str(project_root / "api"))

from api.services.safetensors_atomization import SafeTensorsAtomizer
from api.dependencies import get_db_connection


async def test_safetensors_ingestion():
    """Test SafeTensors atomization with the local embedding model."""
    
    # Use cached embedding model (87MB, includes config, tokenizer, model)
    cache_dir = project_root / ".cache" / "embedding_models" / "all-MiniLM-L6-v2"
    model_path = cache_dir / "model.safetensors"
    config_path = cache_dir / "config.json"
    tokenizer_path = cache_dir / "tokenizer.json"
    
    # Verify files exist
    if not model_path.exists():
        print(f"❌ Model not found: {model_path}")
        print(f"   Expected local cache at: {cache_dir}")
        return False
        
    if not config_path.exists():
        print(f"⚠️  Config not found: {config_path} (will skip config atomization)")
        config_path = None
        
    if not tokenizer_path.exists():
        print(f"⚠️  Tokenizer not found: {tokenizer_path} (will skip vocab atomization)")
        tokenizer_path = None
    
    print("=" * 80)
    print("SAFETENSORS INGESTION TEST")
    print("=" * 80)
    print(f"Model: all-MiniLM-L6-v2 (embedding model)")
    print(f"Path: {model_path}")
    print(f"Size: {model_path.stat().st_size / 1e6:.2f} MB")
    print(f"Config: {'✓' if config_path else '✗'}")
    print(f"Tokenizer: {'✓' if tokenizer_path else '✗'}")
    print()
    print("Note: This test processes ALL tensors in the model (full validation)")
    print("=" * 80)
    print()
    
    # Initialize atomizer with default threshold
    atomizer = SafeTensorsAtomizer(threshold=1e-6)
    
    # Get database connection
    async with await get_db_connection() as conn:
        try:
            # Atomize model
            result = await atomizer.atomize_model(
                file_path=model_path,
                model_name="all-MiniLM-L6-v2",
                conn=conn,
                config_path=config_path,
                tokenizer_path=tokenizer_path,
                max_tensors=None,  # Process all tensors for full validation
            )
            
            # Display results
            print()
            print("=" * 80)
            print("INGESTION SUMMARY")
            print("=" * 80)
            print(f"Model Atom ID: {result['model_atom_id']}")
            print(f"Model name: {result['model_name']}")
            print(f"File size: {result['file_size_bytes'] / 1e6:.2f} MB")
            print()
            
            # Vocabulary stats
            if result.get('vocab_stats'):
                vocab = result['vocab_stats']
                print(f"Vocabulary:")
                print(f"  Tokens: {vocab.get('total_tokens', 0):,}")
                print(f"  Characters: {vocab.get('total_chars', 0):,}")
                print(f"  Token atoms: {vocab.get('token_atoms', 0):,}")
                print(f"  Character atoms: {vocab.get('char_atoms', 0):,}")
                print()
            
            # Configuration stats
            if result.get('config_stats'):
                config = result['config_stats']
                print(f"Configuration:")
                print(f"  Architecture atoms: {config.get('arch_atoms', 0):,}")
                print(f"  Parameters: {config.get('total_params', 0):,}")
                print()
            
            # Weight stats
            if result.get('weight_stats'):
                weights = result['weight_stats']
                total_weights = weights['total_weights']
                weight_atoms = weights['weight_atoms']
                dedup_ratio = total_weights / weight_atoms if weight_atoms > 0 else 0
                
                print(f"Weights:")
                print(f"  Tensors processed: {weights['tensors_processed']}")
                print(f"  Total weights: {total_weights:,}")
                print(f"  Weight atoms: {weight_atoms:,}")
                print(f"  Deduplication: {dedup_ratio:.1f}x")
                print(f"  Sparse weights: {weights.get('sparse_count', 0):,} ({weights.get('sparse_count', 0) / total_weights * 100:.2f}%)")
                print(f"  Processing time: {weights['processing_time_secs']:.2f}s")
                print()
            
            # Total atoms
            print(f"Total atoms created: {result.get('total_atoms', 0):,}")
            print()
            
            # Sample atoms (if any)
            if result.get('sample_atoms'):
                print("Sample Atoms:")
                for atom in result['sample_atoms'][:5]:
                    print(f"  - {atom['atom_type']}: {atom.get('value', 'N/A')} (hash: {atom['content_hash'][:16]}...)")
                print()
            
            print("=" * 80)
            print("✓ SafeTensors ingestion test PASSED")
            print("=" * 80)
            return True
            
        except Exception as e:
            print()
            print("=" * 80)
            print("❌ SafeTensors ingestion test FAILED")
            print("=" * 80)
            print(f"Error: {str(e)}")
            import traceback
            traceback.print_exc()
            return False


if __name__ == "__main__":
    success = asyncio.run(test_safetensors_ingestion())
    sys.exit(0 if success else 1)
