"""
Test script for GGUF model atomization.

Run this to test atomizing your Ollama models in D:/Models
"""

import asyncio
import sys
from pathlib import Path

# Add parent directory to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from api.services.model_atomization import GGUFAtomizer
from psycopg import AsyncConnection
from api.config import settings


async def test_gguf_atomization():
    """Test atomizing a GGUF model with quick validation."""

    # Connect to database
    conn = await AsyncConnection.connect(settings.get_connection_string())

    try:
        # Use small test model
        test_file = Path(".cache/test_models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf")
        
        if not test_file.exists():
            print(f"Test model not found: {test_file}")
            print("Downloading TinyLlama-1.1B (~637MB)...")
            test_file.parent.mkdir(parents=True, exist_ok=True)
            import urllib.request
            urllib.request.urlretrieve(
                "https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/resolve/main/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf",
                test_file
            )
            print("✓ Download complete")
        
        print(f"Using test GGUF: {test_file.name} ({test_file.stat().st_size / 1e6:.0f} MB)")
        print("Note: This is a quick validation test (2 tensors only)")
        print("For full ingestion, use: scripts/ingest_model.py")

        # Create atomizer (with higher threshold for testing)
        atomizer = GGUFAtomizer(threshold=0.1)

        # Atomize (limit to 2 tensors for quick validation)
        result = await atomizer.atomize_model(
            file_path=test_file,
            model_name="test-embedding-model",
            conn=conn,
            max_tensors=2,  # Just validate pipeline works
        )

        print("\n? Atomization complete!")
        print(f"   Model: {result['model_name']}")
        print(f"   Tensors processed: {result['tensors_processed']}")
        print(f"   Total weights: {result['total_processed']:,}")
        print(f"   Total atoms: {result['atoms_created']:,}")
        print(f"   Sparse skipped: {result['sparse_skipped']:,}")
        print(f"   Deduplication: {result['deduplication_ratio']:.1f}x")
        print(f"   Sparse: {result['sparse_percentage']:.1f}%")

        # Query some atoms
        print("\n?? Sample atoms:")
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT canonical_text, 
                       metadata->>'modality' as modality,
                       metadata->>'subtype' as subtype,
                       metadata->>'value' as weight_value
                FROM atom
                WHERE metadata->>'modality' = 'weight'
                LIMIT 5
            """
            )

            async for row in cur:
                print(f"   - {row[0][:50] if row[0] else 'N/A'} (modality={row[1]}, value={row[3]})")

    finally:
        await conn.close()


if __name__ == "__main__":
    print("=" * 60)
    print("GGUF Model Atomization Test")
    print("=" * 60)

    # Fix for Windows ProactorEventLoop issue with psycopg
    import sys
    if sys.platform == 'win32':
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

    asyncio.run(test_gguf_atomization())
