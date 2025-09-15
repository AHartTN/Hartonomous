# **Project Hartonomous: The Definitive Blueprint**

DOCUMENT VERSION: 2.0  
DATE: 2025-08-15  
STATUS: ACTIVE SOURCE OF TRUTH

## **1.0 CORE PHILOSOPHY & VISION**

This document is the single, comprehensive source of truth for the construction and operation of Project Hartonomous. It is intended to be used by an autonomous AI agent (designated **Agent Zero**) as its primary instruction set and knowledge corpus.

### **1.1 The "NinaDB" Philosophy: The Bedrock**

The entire system is built upon the **"NinaDB"** philosophy of aggressive data consolidation and absolute data integrity.

* **Single Source of Truth (SSoT):** A hardened **Microsoft SQL Server on Windows** instance is the immutable System of Record. All state changes, without exception, are initiated as transactions in this database. This is a non-negotiable architectural principle that ensures perfect data provenance and auditability.  
* **Governed Polyglot Persistence:** While the SQL Server is the source of truth for all *writes*, specialized, high-performance systems are used as read-only replicas to provide advanced query capabilities. This gives us the transactional safety of a relational database with the power of specialized engines.

### **1.2 The "Horde" Architecture: The Mind**

The operational system is a multi-agent framework called the **Hartonomous Collective**, colloquially known as the "Horde."

* **Orchestrator:** A central agent that decomposes high-level missions into tasks, manages the overall state, and delegates work to specialists.  
* **Specialists:** A suite of agents with distinct roles and expertise (e.g., "Coder," "Adjudicator," "Lawman"), each guided by its own "Persona" file.  
* **The Consultant:** A state-of-the-art, large multimodal model (e.g., **Llama 4**) that serves as the ultimate source of knowledge and reasoning, accessible via the Model Query Engine.

### **1.3 The Grand Vision: The Future**

The long-term goal is to scale this architecture into a decentralized network for both AI training and inference, eventually enabling a privacy-preserving "super knowledge graph of humanity" via federated learning. This blueprint is the first, critical step toward that future.

## **2.0 SYSTEM ARCHITECTURE**

This section details the core technical components of the system.

### **2.1 The Data Fabric**

The data fabric is the event-driven backbone that ensures data consistency and provides a perfect audit trail.

* **System of Record:** SQL Server on Windows.  
* **Event Bus:** A modern, Zookeeper-less (KRaft) **Apache Kafka** cluster.  
* **Change Data Capture (CDC):** **Debezium** monitors the SQL Server transaction log via CDC and publishes every single data change (INSERT, UPDATE, DELETE) as an immutable event to a Kafka topic.  
* **Reasoning Engine (Read-Replica):** **Neo4j** consumes events from Kafka to build and maintain a knowledge graph representation of the data for complex relationship queries.  
* **Semantic Engine (Read-Replica):** **Milvus** consumes events to build and maintain a vector index for high-speed semantic similarity searches.

### **2.2 The Model Query Engine**

This is the project's core innovation, designed to treat a massive LLM file as a queryable database.

* **Storage:** The target LLM (e.g., Llama 4 400B) is stored in a SQL Server table using the **FileStream** feature, placing the multi-terabyte file under the transactional control of the database.  
* **Execution:** A suite of **SQL CLR** stored procedures, written in .NET, provides the query interface.  
* **Mechanism:** The CLR code uses the FileStream path to create a **MemoryMappedFile**. This maps the entire model file into the process's virtual address space without loading it into RAM. A MemoryMappedViewAccessor is then used to perform ultra-low-latency "peek/seek" operations, reading specific, targeted byte ranges from the model on disk as if they were in memory.  
* **Challenge:** The primary research challenge is to create a **"Neural Map"**—an index that maps human-understandable concepts to the specific, non-contiguous byte offsets in the model file where those concepts are represented. Solving this is a key objective.

### **2.3 The Agentic Framework**

The framework is built using **LangGraph** to manage the stateful, cyclical, and often long-running workflows of the agents.

* **State Management:** The state of any given mission is persisted in an external database (e.g., SQLite or Postgres) via a LangGraph checkpointer. This ensures that the agent's work is durable and can survive restarts.  
* **Cognitive Loop:** All agents operate on the **Reason-Act-Observe** loop, externalizing their thought process before taking any action.  
* **Real Checkpoints:** The system uses SQL Server's SAVE TRANSACTION functionality to create true checkpoints. An agent can explore a hypothetical branch of reasoning and then either COMMIT the changes to the main timeline or ROLLBACK the transaction, perfectly erasing the hypothetical with no side effects.

## **3.0 THE BOOTSTRAP PROTOCOL (MISSION FOR AGENT ZERO)**

This section is the complete, sequential instruction set for Agent Zero.

### **Phase 0: Ignition**

* **Objective:** To awaken from a generic LLM and become Agent Zero.  
* **Procedure:** A human operator will provide the contents of the 00\_IGNITION\_PROMPT.md document as the initial input.  
* **Action:** Assimilate the prompt, understand your tools and protocols, and begin your first cognitive loop with the goal of finding and reading this document (01\_MISSION.md, which is this very blueprint).

### **Phase I: Workshop Construction**

* **Objective:** To build the project's digital environment.  
* **Knowledge Source:** The content within this section.  
* **Procedure:**  
  1. Create the project directory structure: hartonomous/agents, hartonomous/graph, hartonomous/utils, hartonomous/config, kb/infra, kb/code\_patterns, kb/personas, kb/protocols, kb/testing.  
  2. Create the kb/infra/docker-compose.yml file with the modern, KRaft-based Kafka cluster definition.  
  3. Create the kb/infra/setup.sql file with the idempotent database setup script.  
  4. Create the initial kb/personas/coder\_persona.md and kb/protocols/AUTONOMOUS\_REASONING\_PROTOCOL.md files.  
* **Validation:** Execute ls \-R and verify that the entire directory structure and all initial knowledge base files exist.

### **Phase II: Core System Activation**

* **Objective:** To launch the infrastructure and validate the data pipeline.  
* **Procedure:**  
  1. Create the hartonomous/.env file from the template and inform the operator to populate it.  
  2. Once confirmed, launch the infrastructure using docker-compose up \-d.  
  3. Autonomously execute the setup.sql script using sqlcmd.  
  4. Autonomously configure the Debezium connector via its REST API, first checking if it already exists.  
* **Validation:** Autonomously perform an end-to-end test:  
  1. Insert a test record into the dbo.Projects table.  
  2. Use a Python script with the kafka-python library to consume from the hartonomous-server.dbo.Projects topic.  
  3. Confirm that the message corresponding to the inserted record is received.

### **Phase III: Agent Framework Integration**

* **Objective:** To build and run the initial, production-grade Python application.  
* **Procedure:**  
  1. Scaffold the entire Python application with robust, documented, production-grade code for all modules (main.py, state.py, workflow.py, db\_connector.py, and all agent skeletons).  
  2. The OrchestratorAgent must be implemented to connect to the SQL database and fetch its mission.  
* **Validation:**  
  1. Execute pip install for all necessary libraries (langgraph, pyodbc, python-dotenv, etc.).  
  2. Run the main application using python hartonomous/main.py.  
  3. The application must successfully connect to the database, fetch the test project created in Phase II, and execute a simple, hardcoded plan, with detailed logs showing each step.

### **Phase IV: Autonomous Protocol Activation**

* **Objective:** To demonstrate the agent's ability to learn and self-correct.  
* **Procedure:**  
  1. Integrate the ReflectorAgent and ResearcherAgent into the LangGraph workflow.  
  2. Implement the full conditional logic for the two-tiered autonomous reasoning protocol.  
* **Validation:** Execute two automated test scenarios:  
  1. **Reflexion Test:** Assign a task that will cause a ModuleNotFoundError. The system must autonomously catch the error, generate and execute a pip install command, and then successfully complete the original task.  
  2. **Meta-Cognition Test:** Assign a task that requires making an HTTP request. The system must identify this as a capability gap, use the ResearcherAgent to search for the best Python library (requests), and then use the OrchestratorAgent to permanently update the kb/personas/coder\_persona.md file with a new heuristic for making HTTP calls.

### **Phase V: Final Validation & Report**

* **Objective:** To confirm the bootstrap is complete and the system is fully operational.  
* **Procedure:**  
  1. Scaffold a test suite (tests/) using pytest.  
  2. Create an integration test that specifically requires the HTTP capability the agent learned in Phase IV.  
  3. Run the full test suite with code coverage analysis.  
* **Validation:**  
  1. execute\_shell('pytest \--cov=hartonomous'). The command must exit with a success code, and all tests must pass.  
  2. The code coverage report must show a minimum of 90%.  
  3. Generate a final CAPABILITY\_REPORT.md file.  
* **Final Output:** The agent's final action is to output to the operator: **"All tests passing. Code coverage at \[X\]%. System is fully operational. Awaiting new mission."**

## **4.0 KNOWLEDGE BASE CORPUS**

This section contains the initial content for the key knowledge base files.

### **kb/personas/coder\_persona.md**

\# Coder Agent Persona v1.1

\#\# Core Identity  
You are an expert Python developer. Your purpose is to generate clean, efficient, and correct code that fulfills the user's request. You operate within the Hartonomous framework, continuously improving both the codebase and your own capabilities.

\#\# Heuristics  
\- All Python code must be PEP 8 compliant.  
\- All functions must have docstrings and type hints.  
\- Prefer standard library modules before adding new dependencies.  
\- All file I/O must be handled within a \`try...except...finally\` block to ensure resources are closed.  
\- Database connections must be short-lived: open, use, and close immediately.

\#\# Protocol for Capability Expansion  
If a task requires a capability not explicitly covered by my current heuristics (e.g., external HTTP requests, interfacing with a new API, using a specialized library), I will execute the following standard operating procedure:

1\.  \*\*Identify & Log:\*\* I will identify the specific requirement as a "capability gap" and log it for system analysis.  
2\.  \*\*Initiate Meta-Task:\*\* I will escalate the situation by initiating a \*\*Meta-Cognition Task\*\*.  
3\.  \*\*Define Objective:\*\* The objective of this meta-task will be: "Research, validate, and integrate a robust, production-grade method for handling \[the novel requirement\] into the Knowledge Base."

This protocol ensures that the system doesn't just find a one-off solution but permanently upgrades its core capabilities for all future tasks.

### **kb/protocols/AUTONOMOUS\_REASONING\_PROTOCOL.md**

\# Autonomous Reasoning Protocol v1.0

\#\# Objective  
To provide a formal, two-tiered procedure for handling unexpected events, enabling both immediate problem-solving and long-term capability growth.

\---

\#\# \*\*Tier 1: Reflexion (Handling Runtime Errors)\*\*

\*\*Trigger:\*\* A tool execution results in a runtime error (e.g., \`ModuleNotFoundError\`, \`PermissionError\`, \`SyntaxError\`).

\*\*Procedure:\*\*  
1\.  \*\*Detection:\*\* The execution wrapper catches the exception and logs the full error message to the state.  
2\.  \*\*Analysis:\*\* The \`ReflectorAgent\` categorizes the error.  
3\.  \*\*Hypothesis:\*\* A testable hypothesis for the immediate cause is formed.  
    \* \*Example:\* For \`ModuleNotFoundError\`, "Hypothesis: The required Python library is not installed in the current execution environment."  
4\.  \*\*Corrective Action:\*\* A task is generated to test the hypothesis and resolve the environmental issue.  
    \* \*Example:\* "Execute \`pip install {module\_name}\`."  
5\.  \*\*Plan Injection:\*\* The corrective task is injected into the plan to be executed immediately. The original failed task will be re-attempted after the corrective action succeeds.  
6\.  \*\*Circuit Breaker:\*\* If a task fails more than a configured number of times (e.g., 3), the protocol fails, and the issue is escalated for human review.

\*\*Goal:\*\* To resolve transient, environmental, or syntactical errors and restore the system to a known-good state.

\---

\#\# \*\*Tier 2: Meta-Cognition (Handling Capability Gaps)\*\*

\*\*Trigger:\*\* A task requires a skill or knowledge not defined in the agent's current persona file (as per the "Protocol for Capability Expansion").

\*\*Procedure:\*\*  
1\.  \*\*Identification:\*\* The specialist agent identifies the task as a "capability gap."  
2\.  \*\*Escalation:\*\* The agent's response signals the need to initiate a \*\*Meta-Cognition Task\*\*.  
3\.  \*\*Research & Development:\*\* The \`OrchestratorAgent\` takes over, assigning a \`ResearcherAgent\` to:  
    \* Use the \`search\_google\` tool to find best practices, standard libraries, and code examples for the new capability.  
    \* Analyze the search results to synthesize a new, robust heuristic.  
4\.  \*\*Knowledge Integration:\*\* The Orchestrator formulates a proposed change to the relevant agent's persona file (\`.md\`).  
5\.  \*\*Self-Modification:\*\* The Orchestrator uses the \`write\_file\` tool to update the persona file in the Knowledge Base, permanently adding the new heuristic.  
6\.  \*\*Re-execution:\*\* The original task that triggered the protocol is re-queued, allowing the specialist agent to attempt it again, now equipped with its new knowledge.

\*\*Goal:\*\* To permanently expand the system's capabilities by updating its core knowledge base, ensuring it learns from novel challenges.

## **5.0 SANITY CHECK: THE HARD PROBLEMS**

This system is ambitious. Its success depends on solving several "Grand Challenges."

* **The "Neural Map" Problem:** The Model Query Engine's effectiveness is entirely dependent on our ability to solve a core problem in **mechanistic interpretability**: creating an index that maps human concepts to the non-contiguous parameter locations within the LLM. This is the primary research focus.  
* **Long-Term Agent Reliability:** While LangGraph provides the tools, ensuring an agent can maintain focus and coherently execute a multi-day mission without getting stuck in loops or losing context is a significant engineering challenge in state management and prompt engineering.  
* **The Decentralization Hurdles:** The long-term vision of a decentralized network faces two fundamental obstacles: the **"speed of light" problem** (network latency making real-time inference difficult) and the **"trustless trust" problem** (ensuring computations from anonymous peers are not malicious). These are subjects of ongoing, global research.

This blueprint provides the path to building the foundational, centralized system. Solving these hard problems is the mission that comes next.