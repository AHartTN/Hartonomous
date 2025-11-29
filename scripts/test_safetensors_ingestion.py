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
import logging
import time
from pathlib import Path

# Add project root to path
project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))
sys.path.insert(0, str(project_root / "api"))

from api.services.safetensors_atomization import SafeTensorsAtomizer
from api.dependencies import get_db_connection

# Configure logging to show ALL output with timestamps
logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s [%(levelname)s] %(name)s: %(message)s',
    datefmt='%H:%M:%S'
)


async def test_safetensors_ingestion():
    """Test SafeTensors atomization with the local embedding model."""
    
    start_time = time.time()
    
    print()
    print("█" * 80)
    print("█" + " " * 78 + "█")
    print("█" + " " * 22 + "SAFETENSORS INGESTION TEST" + " " * 30 + "█")
    print("█" + " " * 78 + "█")
    print("█" * 80)
    print()
    print(f"Test started at: {time.strftime('%Y-%m-%d %H:%M:%S')}")
    print()
    
    # Use cached embedding model (87MB, includes config, tokenizer, model)
    cache_dir = project_root / ".cache" / "embedding_models" / "all-MiniLM-L6-v2"
    snapshot_dir = cache_dir / "snapshots" / "c9745ed1d9f207416be6d2e6f8de32d1f16199bf"
    model_path = snapshot_dir / "model.safetensors"
    config_path = snapshot_dir / "config.json"
    tokenizer_path = snapshot_dir / "tokenizer.json"
    
    print("=" * 80)
    print("STEP 1: CHECKING MODEL FILES")
    print("=" * 80)
    
    # Verify files exist
    if not model_path.exists():
        print(f"❌ Model not found: {model_path}")
        print(f"   Expected local cache at: {cache_dir}")
        print()
        print("To fix: Run the API server once to trigger model download, or:")
        print("  python -c 'from sentence_transformers import SentenceTransformer; SentenceTransformer(\"all-MiniLM-L6-v2\")'")
        return False
    
    print(f"✓ Model file found: model.safetensors")
    file_size_mb = model_path.stat().st_size / (1024 * 1024)
    print(f"   Size: {file_size_mb:.2f} MB")
    print(f"   Path: {model_path}")
    print()
    
    if not config_path.exists():
        print(f"⚠ Config not found: {config_path} (will skip config atomization)")
        config_path = None
    else:
        print(f"✓ Config file found: config.json")
        
    if not tokenizer_path.exists():
        print(f"⚠ Tokenizer not found: {tokenizer_path} (will skip vocab atomization)")
        tokenizer_path = None
    else:
        print(f"✓ Tokenizer file found: tokenizer.json")
    
    print()
    print("MODEL INFO:")
    print(f"   Model: all-MiniLM-L6-v2 (sentence-transformers embedding model)")
    print(f"   Format: SafeTensors")
    print(f"   Components: Model {'+ Config' if config_path else ''} {'+ Tokenizer' if tokenizer_path else ''}")
    print()
    print("PROCESSING PLAN:")
    print(f"   Max tensors: None (process ALL tensors - full validation)")
    print(f"   Threshold: 1e-6 (sparse encoding for very small weights)")
    print()
    
    # Initialize atomizer with default threshold
    print("=" * 80)
    print("STEP 2: INITIALIZING ATOMIZER")
    print("=" * 80)
    atomizer = SafeTensorsAtomizer(threshold=1e-6)
    print("✓ SafeTensorsAtomizer initialized")
    print()
    
    # Get database connection
    print("=" * 80)
    print("STEP 3: CONNECTING TO DATABASE")
    print("=" * 80)
    conn_start = time.time()
    async with await get_db_connection() as conn:
        conn_time = time.time() - conn_start
        print(f"✓ Database connection established in {conn_time:.2f}s")
        print()
        
        try:
            print("=" * 80)
            print("STEP 4: ATOMIZING MODEL")
            print("=" * 80)
            print(f"   Start time: {time.strftime('%H:%M:%S')}")
            print()
            
            # Atomize model
            atomize_start = time.time()
            result = await atomizer.atomize_model(
                file_path=model_path,
                model_name="all-MiniLM-L6-v2",
                conn=conn,
                config_path=config_path,
                tokenizer_path=tokenizer_path,
                max_tensors=None,  # Process all tensors for full validation
            )
            atomize_time = time.time() - atomize_start
            
            # Display results
            print()
            print("=" * 80)
            print("STEP 5: INGESTION COMPLETE")
            print("=" * 80)
            print(f"   Total time: {atomize_time:.2f}s")
            print(f"   Model Atom ID: {result['model_atom_id']}")
            print(f"   Model name: {result['model_name']}")
            print(f"   File size: {result['file_size_bytes'] / 1e6:.2f} MB")
            print()
            
            # Vocabulary stats
            if result.get('vocab_stats'):
                vocab = result['vocab_stats']
                print("VOCABULARY STATS:")
                print(f"   Tokens: {vocab.get('total_tokens', 0):,}")
                print(f"   Characters: {vocab.get('total_chars', 0):,}")
                print(f"   Token atoms created: {vocab.get('token_atoms', 0):,}")
                print(f"   Character atoms created: {vocab.get('char_atoms', 0):,}")
                print()
            
            # Configuration stats
            if result.get('config_stats'):
                config = result['config_stats']
                print("CONFIGURATION STATS:")
                print(f"   Architecture atoms: {config.get('arch_atoms', 0):,}")
                print(f"   Parameters: {config.get('total_params', 0):,}")
                print()
            
            # Weight stats
            if result.get('weight_stats'):
                weights = result['weight_stats']
                total_weights = weights['total_weights']
                weight_atoms = weights['weight_atoms']
                dedup_ratio = total_weights / weight_atoms if weight_atoms > 0 else 0
                sparse_count = weights.get('sparse_count', 0)
                sparse_pct = (sparse_count / total_weights * 100) if total_weights > 0 else 0
                processing_time = weights['processing_time_secs']
                weights_per_sec = total_weights / processing_time if processing_time > 0 else 0
                
                print("WEIGHT PROCESSING STATS:")
                print(f"   Tensors processed: {weights['tensors_processed']}")
                print(f"   Total weights: {total_weights:,}")
                print(f"   Weight atoms created: {weight_atoms:,}")
                print(f"   Sparse weights skipped: {sparse_count:,} ({sparse_pct:.2f}%)")
                print(f"   Processing time: {processing_time:.2f}s")
                print(f"   Processing speed: {weights_per_sec:,.0f} weights/sec")
                print()
                print("EFFICIENCY METRICS:")
                print(f"   Deduplication ratio: {dedup_ratio:.2f}x")
                print(f"   Memory savings: {(1 - 1/dedup_ratio)*100:.1f}%" if dedup_ratio > 1 else "   Memory savings: 0%")
                print()
            
            # Total atoms
            total_atoms = result.get('total_atoms', 0)
            print(f"TOTAL ATOMS CREATED: {total_atoms:,}")
            print()
            
            # Sample atoms (if any)
            if result.get('sample_atoms'):
                print("SAMPLE ATOMS (first 5):")
                for i, atom in enumerate(result['sample_atoms'][:5], 1):
                    atom_type = atom.get('atom_type', 'unknown')
                    value = atom.get('value', 'N/A')
                    hash_short = atom.get('content_hash', 'unknown')[:16]
                    print(f"   {i}. Type: {atom_type}")
                    print(f"      Value: {value}")
                    print(f"      Hash: {hash_short}...")
                print()
            
            print("=" * 80)
            print("✓ TEST COMPLETE - ALL STEPS SUCCESSFUL")
            print("=" * 80)
            total_time = time.time() - start_time
            print(f"Total execution time: {total_time:.2f}s")
            print(f"End time: {time.strftime('%H:%M:%S')}")
            print("=" * 80)
            return True
            
        except Exception as e:
            print()
            print("=" * 80)
            print("❌ TEST FAILED")
            print("=" * 80)
            print(f"Error: {str(e)}")
            print()
            import traceback
            traceback.print_exc()
            print("=" * 80)
            return False


if __name__ == "__main__":
    # Fix for Windows ProactorEventLoop issue with psycopg
    if sys.platform == 'win32':
        print()
        print("⚙ Configuring Windows event loop policy for psycopg compatibility...")
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
        print("✓ Event loop configured")
    
    try:
        success = asyncio.run(test_safetensors_ingestion())
        sys.exit(0 if success else 1)
    except KeyboardInterrupt:
        print()
        print("=" * 80)
        print("⚠ Test interrupted by user (Ctrl+C)")
        print("=" * 80)
        sys.exit(1)
    except Exception as e:
        print()
        print("=" * 80)
        print("❌ FATAL ERROR")
        print("=" * 80)
        print(f"Error: {str(e)}")
        import traceback
        traceback.print_exc()
        print("=" * 80)
        sys.exit(1)
