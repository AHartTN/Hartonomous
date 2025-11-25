from neo4j import GraphDatabase

# Test Neo4j connection
driver = GraphDatabase.driver('bolt://localhost:7687', auth=('neo4j', 'neo4jneo4j'))

try:
    # Verify connectivity
    driver.verify_connectivity()
    print("? Neo4j connection verified")
    
    # Test query
    with driver.session() as session:
        result = session.run('RETURN 1 as test')
        value = result.single()[0]
        print(f"? Neo4j query test: {value}")
        
        # Check if constraint exists
        result = session.run("""
            SHOW CONSTRAINTS
            YIELD name, type
            WHERE name = 'atom_id_unique'
            RETURN count(*) as count
        """)
        constraint_count = result.single()[0]
        print(f"? Atom ID constraint exists: {constraint_count > 0}")
        
finally:
    driver.close()
    print("? Driver closed")
