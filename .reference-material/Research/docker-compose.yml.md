\# Hartonomous Infrastructure Stack v1.1  
\# This stack is designed for production-grade local development and testing.  
\# NOTE: The top-level 'version' key has been removed as it is deprecated  
\# in modern Docker Compose and handled automatically by the Docker engine.

services:  
  zookeeper:  
    image: confluentinc/cp-zookeeper:7.3.0  
    container\_name: zookeeper  
    environment:  
      ZOOKEEPER\_CLIENT\_PORT: 2181  
      ZOOKEEPER\_TICK\_TIME: 2000

  kafka:  
    image: confluentinc/cp-kafka:7.3.0  
    container\_name: kafka  
    depends\_on:  
      \- zookeeper  
    ports:  
      \- "9092:9092"  
    environment:  
      KAFKA\_BROKER\_ID: 1  
      KAFKA\_ZOOKEEPER\_CONNECT: 'zookeeper:2181'  
      KAFKA\_LISTENER\_SECURITY\_PROTOCOL\_MAP: PLAINTEXT:PLAINTEXT,PLAINTEXT\_INTERNAL:PLAINTEXT  
      KAFKA\_ADVERTISED\_LISTENERS: PLAINTEXT://localhost:9092,PLAINTEXT\_INTERNAL://kafka:29092  
      KAFKA\_OFFSETS\_TOPIC\_REPLICATION\_FACTOR: 1  
      KAFKA\_TRANSACTION\_STATE\_LOG\_MIN\_ISR: 1  
      KAFKA\_TRANSACTION\_STATE\_LOG\_REPLICATION\_FACTOR: 1

  sqlserver:  
    image: mcr.microsoft.com/mssql/server:2019-latest  
    container\_name: sqlserver  
    ports:  
      \- "1433:1433"  
    environment:  
      ACCEPT\_EULA: "${SQL\_SERVER\_ACCEPT\_EULA}"  
      SA\_PASSWORD: "${SQL\_SERVER\_SA\_PASSWORD}"  
    volumes:  
      \- sqlserver\_data:/var/opt/mssql

  connect:  
    image: debezium/connect:2.1  
    container\_name: connect  
    ports:  
      \- "8083:8083"  
    depends\_on:  
      \- kafka  
      \- sqlserver  
    environment:  
      BOOTSTRAP\_SERVERS: 'kafka:9092'  
      GROUP\_ID: 1  
      CONFIG\_STORAGE\_TOPIC: my\_connect\_configs  
      OFFSET\_STORAGE\_TOPIC: my\_connect\_offsets  
      STATUS\_STORAGE\_TOPIC: my\_connect\_statuses

  neo4j:  
    image: neo4j:5.5.0  
    container\_name: neo4j  
    ports:  
      \- "7474:7474"  
      \- "7687:7687"  
    environment:  
      NEO4J\_AUTH: "${NEO4J\_USER}/${NEO4J\_PASSWORD}"  
    volumes:  
      \- neo4j\_data:/data

volumes:  
  sqlserver\_data:  
  neo4j\_data:

