from neo4j import GraphDatabase

driver = GraphDatabase.driver('bolt://localhost:7687', auth=('neo4j', 'neo4jneo4j'))

print("\n=== NEO4J ATOM INSPECTION ===\n")

with driver.session() as session:
    # Get all atoms with ALL properties
    result = session.run('MATCH (n:Atom) RETURN n LIMIT 10')
    
    print("Atoms in Neo4j:")
    for i, record in enumerate(result, 1):
        atom = record['n']
        print(f"\nAtom {i}:")
        for key, value in atom.items():
            if len(str(value)) > 50:
                print(f"  {key}: {str(value)[:50]}...")
            else:
                print(f"  {key}: {value}")
    
    # Count total
    result = session.run('MATCH (n:Atom) RETURN count(n) as count')
    count = result.single()['count']
    print(f"\nTotal: {count} atoms")
    
    # Check relationships
    result = session.run('MATCH ()-[r:DERIVED_FROM]->() RETURN count(r) as count')
    rel_count = result.single()['count']
    print(f"Relationships: {rel_count}")

driver.close()
