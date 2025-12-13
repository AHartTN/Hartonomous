"""
Export/import utilities for Hartonomous atom data.
Supports portable JSON format for atom transfer between instances.
"""

import json
import psycopg2
from psycopg2.extras import RealDictCursor
import os
from typing import List, Dict, Optional
from datetime import datetime

class AtomExporter:
    """Export atoms to portable JSON format."""
    
    def __init__(self, conn):
        self.conn = conn
    
    def export_atoms(self, output_file: str, 
                    where_clause: str = "",
                    batch_size: int = 1000):
        """
        Export atoms to JSON file.
        
        Args:
            output_file: Target JSON file path
            where_clause: Optional SQL WHERE clause to filter atoms
            batch_size: Number of atoms per batch
        """
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        # Count total atoms
        count_sql = f"SELECT COUNT(*) as cnt FROM atom"
        if where_clause:
            count_sql += f" WHERE {where_clause}"
        
        cur.execute(count_sql)
        total = cur.fetchone()['cnt']
        
        print(f"📤 Exporting {total} atoms to {output_file}")
        
        # Build export query
        export_sql = f"""
            SELECT 
                atom_hash,
                atom_class,
                modality,
                ST_X(geom) as x,
                ST_Y(geom) as y,
                ST_Z(geom) as z,
                ST_M(geom) as m,
                atomic_value,
                metadata,
                constituents,
                created_at
            FROM atom
        """
        if where_clause:
            export_sql += f" WHERE {where_clause}"
        export_sql += " ORDER BY created_at"
        
        cur.execute(export_sql)
        
        # Stream to file in batches
        exported = 0
        with open(output_file, 'w') as f:
            f.write('{"atoms": [\n')
            
            first = True
            while True:
                batch = cur.fetchmany(batch_size)
                if not batch:
                    break
                
                for row in batch:
                    if not first:
                        f.write(',\n')
                    first = False
                    
                    # Convert to serializable format
                    atom_dict = dict(row)
                    if atom_dict['created_at']:
                        atom_dict['created_at'] = atom_dict['created_at'].isoformat()
                    
                    # Convert bytea to hex string
                    if atom_dict['atomic_value'] and isinstance(atom_dict['atomic_value'], bytes):
                        atom_dict['atomic_value'] = atom_dict['atomic_value'].hex()
                    
                    json.dump(atom_dict, f, indent=2)
                    exported += 1
                
                print(f"   Progress: {exported}/{total} ({exported/total*100:.1f}%)", end='\r')
            
            f.write('\n],\n')
            f.write(f'"metadata": {{\n')
            f.write(f'  "exported_at": "{datetime.now().isoformat()}",\n')
            f.write(f'  "total_atoms": {exported},\n')
            f.write(f'  "source_db": "hartonomous"\n')
            f.write('}\n}\n')
        
        cur.close()
        print(f"\n✅ Export completed: {exported} atoms")

class AtomImporter:
    """Import atoms from portable JSON format."""
    
    def __init__(self, conn):
        self.conn = conn
    
    def import_atoms(self, input_file: str, 
                    mode: str = "skip",
                    batch_size: int = 100):
        """
        Import atoms from JSON file.
        
        Args:
            input_file: Source JSON file
            mode: Conflict resolution ('skip', 'replace', 'error')
            batch_size: Atoms per transaction batch
        """
        if not os.path.exists(input_file):
            raise FileNotFoundError(f"Import file not found: {input_file}")
        
        print(f"📥 Importing atoms from {input_file}")
        
        with open(input_file, 'r') as f:
            data = json.load(f)
        
        atoms = data['atoms']
        metadata = data.get('metadata', {})
        
        print(f"   Total atoms in file: {len(atoms)}")
        print(f"   Exported at: {metadata.get('exported_at', 'unknown')}")
        print(f"   Conflict mode: {mode}")
        
        cur = self.conn.cursor()
        
        imported = 0
        skipped = 0
        errors = 0
        
        # Process in batches
        for i in range(0, len(atoms), batch_size):
            batch = atoms[i:i+batch_size]
            
            try:
                for atom in batch:
                    # Build INSERT statement based on mode
                    if mode == "skip":
                        sql = """
                            INSERT INTO atom (
                                atom_hash, atom_class, modality, geom,
                                atomic_value, metadata, constituents
                            ) VALUES (
                                %s, %s, %s, 
                                ST_SetSRID(ST_MakePointZM(%s, %s, %s, %s), 4326),
                                %s, %s, %s
                            )
                            ON CONFLICT (atom_hash) DO NOTHING
                        """
                    elif mode == "replace":
                        sql = """
                            INSERT INTO atom (
                                atom_hash, atom_class, modality, geom,
                                atomic_value, metadata, constituents
                            ) VALUES (
                                %s, %s, %s,
                                ST_SetSRID(ST_MakePointZM(%s, %s, %s, %s), 4326),
                                %s, %s, %s
                            )
                            ON CONFLICT (atom_hash) DO UPDATE SET
                                geom = EXCLUDED.geom,
                                metadata = EXCLUDED.metadata
                        """
                    else:  # error mode
                        sql = """
                            INSERT INTO atom (
                                atom_hash, atom_class, modality, geom,
                                atomic_value, metadata, constituents
                            ) VALUES (
                                %s, %s, %s,
                                ST_SetSRID(ST_MakePointZM(%s, %s, %s, %s), 4326),
                                %s, %s, %s
                            )
                        """
                    
                    # Convert hex string back to bytea
                    atomic_value = atom.get('atomic_value')
                    if atomic_value and isinstance(atomic_value, str):
                        atomic_value = bytes.fromhex(atomic_value)
                    
                    params = (
                        atom['atom_hash'],
                        atom['atom_class'],
                        atom['modality'],
                        atom['x'], atom['y'], atom['z'], atom['m'],
                        atomic_value,
                        json.dumps(atom.get('metadata')) if atom.get('metadata') else None,
                        atom.get('constituents')
                    )
                    
                    cur.execute(sql, params)
                    imported += 1
                
                self.conn.commit()
                print(f"   Progress: {imported}/{len(atoms)} ({imported/len(atoms)*100:.1f}%)", end='\r')
                
            except Exception as e:
                self.conn.rollback()
                errors += 1
                print(f"\n⚠️  Batch error: {e}")
                if mode == "error":
                    raise
        
        cur.close()
        print(f"\n✅ Import completed: {imported} atoms imported, {skipped} skipped, {errors} errors")

def main():
    """Demo export/import workflow."""
    import os
    import argparse
    
    parser = argparse.ArgumentParser(description="Export/import Hartonomous atoms")
    parser.add_argument('action', choices=['export', 'import'], help="Action to perform")
    parser.add_argument('file', help="JSON file path")
    parser.add_argument('--where', help="WHERE clause for export filter")
    parser.add_argument('--mode', choices=['skip', 'replace', 'error'], default='skip',
                       help="Import conflict mode")
    
    args = parser.parse_args()
    
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    if args.action == 'export':
        exporter = AtomExporter(conn)
        exporter.export_atoms(args.file, where_clause=args.where or "")
    else:
        importer = AtomImporter(conn)
        importer.import_atoms(args.file, mode=args.mode)
    
    conn.close()

if __name__ == "__main__":
    main()
