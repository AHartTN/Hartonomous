# Architecture

This document provides a high-level overview of the Hartonomous system architecture. The system is designed as a hybrid, multi-database platform that leverages the strengths of different technologies to create a highly efficient, scalable, and auditable knowledge system.

## Core Principles

1.  **Atomicity & Composability**: The fundamental design principle is that all data, regardless of its type (text, image, number, etc.), is broken down into indivisible, content-addressable units called "atoms." Complex data structures are then built up as "compositions" of these atoms, creating a hierarchical and highly deduplicated knowledge graph.
2.  **In-Database Intelligence**: As much processing logic as possible is pushed directly into the database layer. This includes data transformation (atomization), relationship management, and even machine learning inference (via `PL/Python` and specialized SQL functions). This minimizes data movement and leverages the power and transactional integrity of the database.
3.  **Separation of Concerns**: The system separates the core data storage and logic (PostgreSQL) from the a-priori knowledge and reasoning layer (Neo4j), allowing each component to be optimized for its specific task.

## System Components

The architecture consists of three main components:

1.  **PostgreSQL Database**: The primary data store.
    *   **Core Tables**: `atoms`, `atom_composition`, and `atom_relation` form the core schema.
    *   **Spatial Indexing (PostGIS)**: Used unconventionally to create a semantic space. High-dimensional embeddings are projected down to 3D and stored as `POINTZ` types, with an R-tree index providing fast nearest-neighbor (semantic similarity) search capabilities.
    *   **Procedural Logic (`PL/pgSQL`, `PL/Python`)**: Contains the functions for atomizing data (`atomize_*`), performing search/inference (`compute_attention`), and managing the database logic.
2.  **FastAPI Application**: A lightweight Python web server that acts as the main entry point for the system.
    *   **Responsibilities**: Handles user authentication, request validation, pre-processing of incoming data (e.g., extracting frames from video), and orchestrating calls to the PostgreSQL functions. It serves as a thin API layer, not a monolith.
3.  **Neo4j Graph Database**: Acts as a dedicated, real-time provenance tracker.
    *   **Responsibilities**: Stores the "story" of how data is created and linked. It receives updates asynchronously from PostgreSQL via a message-passing mechanism (`LISTEN/NOTIFY`). This allows for complex graph traversals and lineage analysis without impacting the performance of the primary database.

## Data Flow: Ingestion and "Atomization"

1.  A user submits data (e.g., a text document) via the FastAPI endpoint.
2.  The FastAPI service performs any necessary pre-processing.
3.  It then calls a stored procedure in PostgreSQL (e.g., `atomize_text`).
4.  The `atomize_text` function breaks the text down into its constituent atoms (characters). For each unique character, it calls `atomize_value`.
5.  `atomize_value` calculates the hash of the character, checks if an atom with that hash already exists, and either returns the existing `atom_id` or creates a new one. This is the core of the content-addressable storage.
6.  The `atomize_text` function then creates a new "composition" atom representing the full text, linking it to the character atoms via the `atom_composition` table.
7.  The database triggers, upon seeing these new rows, send `NOTIFY` messages.
8.  The `Neo4jProvenanceWorker` receives these messages and asynchronously creates the corresponding nodes and `:DERIVED_FROM` relationships in the Neo4j graph.

This entire process is transactional and highly efficient, leveraging the database for a task usually handled in application memory.