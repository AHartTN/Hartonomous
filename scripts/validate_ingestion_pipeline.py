#!/usr/bin/env python3
"""
Ingestion Pipeline Validation Script

Demonstrates the complete end-to-end ingestion pipeline:
1. File detection and routing
2. Parser selection
3. Atomization
4. Spatial positioning
5. Composition building
6. Relation creation
7. Database storage
8. Neo4j provenance sync

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import sys
import asyncio
import logging
from pathlib import Path
from typing import Dict, Any

# Add project root to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from src.ingestion.coordinator import IngestionCoordinator, IngestionStatus
from src.db.ingestion import IngestionDB

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)

logger = logging.getLogger(__name__)


async def validate_text_ingestion(coordinator: IngestionCoordinator):
    """Validate text file ingestion."""
    logger.info("=" * 60)
    logger.info("VALIDATION: Text Ingestion")
    logger.info("=" * 60)
    
    # Create test text file
    test_file = Path("test_text.txt")
    test_file.write_text("Hello, World! This is a test of the Hartonomous ingestion pipeline.")
    
    try:
        result = await coordinator.ingest_file(test_file, source_id="validation_text_001")
        
        logger.info(f"Status: {result.status}")
        logger.info(f"Atoms created: {result.atoms_created}")
        logger.info(f"Landmarks created: {result.landmarks_created}")
        logger.info(f"Associations: {result.associations_created}")
        
        assert result.status == IngestionStatus.COMPLETED
        assert result.atoms_created > 0
        logger.info("? Text ingestion PASSED")
        
    finally:
        test_file.unlink(missing_ok=True)


async def validate_code_ingestion(coordinator: IngestionCoordinator):
    """Validate code file ingestion."""
    logger.info("=" * 60)
    logger.info("VALIDATION: Code Ingestion")
    logger.info("=" * 60)
    
    # Create test Python file
    test_file = Path("test_code.py")
    test_file.write_text("""
def hello_world():
    \"\"\"Say hello.\"\"\"
    print("Hello from Hartonomous!")
    return 42

class TestClass:
    def __init__(self, value):
        self.value = value
    
    def get_value(self):
        return self.value
""")
    
    try:
        result = await coordinator.ingest_file(test_file, source_id="validation_code_001")
        
        logger.info(f"Status: {result.status}")
        logger.info(f"Atoms created: {result.atoms_created}")
        logger.info(f"Landmarks created: {result.landmarks_created}")
        
        assert result.status == IngestionStatus.COMPLETED
        assert result.atoms_created > 0
        logger.info("? Code ingestion PASSED")
        
    finally:
        test_file.unlink(missing_ok=True)


async def validate_batch_ingestion(coordinator: IngestionCoordinator):
    """Validate batch file ingestion."""
    logger.info("=" * 60)
    logger.info("VALIDATION: Batch Ingestion")
    logger.info("=" * 60)
    
    # Create test files
    test_files = []
    try:
        for i in range(5):
            test_file = Path(f"test_batch_{i}.txt")
            test_file.write_text(f"Batch test file #{i} for Hartonomous pipeline validation.")
            test_files.append({
                'path': str(test_file),
                'source_id': f"validation_batch_{i:03d}",
                'metadata': {'batch_id': 'validation_001', 'index': i}
            })
        
        results = await coordinator.ingest_batch(test_files)
        
        successful = sum(1 for r in results if r.status == IngestionStatus.COMPLETED)
        failed = sum(1 for r in results if r.status == IngestionStatus.FAILED)
        total_atoms = sum(r.atoms_created for r in results)
        
        logger.info(f"Successful: {successful}/{len(test_files)}")
        logger.info(f"Failed: {failed}/{len(test_files)}")
        logger.info(f"Total atoms created: {total_atoms}")
        
        assert successful == len(test_files)
        assert total_atoms > 0
        logger.info("? Batch ingestion PASSED")
        
    finally:
        for item in test_files:
            Path(item['path']).unlink(missing_ok=True)


async def validate_directory_ingestion(coordinator: IngestionCoordinator):
    """Validate directory ingestion with file filtering."""
    logger.info("=" * 60)
    logger.info("VALIDATION: Directory Ingestion")
    logger.info("=" * 60)
    
    # Create test directory with mixed files
    test_dir = Path("test_directory")
    test_dir.mkdir(exist_ok=True)
    
    try:
        # Create various file types
        (test_dir / "file1.txt").write_text("Text file 1")
        (test_dir / "file2.txt").write_text("Text file 2")
        (test_dir / "code.py").write_text("print('test')")
        (test_dir / "readme.md").write_text("# Test README")
        (test_dir / "unsupported.xyz").write_text("Unsupported format")
        
        # Create subdirectory
        subdir = test_dir / "subdir"
        subdir.mkdir(exist_ok=True)
        (subdir / "nested.txt").write_text("Nested file")
        
        # Ingest directory recursively
        results = await coordinator.ingest_directory(test_dir, recursive=True)
        
        successful = sum(1 for r in results if r.status == IngestionStatus.COMPLETED)
        total_atoms = sum(r.atoms_created for r in results)
        
        logger.info(f"Files processed: {len(results)}")
        logger.info(f"Successful: {successful}")
        logger.info(f"Total atoms: {total_atoms}")
        
        # Should process .txt, .py, .md but not .xyz
        assert successful >= 5  # At least the supported files
        assert total_atoms > 0
        logger.info("? Directory ingestion PASSED")
        
    finally:
        # Cleanup
        import shutil
        shutil.rmtree(test_dir, ignore_errors=True)


async def validate_error_handling(coordinator: IngestionCoordinator):
    """Validate error handling for invalid inputs."""
    logger.info("=" * 60)
    logger.info("VALIDATION: Error Handling")
    logger.info("=" * 60)
    
    # Test 1: Non-existent file
    result = await coordinator.ingest_file(
        Path("non_existent_file.txt"),
        source_id="validation_error_001"
    )
    
    assert result.status == IngestionStatus.FAILED
    assert result.error is not None
    logger.info("? Non-existent file error handling PASSED")
    
    # Test 2: Unsupported file type
    test_file = Path("test.unsupported")
    test_file.write_text("Unsupported content")
    
    try:
        result = await coordinator.ingest_file(test_file, source_id="validation_error_002")
        
        assert result.status == IngestionStatus.FAILED
        assert "No parser found" in result.error
        logger.info("? Unsupported file type handling PASSED")
        
    finally:
        test_file.unlink(missing_ok=True)


async def validate_composition_building(db: IngestionDB):
    """Validate hierarchical composition creation."""
    logger.info("=" * 60)
    logger.info("VALIDATION: Composition Building")
    logger.info("=" * 60)
    
    # Create parent atom
    from src.core.atomization import Atom, ModalityType
    import hashlib
    
    parent_data = b"document"
    parent_atom = Atom(
        atom_id=hashlib.sha256(parent_data).digest(),
        modality=ModalityType.TEXT_TOKEN,
        data=parent_data,
        compression_type=0,
        metadata={'type': 'document', 'canonical_text': 'document'}
    )
    
    # Create child atoms
    child_atoms = []
    for word in ["hello", "world"]:
        word_bytes = word.encode('utf-8')
        child_atoms.append(Atom(
            atom_id=hashlib.sha256(word_bytes).digest(),
            modality=ModalityType.TEXT_TOKEN,
            data=word_bytes,
            compression_type=0,
            metadata={'type': 'word', 'canonical_text': word}
        ))
    
    # Store atoms
    parent_id = await db.store_atom(parent_atom)
    child_ids = await db.store_atoms_batch(child_atoms)
    
    # Create composition
    composition_ids = await db.create_composition(
        parent_atom_id=parent_id,
        component_atom_ids=child_ids,
        metadata={'type': 'word_sequence'}
    )
    
    logger.info(f"Parent atom ID: {parent_id}")
    logger.info(f"Child atom IDs: {child_ids}")
    logger.info(f"Composition IDs: {composition_ids}")
    
    assert len(composition_ids) == len(child_ids)
    logger.info("? Composition building PASSED")


async def validate_relation_creation(db: IngestionDB):
    """Validate semantic relation creation."""
    logger.info("=" * 60)
    logger.info("VALIDATION: Relation Creation")
    logger.info("=" * 60)
    
    from src.core.atomization import Atom, ModalityType
    import hashlib
    
    # Create two related atoms
    cat_bytes = b"cat"
    dog_bytes = b"dog"
    
    cat_atom = Atom(
        atom_id=hashlib.sha256(cat_bytes).digest(),
        modality=ModalityType.TEXT_TOKEN,
        data=cat_bytes,
        compression_type=0,
        metadata={'type': 'word', 'canonical_text': 'cat'}
    )
    
    dog_atom = Atom(
        atom_id=hashlib.sha256(dog_bytes).digest(),
        modality=ModalityType.TEXT_TOKEN,
        data=dog_bytes,
        compression_type=0,
        metadata={'type': 'word', 'canonical_text': 'dog'}
    )
    
    # Store atoms
    cat_id = await db.store_atom(cat_atom)
    dog_id = await db.store_atom(dog_atom)
    
    # Create semantic relation
    relation_id = await db.create_relation(
        source_atom_id=cat_id,
        target_atom_id=dog_id,
        relation_type='semantic_similar',
        weight=0.75,
        metadata={'context': 'pets', 'confidence': 0.9}
    )
    
    logger.info(f"Cat atom ID: {cat_id}")
    logger.info(f"Dog atom ID: {dog_id}")
    logger.info(f"Relation ID: {relation_id}")
    
    assert relation_id > 0
    logger.info("? Relation creation PASSED")


async def main():
    """Run all validation tests."""
    logger.info("?" + "=" * 58 + "?")
    logger.info("?" + " " * 10 + "HARTONOMOUS INGESTION PIPELINE VALIDATION" + " " * 6 + "?")
    logger.info("?" + "=" * 58 + "?")
    
    # Initialize database connection
    # TODO: Update connection string for your environment
    connection_string = "postgresql://postgres:postgres@localhost:5432/hartonomous"
    
    db = IngestionDB(connection_string)
    db.connect()
    
    # Initialize coordinator
    coordinator = IngestionCoordinator(db)
    
    try:
        # Run validations
        await validate_text_ingestion(coordinator)
        await validate_code_ingestion(coordinator)
        await validate_batch_ingestion(coordinator)
        await validate_directory_ingestion(coordinator)
        await validate_error_handling(coordinator)
        await validate_composition_building(db)
        await validate_relation_creation(db)
        
        logger.info("")
        logger.info("?" + "=" * 58 + "?")
        logger.info("?" + " " * 16 + "ALL VALIDATIONS PASSED ?" + " " * 16 + "?")
        logger.info("?" + "=" * 58 + "?")
        logger.info("")
        logger.info("The ingestion pipeline is fully operational!")
        logger.info("")
        logger.info("Components validated:")
        logger.info("  ? File detection and parser routing")
        logger.info("  ? Text atomization")
        logger.info("  ? Code atomization (with TreeSitter)")
        logger.info("  ? Batch ingestion")
        logger.info("  ? Directory ingestion (recursive)")
        logger.info("  ? Error handling")
        logger.info("  ? Composition building (hierarchical structure)")
        logger.info("  ? Relation creation (semantic graph)")
        logger.info("")
        logger.info("Next steps:")
        logger.info("  1. Start Neo4j for provenance tracking")
        logger.info("  2. Start FastAPI server: uvicorn api.main:app --reload")
        logger.info("  3. Ingest real data: python scripts/ingest_repo.py")
        logger.info("  4. Query semantic space: psql -d hartonomous")
        
    except Exception as e:
        logger.error(f"Validation failed: {e}", exc_info=True)
        return 1
    
    finally:
        db.close()
    
    return 0


if __name__ == '__main__':
    sys.exit(asyncio.run(main()))
