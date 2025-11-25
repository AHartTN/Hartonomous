from neo4j import GraphDatabase

driver = GraphDatabase.driver('bolt://localhost:7687', auth=('neo4j', 'neo4jneo4j'))

print("\n=== NEO4J PROVENANCE GRAPH ===\n")

with driver.session() as session:
    # Count atoms
    result = session.run('MATCH (n:Atom) RETURN count(n) as count')
    count = result.single()['count']
    print(f"Total atoms: {count}")
    
    # List all atoms
    result = session.run('''
        MATCH (n:Atom)
        RETURN n.atom_id as id, n.canonical_text as text, n.content_hash as hash
        ORDER BY n.atom_id
    ''')
    
    print("\nAtoms:")
    for record in result:
        text = record['text'] if record['text'] else '<no text>'
        hash_short = record['hash'][:16] if record['hash'] else 'N/A'
        print(f"  Atom {record['id']}: '{text}' (hash: {hash_short}...)")
    
    # Check for relationships
    result = session.run('''
        MATCH (parent:Atom)-[r:DERIVED_FROM]->(child:Atom)
        RETURN parent.atom_id as parent_id, 
               parent.canonical_text as parent_text,
               child.atom_id as child_id,
               child.canonical_text as child_text,
               r.position as position
        ORDER BY parent_id, position
    ''')
    
    relationships = list(result)
    print(f"\nProvenance relationships: {len(relationships)}")
    
    if relationships:
        print("\nProvenance chain:")
        for record in relationships:
            parent_text = record['parent_text'] if record['parent_text'] else f"Atom {record['parent_id']}"
            child_text = record['child_text'] if record['child_text'] else f"Atom {record['child_id']}"
            print(f"  '{parent_text}' ? (pos {record['position']}) ? '{child_text}'")

driver.close()

print("\n? Neo4j provenance verification complete")
print("\nTo visualize in Neo4j Browser (http://localhost:7474), run:")
print("  MATCH path = (parent:Atom)-[:DERIVED_FROM]->(child:Atom)")
print("  RETURN path")
