"""Test script for GGUF model atomization.

Run this to test atomizing your Ollama models in D:/Models
"""

import asyncio
import sys
import logging
import time
from pathlib import Path
import pytest

# Add parent directory to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from api.services.model_atomization import GGUFAtomizer
from psycopg import AsyncConnection
from psycopg_pool import AsyncConnectionPool
from api.config import settings

pytestmark = [pytest.mark.performance, pytest.mark.gguf]
from api.config import settings

# Configure logging to show ALL output with timestamps
logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s [%(levelname)s] %(name)s: %(message)s',
    datefmt='%H:%M:%S'
)

# Enable psycopg pool logging specifically
logging.getLogger("psycopg.pool").setLevel(logging.DEBUG)


async def test_gguf_atomization():
    """Test atomizing a GGUF model with quick validation."""
    
    start_time = time.time()
    
    print("=" * 80)
    print("STEP 1: CREATING CONNECTION POOL")
    print("=" * 80)
    print(f"Connection string: {settings.pghost}:{settings.pgport}/{settings.pgdatabase}")
    print(f"Pool configuration: min_size=2, max_size=4")
    print(f"Start time: {time.strftime('%H:%M:%S')}")
    
    # Create connection pool instead of single connection
    conn_start = time.time()
    pool = AsyncConnectionPool(
        conninfo=settings.get_connection_string(),
        min_size=2,
        max_size=4,
        open=False  # Open manually for proper async
    )
    
    try:
        await pool.open()
        await pool.wait()  # Wait for min_size connections to be ready
        conn_time = time.time() - conn_start
        print(f"✓ Connection pool initialized in {conn_time:.2f}s")
        print()

        async with pool.connection() as conn:
            # Use small test model
            test_file = Path(".cache/test_models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf")
            
            print("=" * 80)
            print("STEP 2: CHECKING MODEL FILE")
            print("=" * 80)
            
            if not test_file.exists():
                print(f"❌ Test model not found: {test_file}")
                print("📥 Downloading TinyLlama-1.1B (~637MB)...")
                print(f"   URL: https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF")
                test_file.parent.mkdir(parents=True, exist_ok=True)
                
                download_start = time.time()
                import urllib.request
                
                def download_progress(block_num, block_size, total_size):
                    downloaded = block_num * block_size
                    percent = (downloaded / total_size) * 100
                    mb_downloaded = downloaded / (1024 * 1024)
                    mb_total = total_size / (1024 * 1024)
                    elapsed = time.time() - download_start
                    speed = (mb_downloaded / elapsed) if elapsed > 0 else 0
                    print(f"\r   Progress: {percent:.1f}% ({mb_downloaded:.1f}/{mb_total:.1f} MB) @ {speed:.1f} MB/s", end='')
                
                urllib.request.urlretrieve(
                    "https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/resolve/main/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf",
                    test_file,
                    reporthook=download_progress
                )
                print()  # newline after progress
                download_time = time.time() - download_start
                print(f"✓ Download complete in {download_time:.1f}s ({test_file.stat().st_size / (1024*1024*download_time):.1f} MB/s)")
            else:
                print(f"✓ Test model found: {test_file}")
            
            file_size_mb = test_file.stat().st_size / (1024 * 1024)
            print(f"   File: {test_file.name}")
            print(f"   Size: {file_size_mb:.2f} MB")
            print(f"   Path: {test_file.absolute()}")
            print()
            
            print("=" * 80)
            print("STEP 3: INITIALIZING ATOMIZER")
            print("=" * 80)
            print(f"   Threshold: 0.1 (sparse encoding - weights < 0.1 will be skipped)")
            print(f"   Max tensors: 2 (quick validation only)")
            print(f"   Note: Full ingestion available via scripts/ingest_model.py")
            print()

            # Create atomizer (with higher threshold for testing)
            atomizer = GGUFAtomizer(threshold=0.1, parallel_processing=False)
            
            print("=" * 80)
            print("STEP 4: ATOMIZING MODEL")
            print("=" * 80)
            print(f"   Start time: {time.strftime('%H:%M:%S')}")
            print()

            # Atomize (limit to 2 tensors for quick validation)
            atomize_start = time.time()
            result = await atomizer.atomize_model(
                file_path=test_file,
                model_name="test-tinyllama-validation",
                conn=conn,
                max_tensors=2,  # Just validate pipeline works
                pool=pool,  # Enable parallel processing with connection pool
            )
            atomize_time = time.time() - atomize_start
            
            print()
            print("=" * 80)
            print("STEP 5: ATOMIZATION COMPLETE")
            print("=" * 80)
            print(f"   Total time: {atomize_time:.2f}s")
            print(f"   Model: {result['model_name']}")
            print(f"   Model atom ID: {result['model_atom_id']}")
            print(f"   File size: {result['file_size_gb']:.3f} GB")
            print()
            print("TENSOR PROCESSING:")
            print(f"   Tensors processed: {result['tensors_processed']}")
            print(f"   Total weights: {result['total_processed']:,}")
            print(f"   Atoms created: {result['atoms_created']:,}")
            print(f"   Sparse skipped: {result['sparse_skipped']:,}")
            print()
            print("EFFICIENCY METRICS:")
            print(f"   Deduplication: {result['deduplication_ratio']:.2f}x")
            print(f"   Sparse ratio: {result['sparse_percentage']:.2f}%")
            print(f"   Processing speed: {result['total_processed'] / atomize_time:.0f} weights/sec")
            print()
            
            # Query some atoms
            print("=" * 80)
            print("STEP 6: VALIDATING ATOMS IN DATABASE")
            print("=" * 80)
            
            query_start = time.time()
            async with conn.cursor() as cur:
                await cur.execute(
                    """
                    SELECT canonical_text, 
                           metadata->>'modality' as modality,
                           metadata->>'subtype' as subtype,
                           metadata->>'value' as weight_value
                    FROM atom
                    WHERE metadata->>'modality' = 'weight'
                    ORDER BY atom_id DESC
                    LIMIT 5
                """
                )
                
                rows = await cur.fetchall()
                query_time = time.time() - query_start
                
                print(f"   Query time: {query_time:.3f}s")
                print(f"   Sample atoms (most recent):")
                print()
                
                for i, row in enumerate(rows, 1):
                    text = row[0][:50] if row[0] else 'N/A'
                    modality = row[1]
                    value = row[3]
                    print(f"   {i}. {text}")
                    print(f"      Modality: {modality}, Value: {value}")
            
            print()
            print("=" * 80)
            print("✓ TEST COMPLETE - ALL STEPS SUCCESSFUL")
            print("=" * 80)
            total_time = time.time() - start_time
            print(f"Total execution time: {total_time:.2f}s")
            print(f"End time: {time.strftime('%H:%M:%S')}")
            print("=" * 80)
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
        raise
    finally:
        close_start = time.time()
        await pool.close()
        close_time = time.time() - close_start
        print(f"\n✓ Connection pool closed ({close_time:.3f}s)")


if __name__ == "__main__":
    print()
    print("█" * 80)
    print("█" + " " * 78 + "█")
    print("█" + " " * 25 + "GGUF MODEL ATOMIZATION TEST" + " " * 27 + "█")
    print("█" + " " * 78 + "█")
    print("█" * 80)
    print()
    print(f"Test started at: {time.strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"Python version: {sys.version}")
    print(f"Platform: {sys.platform}")
    print()

    # Fix for Windows ProactorEventLoop issue with psycopg
    if sys.platform == 'win32':
        print("⚙ Configuring Windows event loop policy for psycopg compatibility...")
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
        print("✓ Event loop configured")
        print()

    try:
        asyncio.run(test_gguf_atomization())
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
