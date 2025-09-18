

# **Functional Blueprint for the Hartonomous Distillation Engine**

## **Part I: System Architecture and Operational Overview**

### **1.1. The Hartonomous Distillation Engine: A Conceptual Framework**

The Hartonomous Distillation Engine is conceived as a comprehensive system for the systematic creation of specialized, high-performance AI agents derived from large, general-purpose foundation models. The primary operational constraint is its deployment on consumer-grade hardware, characterized by limited GPU VRAM and substantial system RAM. This blueprint moves beyond conventional, opaque fine-tuning methodologies, which often treat the model as a black box, and instead establishes a repeatable, mechanistic process rooted in the principles of mechanistic interpretability (MI). The engine's core purpose is to deconstruct a "teacher" foundation model, understand its internal computational algorithms, and surgically extract only the necessary components to create a smaller, more efficient, and domain-specific "student" agent.  
The system's operation is defined by a three-phase cycle, designed to be iterative and semi-automated:

1. **Analysis Phase:** This initial phase involves a deep, introspective analysis of the teacher foundation model. A domain-specific dataset is processed by the model, and its internal states—specifically, the activation patterns within its neural layers—are captured at scale. These activations are then decomposed into sparse, human-interpretable features using advanced MI techniques. The causal relationships between these features are mapped to reverse-engineer the computational "circuits" the model uses to perform tasks relevant to the target domain, such as logical reasoning or factual recall.1  
2. **Distillation Phase:** This phase redefines knowledge distillation. Instead of training a student model to mimic the teacher's output probabilities, the Hartonomous engine performs a direct, structural transfer of knowledge. The circuits identified in the Analysis Phase as critical for the target domain form a "knowledge scaffold." The engine then uses attribution-guided pruning techniques to surgically remove all parameters from the teacher model that do not contribute to this scaffold. The result is not merely a compressed model, but a functionally specialized one, where irrelevant capabilities have been excised at the parameter level.3  
3. **Execution Phase:** The final, distilled model is quantized into a highly efficient runtime format (GGUF) and deployed within a C\#-native execution environment. This phase focuses on providing a robust, high-performance interface for interacting with the specialized agent, complete with features for managing conversational context and state, all while operating within the constraints of consumer hardware.5

This cyclical process enables a new paradigm of AI specialization. By understanding and manipulating the internal mechanisms of a model, the engine allows for the creation of agents that are not only smaller and faster but also more robust and auditable, as their capabilities are a direct result of the specific, identified circuits they contain.

### **1.2. Architectural Blueprint: Components and Data Flow**

The architecture of the Hartonomous Distillation Engine is composed of four primary, interconnected components, orchestrated to execute the three-phase operational cycle. The system is designed with a C\#-first implementation philosophy, leveraging external tools or languages only when a significant performance or capability advantage is present.  
The primary components are:

* **Analysis Core:** This is the computational heart of the system during the Analysis Phase. It is a C\# application built around a llama.cpp wrapper library, such as LLamaSharp. Its responsibilities include loading the foundation model (in GGUF format), managing its distribution between VRAM and system RAM, feeding it domain-specific data, capturing layer activations, and executing the feature extraction and causal tracing algorithms.  
* **Persistence Fabric:** A novel, tightly integrated data layer comprising SQL Server 2025 and the Neo4j graph database. This fabric is not a simple polyglot architecture but a unified system where SQL Server acts as the primary transactional endpoint and system of record, while Neo4j serves as a specialized analytical store for the inherently graph-structured neural circuit data. The unification is achieved through a custom SQL CLR bridge.  
* **Distillation Pipeline:** A workflow engine, implemented in C\#, that orchestrates the Distillation Phase. It queries the Persistence Fabric to retrieve the identified knowledge scaffold (the circuit graph), calculates attribution scores for the model's parameters, executes the targeted pruning algorithm, and manages the post-pruning calibration and final quantization of the distilled agent.  
* **Runtime Engine:** The C\# application layer responsible for the Execution Phase. It loads the final, distilled GGUF agent file from the Persistence Fabric and provides an interface for interaction. This component leverages LLamaSharp for efficient inference and includes logic for advanced session and state management.

The data flow through the engine is meticulously designed to handle large volumes of data on resource-constrained hardware. The process begins with domain-specific text datasets, stored in standard SQL Server tables. During the Analysis Phase, the Analysis Core streams these datasets through the foundation model. The resulting activation data, which can be terabytes in size, is not held in memory but streamed directly into SQL Server and stored in varbinary(max) columns enabled with the FILESTREAM feature.7 The SQL CLR bridge then processes this FILESTREAM data, extracts features and their causal relationships, and populates the circuit graph in Neo4j. During the Distillation Phase, the pipeline reads this graph from Neo4j to guide the pruning of the original model's GGUF file (also stored in FILESTREAM). The final, pruned, and quantized GGUF file, representing the distilled agent, is then written back to a FILESTREAM-enabled table, ready for the Runtime Engine.

### **1.3. The Integrated Data Fabric: A Unified Approach to Polyglot Persistence**

A core directive for the engine's design is the minimization of polyglot persistence. While the architecture specifies two distinct database technologies—SQL Server 2025 for relational and large object storage, and Neo4j for graph data—they are not implemented as loosely-coupled, independent systems. Instead, they form a cohesive, unified data fabric where the application layer interacts almost exclusively with a single database interface: SQL Server.  
This unification is made possible by a specific architectural pattern centered on the advanced capabilities of SQL Server 2025, particularly its Common Language Runtime (CLR) integration. The application's primary data interactions—storing models, datasets, and activations, and initiating analysis—are all directed at SQL Server, which serves as the transactional system of record. Neo4j's role is that of a specialized, materialized analytical view. Neural circuits, which represent the connections and causal influences between learned features, are fundamentally graph-structured data.8 Storing and querying this data in a native graph database like Neo4j is vastly more efficient and intuitive than attempting to model it in a relational database.  
The critical component that enables this unified fabric is the **SQL CLR Bridge**. This consists of C\# methods compiled into a.NET assembly and registered within SQL Server as stored procedures.11 From the application's perspective, analyzing a model is as simple as executing a T-SQL stored procedure, for example,  
EXEC \[circuits\].ExtractAndStoreCircuits @ModelID \= 1, @DatasetID \= 1;. The complexity of the underlying process is entirely encapsulated within this procedure.  
Internally, the C\# code of the CLR procedure performs a series of sophisticated operations. It uses the efficient in-process SQL Server connection (context connection=true) to read metadata and access the raw activation data stored in FILESTREAM. It then processes this data in-memory to extract the feature graph. Crucially, this C\# code can instantiate and use the official Neo4j.NET driver to establish a connection to the Neo4j server and execute Cypher queries to populate the graph.13 By wrapping the entire operation within a single stored procedure, the polyglot nature of the backend is abstracted away from the application developer. This design pattern directly fulfills the user's directive by making the persistence layer appear monolithic from the outside, thereby minimizing the practical effects of its internal polyglot implementation.

## **Part II: The Analysis Core \- From Activations to Interpretable Circuits**

### **2.1. Foundation Model Preparation and Activation Capture**

The Analysis Phase commences with the preparation of the foundation "teacher" model. To operate within the memory constraints of consumer hardware, the model must be in a quantized format. The GGUF format, developed for the llama.cpp ecosystem, is the designated standard for this engine due to its efficiency and flexibility.15 The model is loaded into memory using a C\# wrapper library like  
LLamaSharp, which provides a high-level.NET interface to the underlying C++ llama.cpp engine.5  
A key strategy for managing hardware limitations is the use of llama.cpp's partial GPU offloading capability. Consumer GPUs may have insufficient VRAM (e.g., 8-24 GB) to hold an entire large model (e.g., a 70B parameter model, which can be over 40 GB even when quantized). The engine will therefore implement a strategy where a configurable number of the model's layers are offloaded to the GPU's VRAM, while the remaining layers are processed by the CPU, using the much larger system RAM.15 This hybrid execution model provides a significant speedup over CPU-only inference without requiring an enterprise-grade GPU, striking a critical balance between performance and hardware cost.  
Once the model is loaded, the activation capture process begins. A domain-specific dataset, such as a corpus of clinical question-and-answer pairs for a medical agent 17, is fed through the model in batches. For each token in the input, the engine intercepts and records the activation vectors from specific target layers, typically the outputs of the Multi-Layer Perceptron (MLP) sub-blocks within each transformer layer. Given that a large dataset processed through a large model can generate terabytes of activation data, it is infeasible to store this in system RAM. The engine's C\# process will therefore stream these captured activation vectors directly to the SQL Server Persistence Fabric. The data will be written to a  
varbinary(max) column configured with the FILESTREAM attribute, which stores the data on the filesystem under transactional control of the database engine, thus avoiding memory exhaustion and providing durable, transactionally consistent storage for the raw analysis data.7

### **2.2. Feature Extraction: The Case for Skip Transcoders over Sparse Autoencoders (SAEs)**

The central task of mechanistic interpretability is to decompose the dense, high-dimensional, and polysemantic activation vectors of a model into a sparse set of monosemantic, human-interpretable "features." This is the foundation upon which all subsequent circuit analysis is built. While Sparse Autoencoders (SAEs) are a prominent tool for this task, this blueprint advocates for the adoption of a more advanced and effective alternative: **Skip Transcoders**.  
The traditional approach, SAEs, are simple neural networks trained to reconstruct a model's activations from a sparse, higher-dimensional latent representation.2 The goal is that each dimension in this latent space will correspond to a single, understandable concept. However, SAEs suffer from several well-documented limitations that challenge their reliability for systematic analysis:

* **Feature Inconsistency:** A significant issue is that SAEs trained on the same data often fail to converge to the same set of features across different training runs. This instability undermines the scientific goal of identifying a canonical set of features for a given model, making results difficult to reproduce and compare.1  
* **High Reconstruction Error:** SAEs frequently fail to fully reconstruct the original activation, leaving a large residual error or "dark matter" that contains unexplained model functionality. This means important computational mechanisms may be missed entirely.17  
* **Domain-Specific Efficacy:** Research indicates that SAEs trained on a broad, general distribution of data are less effective at capturing meaningful features than those trained on a well-defined, confined domain.17 While this aligns with the Hartonomous Engine's goal of specialization, it highlights the limitations of general-purpose, "foundation" SAEs.

This blueprint proposes the use of **Skip Transcoders** as a superior methodology that directly addresses these shortcomings.18 A transcoder's architecture is subtly but critically different from an SAE's. Instead of being trained to reconstruct its own input (a layer's activations), a transcoder is trained to reconstruct the  
*output* of a model component (e.g., an MLP layer) from that component's *input*.18 This objective forces the transcoder to learn an approximation of the  
*function* performed by the component, rather than just a sparse representation of its state. This functional approximation leads to features that are demonstrably more interpretable.  
Furthermore, the "skip transcoder" architecture enhances this by adding a direct, linear (affine) skip connection from the input to the output. This simple addition allows the transcoder to more easily model the linear parts of the component's function, significantly reducing reconstruction error without negatively impacting the interpretability of the features learned in the sparse bottleneck. Experimental evidence shows that skip transcoders Pareto-dominate SAEs, achieving both lower reconstruction loss and higher interpretability scores simultaneously.18  
The implementation will involve training a skip transcoder—a simple, single-hidden-layer neural network with a skip connection—on the activation data stored in FILESTREAM. The resulting transcoder weights (the encoder and decoder matrices) represent the dictionary of learned features and will be stored in standard SQL Server tables for subsequent analysis.

| Feature | Sparse Autoencoder (SAE) | Skip Transcoder |
| :---- | :---- | :---- |
| **Training Objective** | Reconstruct a component's activations from a sparse latent space: x≈D(E(x)) | Reconstruct a component's output from its input via a sparse latent space: f(x)≈D(E(x))+Ax+b |
| **Core Limitation** | High reconstruction error ("dark matter") and feature inconsistency across training runs.17 | Primarily focused on feed-forward components like MLPs; application to attention layers is an area of active research. |
| **Interpretability Score** | Establishes a baseline for automated interpretability metrics.18 | Significantly outperforms SAEs on automated interpretability benchmarks.18 |
| **Reconstruction Fidelity** | Often leaves \>20% of activation variance unexplained, hiding key model functions.17 | Achieves much lower reconstruction error due to the skip connection, capturing more of the component's function.18 |
| **Key Innovation** | Introduced the concept of decomposing activations into a sparse, interpretable feature space.18 | Models the *function* of a component, not just its state, and uses a skip connection to Pareto-dominate SAEs.18 |
| **Recommended Use Case** | Legacy analysis or when a direct representation of activations (not function) is explicitly required. | The recommended default for feature extraction in the Hartonomous Engine due to superior interpretability and fidelity. |

### **2.3. Causal Tracing and Circuit Function Analysis**

Extracting a dictionary of interpretable features is only the first step. To build a functional understanding of the model, it is necessary to determine the causal role these features play in producing specific behaviors. The engine will employ a technique known as **Causal Tracing** (or activation patching) to establish these cause-and-effect relationships.25  
Causal tracing is an experimental method used to identify which internal model states are causally responsible for a given output. The methodology implemented by the Analysis Core will be as follows:

1. **Establish a Baseline (Clean Run):** The model is run on a clean, factual input prompt (e.g., "The capital of France is"). The feature activations at each layer, as computed by the trained skip transcoder, are recorded and cached. The model's correct output (e.g., "Paris") is confirmed.  
2. **Establish a Counterfactual (Corrupted Run):** The model is run on a different but structurally similar input that elicits a different correct answer (e.g., "The capital of Italy is"). This is the "corrupted" run, relative to the clean run's goal of producing "Paris".  
3. **Perform Intervention (Patching):** The engine now re-runs the corrupted input ("The capital of Italy is"), but at a specific layer, it intervenes. Instead of using the feature activations generated by this input, it "patches in" (copies and overwrites) the cached activation of a single feature from the clean run.  
4. **Observe the Outcome:** The final output of this patched run is observed. If patching a specific feature from the "France" run (e.g., a feature representing the concept of "Paris") into the "Italy" run causes the model's output to flip from "Rome" to "Paris," a causal link has been established. This feature is causally implicated in the model's ability to recall the capital of France.

This process is repeated systematically for all relevant features across all layers to build a comprehensive map of causal influences. This analysis is particularly powerful for understanding and mitigating model failures like hallucinations. By comparing the circuits that activate for a factual statement versus a hallucinated one, the engine can identify the specific features and pathways responsible for the erroneous output.28 The final output of the causal tracing process is a directed, weighted graph where nodes are the interpretable features and edges represent causal influence. This graph is the primary data structure that will be persisted in the Neo4j database for the Distillation Phase.

## **Part III: The Distillation Pipeline \- Forging Specialized Agents**

### **3.1. Circuit-Based Knowledge Scaffolding**

The Distillation Phase begins by leveraging the causal graph stored in Neo4j to define a "knowledge scaffold." This scaffold represents the minimal set of computational mechanisms within the teacher model required for the target specialization. The process transforms the abstract goal of creating a "medical AI" or a "coding assistant" into a concrete, queryable set of model components.  
Using Cypher queries against the Neo4j database, the system identifies all (:Feature) nodes and their connecting :CAUSALLY\_INFLUENCES relationships that are activated by the domain-specific dataset. For example, to build an agent specialized in answering questions about infectious diseases, the system would query for all circuits that were causally implicated in generating correct answers to questions about that topic during the Analysis Phase.  
This scaffold is a subgraph of the teacher model's complete circuit graph. It contains the features representing relevant concepts (e.g., "mononucleosis," "symptoms," "viral infection") and the pathways that connect them to produce correct outputs. This subgraph serves as a blueprint for the distillation process, explicitly defining which parts of the teacher model's "knowledge" to preserve and which to discard.

### **3.2. Attribution-Guided Pruning as the Distillation Mechanism**

The core of the distillation process is a technique called **attribution-guided pruning**.4 This method uses the knowledge scaffold as a guide to surgically remove unnecessary weights from the teacher model. This approach is a form of mechanistic knowledge distillation, where the internal computational structure, rather than just the input-output behavior, is transferred to the student model.3  
The process unfolds as follows:

1. **Attribution Score Calculation:** The system first needs to determine the importance of each individual weight in the model with respect to the desired knowledge scaffold. This is done by calculating an attribution score. While complex methods like Layer-wise Relevance Propagation (LRP) can be used, a highly effective and computationally efficient proxy is to use a metric based on weight magnitude and activation data, as demonstrated by the Wanda pruning algorithm.31 For each weight, its importance score can be calculated as the product of its absolute magnitude and the norm of the corresponding input activations observed during the Analysis Phase:  
   Importance=∣Wij​∣⋅∣∣Xj​∣∣. This score quantifies how much a weight contributes to the model's computation on the target domain data.  
2. **Targeted Unstructured Pruning:** With attribution scores calculated, the system performs unstructured (weight-level) pruning.33 It iterates through the parameters of the teacher model. If a parameter is part of a model component (e.g., a neuron in an MLP) that implements a feature within the knowledge scaffold, its attribution score is evaluated. Weights with high scores are preserved. Conversely, any weight that is not part of the scaffold, or has a very low attribution score even if it is, is pruned by setting its value to zero. This effectively erases the computational pathways related to out-of-domain knowledge, such as the model's ability to write poetry if the target domain is medical diagnostics. The result is a highly sparse version of the original model, containing only the circuits essential for the specialized task.

### **3.3. Post-Pruning Calibration and Quantization**

Pruning a large percentage of a model's weights can lead to a slight degradation in performance. To counteract this, the distillation pipeline includes a brief calibration step. The pruned, sparse model is fine-tuned for a small number of epochs on a high-quality, curated subset of the domain dataset. This process, which is significantly faster and less resource-intensive than retraining from scratch, allows the remaining weights to adjust and compensate for the removed parameters, often recovering most or all of the initial performance loss.34  
The final step in the pipeline is quantization. The pruned and calibrated model, which is still in a high-precision format (e.g., 16-bit floating point), is converted into the GGUF format using a low-bit representation. This step is critical for reducing the model's final memory footprint and increasing inference speed, making it suitable for deployment on consumer hardware. The llama.cpp ecosystem offers a variety of quantization methods, each with a different trade-off between model size, accuracy (measured by perplexity), and inference speed.15 The choice of quantization level depends on the specific requirements of the distilled agent.

| Quantization Method | Avg. Bits/Weight | Relative Size (vs. FP16) | Perplexity/Accuracy Impact | Inference Speed (CPU vs. GPU) | Recommended Use Case |
| :---- | :---- | :---- | :---- | :---- | :---- |
| **FP16 (Baseline)** | 16.0 | 100% | Baseline reference for accuracy. | Slowest on CPU; fastest on GPU if model fits in VRAM. | Pre-quantization model storage; analysis tasks. |
| **Q8\_0** | 8.0 | \~50% | Minimal to negligible quality loss. | Significantly faster on CPU than FP16. | High-quality agents where size reduction is secondary to preserving maximum performance. |
| **Q5\_K\_M** | \~5.15 | \~32% | Excellent balance; considered a "go-to" for high quality with significant size reduction.15 | Fast on CPU and GPU. | The recommended default for most distilled agents, offering the best trade-off between performance and size. |
| **Q4\_K\_M** | \~4.5 | \~28% | Good quality, slightly more loss than Q5\_K\_M but smaller file size. | Very fast on CPU and GPU. | Scenarios where memory or storage is highly constrained, and a minor accuracy trade-off is acceptable. |
| **Q3\_K\_S** | \~3.4 | \~21% | Noticeable quality degradation; may affect complex reasoning. Performance should be validated.15 | Very fast on CPU. | Experimental use or for agents performing very simple, constrained tasks where size is the absolute priority. |
| **IQ2\_XXS** | \~2.3 | \~14% | Significant quality loss. A new SOTA quantization method, but can be very slow on CPU.16 | Can be *slower* than K-quants on CPU due to complex dequantization logic. Best if fully offloaded to VRAM.16 | Extreme compression scenarios where the entire model must fit into a very small VRAM budget. |

## **Part IV: The Persistence Fabric \- Implementation with SQL Server & Neo4j**

### **4.1. SQL Server as the System of Record**

SQL Server 2025 will serve as the authoritative data store and the primary interface for the Hartonomous Engine. It will manage all structured metadata related to projects, models, and datasets, as well as all large binary objects using its integrated FILESTREAM feature. This centralized approach simplifies data management, backup, and transactional integrity.  
The core relational schema is designed to track the entire distillation lifecycle. The use of FILESTREAM for large binary objects like model weights and activation data is critical. It allows the database to manage these terabyte-scale files transactionally while storing them directly on the NTFS/ReFS filesystem, which avoids bloating the primary database files and leverages the efficiency of the operating system's file caching mechanisms.7  
For environments with extremely large datasets (tens of terabytes), performance management is crucial. The blueprint recommends several best practices for FILESTREAM: partitioning the data (e.g., by date or project) into different filegroups to improve backup and management overhead, tuning backup parameters (BUFFERCOUNT, MAXTRANSFERSIZE), and applying filesystem-level optimizations such as disabling 8.3 naming and last-access-time updates to reduce I/O overhead on directories containing millions of files.36

| Table Name | Column Name | Data Type | Constraints | Description/Purpose |
| :---- | :---- | :---- | :---- | :---- |
| **Projects** | ProjectID | INT | PRIMARY KEY, IDENTITY(1,1) | Unique identifier for a distillation project. |
|  | ProjectName | NVARCHAR(255) | NOT NULL, UNIQUE | Human-readable name for the project. |
|  | Description | NVARCHAR(MAX) | NULL | Detailed description of the project's goals. |
|  | CreatedDate | DATETIME2 | NOT NULL, DEFAULT(GETUTCDATE()) | Timestamp of project creation. |
| **FoundationModels** | ModelID | INT | PRIMARY KEY, IDENTITY(1,1) | Unique identifier for a foundation model. |
|  | ModelName | NVARCHAR(255) | NOT NULL | Name of the model (e.g., "Llama-3-70B-Instruct"). |
|  | GGUF\_File | VARBINARY(MAX) | FILESTREAM, NOT NULL | The GGUF model file, stored on the filesystem via FILESTREAM. |
|  | Parameters | BIGINT | NOT NULL | Number of parameters in the model. |
| **Datasets** | DatasetID | INT | PRIMARY KEY, IDENTITY(1,1) | Unique identifier for a dataset. |
|  | DatasetName | NVARCHAR(255) | NOT NULL | Name of the dataset. |
|  | DatasetContent | VARBINARY(MAX) | FILESTREAM, NOT NULL | The dataset content (e.g., a zip file of text documents). |
| **ActivationCaptures** | CaptureID | BIGINT | PRIMARY KEY, IDENTITY(1,1) | Unique identifier for an activation capture run. |
|  | ModelID | INT | FOREIGN KEY references FoundationModels | The model used for the capture. |
|  | DatasetID | INT | FOREIGN KEY references Datasets | The dataset used for the capture. |
|  | ModelLayer | INT | NOT NULL | The layer number from which activations were captured. |
|  | ActivationData | VARBINARY(MAX) | FILESTREAM, NOT NULL | The captured activation vectors. |
| **DistilledAgents** | AgentID | INT | PRIMARY KEY, IDENTITY(1,1) | Unique identifier for a distilled agent. |
|  | ProjectID | INT | FOREIGN KEY references Projects | The project this agent belongs to. |
|  | SourceModelID | INT | FOREIGN KEY references FoundationModels | The foundation model it was distilled from. |
|  | AgentName | NVARCHAR(255) | NOT NULL | Name of the specialized agent. |
|  | Pruned\_GGUF\_File | VARBINARY(MAX) | FILESTREAM, NOT NULL | The final, pruned, and quantized GGUF file for the agent. |

### **4.2. Neo4j as the Circuit Graph Database**

Neo4j will house the analytical heart of the engine: the neural circuit graph. Its schema is designed to represent the interpretable features and their causal relationships as a native graph, enabling powerful and efficient analysis that would be cumbersome in a relational model.  
The graph model consists of two primary node labels and several key relationship types:

* **Node Labels:**  
  * (:Feature): Represents a single, monosemantic feature discovered by the skip transcoder. It is the fundamental unit of analysis.  
  * (:ModelComponent): Represents a physical component of the neural network, such as an (:AttentionHead) or (:MLPNeuron). This allows linking abstract features to their physical implementation in the model's architecture.  
* **Relationship Types:**  
  * \`\`: A directed, weighted relationship from one Feature to another, indicating that the source feature has a causal effect on the activation of the target feature. The weight property quantifies the strength of this influence as determined by causal tracing.  
  * \`\`: A relationship connecting a (:Feature) node to one or more (:ModelComponent) nodes, specifying the physical parts of the model that collectively give rise to the feature.

This schema supports sophisticated analytical queries. For example, one could find the most influential features for a specific task with:  
MATCH (f:Feature)--\>(downstream:Feature) WHERE f.task\_relevance \> 0.9 RETURN f, avg(r.weight) as avg\_influence ORDER BY avg\_influence DESC  
Or, one could trace a complete computational path responsible for a specific output:  
MATCH path \= (start:Feature {name: 'concept\_A'})--\>(end:Feature {name: 'concept\_B'}) RETURN path

| Element Type | Label/Type | Properties | Description |
| :---- | :---- | :---- | :---- |
| **Node** | Feature | featureId: INT (unique) layer: INT description: STRING avgActivation: FLOAT sparsity: FLOAT | Represents a single interpretable, monosemantic feature extracted by the transcoder. The description is a human-readable explanation generated via automated interpretability techniques.18 |
| **Node** | ModelComponent | componentId: STRING (e.g., "L12.MLP.3072") type: STRING (e.g., "MLPNeuron", "AttentionHead") | Represents a physical component of the LLM architecture. |
| **Relationship** | CAUSALLY\_INFLUENCES | weight: FLOAT datasetId: INT | A directed edge from (f1:Feature) to (f2:Feature) indicating that activating f1 causally increases or decreases the activation of f2. The weight is determined via causal tracing. |
| **Relationship** | IMPLEMENTED\_BY |  | A directed edge from (f:Feature) to (mc:ModelComponent) linking an abstract feature to the physical neuron(s) or head(s) that implement it. |
| **Relationship** | COMPOSES\_INTO |  | A directed edge from a lower-level feature to a higher-level feature, representing hierarchical composition (e.g., features for "wheel" and "door" compose into a "car" feature). |

### **4.3. The SQL CLR Bridge: Unifying the Data Fabric**

The SQL CLR bridge is the cornerstone of the integrated persistence fabric, enabling complex C\# logic to execute within the SQL Server process to manage the data flow between the relational and graph databases.  
The implementation will center on a C\# static method within an assembly deployed to SQL Server. This method will be exposed as a T-SQL stored procedure. A representative signature would be:

C\#

public static void ExtractFeaturesAndBuildGraph(SqlInt32 modelId, SqlInt32 datasetId)  
{  
    //... implementation...  
}

The internal logic of this procedure will follow a precise sequence:

1. **Connect to SQL Server:** It will use the special context connection=true connection string to get a highly efficient, in-process connection to the SQL Server instance it is running in.  
2. **Access FILESTREAM Data:** Using this connection, it will execute a T-SQL query to retrieve the filesystem path to the relevant ActivationData FILESTREAM object via the PathName function.  
3. **Stream and Process Data:** It will then use the System.Data.SqlTypes.SqlFileStream API to open a stream to the activation data. This allows it to process potentially terabyte-sized files chunk by chunk, without loading the entire file into memory.  
4. **Execute Analysis Logic:** The C\# code will then execute the feature extraction (skip transcoder) and causal tracing algorithms on the streamed data.  
5. **Connect to Neo4j:** The procedure will instantiate the official Neo4j.Driver for.NET. This requires the CLR assembly to have permissions to access external network resources.  
6. **Populate the Graph:** It will open a transaction with the Neo4j database and execute a series of Cypher queries to create the (:Feature) nodes and \`\` relationships derived from the analysis.

**Security is paramount** for this component. Because the assembly needs to make network calls to Neo4j, it cannot run with the default SAFE permission set. It will require either EXTERNAL\_ACCESS or UNSAFE. On SQL Server 2017 and later, the clr strict security option is enabled by default, which treats all assemblies as UNSAFE unless they are explicitly trusted.39 The correct and secure procedure for enabling the CLR bridge is to:

1. Sign the C\# assembly with a strong name key or a certificate.  
2. In the master database, create an asymmetric key or certificate from the assembly file.  
3. Create a login from that key/certificate.  
4. Grant that login the UNSAFE ASSEMBLY permission.  
5. Create the assembly in the target database from the signed DLL file.  
   This ensures that only code from a trusted, cryptographically signed source can execute with elevated privileges, providing a robust security boundary.40

## **Part V: The Runtime Engine \- C\# Implementation**

### **5.1. Core C\# Services and Project Structure**

The Runtime Engine, the primary application for interacting with distilled agents, will be developed as a.NET solution. A clean architecture is recommended to ensure separation of concerns and maintainability. The solution would be structured with the following projects:

* **Hartonomous.Domain:** A class library containing the core business entities (e.g., DistilledAgent, ChatMessage, ConversationSession) and business logic, with no external dependencies.  
* **Hartonomous.Application:** A class library containing the application logic and use cases (e.g., StartNewChatSession, SendMessageToAgent). It orchestrates the flow of data between the domain and infrastructure layers.  
* **Hartonomous.Infrastructure:** A class library responsible for all external concerns. This includes the implementation of data access repositories that interact with the SQL Server Persistence Fabric (calling stored procedures, retrieving model file paths) and the C\# implementation of the llama.cpp wrapper for model inference.  
* **Hartonomous.Presentation:** The user-facing layer, which could be a Console Application for simple interaction, a Web API for programmatic access, or a full desktop/web UI.

This structure ensures that the core logic of the application is independent of the specific database or AI inference engine being used, making the system more modular and testable.

### **5.2. Integrating the llama.cpp Engine via LLamaSharp**

The Infrastructure project will contain the concrete implementation for running the distilled agents using the LLamaSharp library, which provides idiomatic C\# bindings for the high-performance llama.cpp engine.5  
The integration involves several key steps:

1. **Model Loading:** The application will first query the SQL Server database to retrieve the metadata for a specific DistilledAgent, including the filesystem path to its pruned GGUF file (obtained via the PathName function on the FILESTREAM column). This path is then used to load the model weights into a LLamaWeights object in LLamaSharp.  
2. **Inference Execution:** For interacting with the agent, LLamaSharp provides several "executors." For a typical chat-based agent, the InteractiveExecutor is ideal. It maintains the context of the conversation automatically. For single-shot, non-conversational tasks, the InstructExecutor or a stateless call might be more appropriate.  
3. **Chat Session Management:** A critical feature for creating useful AI agents is the ability to manage conversational history. The LLamaSharp ChatSession class provides a high-level abstraction for this purpose.43 The engine will use this class to:  
   * Maintain the context of an ongoing conversation, passing the history of user and assistant messages back to the model with each new turn.  
   * Persist conversations by serializing the ChatSession state. The SaveSession and LoadSession methods allow a conversation to be saved to disk or a database and resumed later, providing a seamless user experience.43 This state can be stored in a dedicated table within the SQL Server database.

### **5.3. Hardware Resource Management in C\#**

The C\# implementation must be acutely aware of the consumer hardware constraints. LLamaSharp exposes the necessary configuration parameters from the underlying llama.cpp engine to allow for fine-grained control over resource usage.  
When loading a model, the application will construct a ModelParams object. This object allows for the precise configuration of the hardware strategy:

* **GPU Offloading:** The GpuLayerCount property will be set to a value appropriate for the available VRAM. For example, on a GPU with 12 GB of VRAM, this might be set to offload 20-30 layers of a 7B parameter model, with the rest being handled by the CPU.45 This value should be configurable by the end-user or detected automatically.  
* **Memory Management:** To handle very large models that might strain even generous amounts of system RAM, the UseMemorymap property will be set to true. This instructs llama.cpp to use memory-mapped files, which allows the operating system to page parts of the model in and out of RAM as needed, preventing out-of-memory errors at the cost of slightly slower inference when data is paged from disk.45

By programmatically controlling these parameters from C\#, the Runtime Engine can dynamically adapt to the user's specific hardware configuration, ensuring that the Hartonomous Distillation Engine can operate effectively across a wide range of consumer-grade systems.

## **Part VI: Conclusion and Strategic Recommendations**

### **6.1. Blueprint Summary and Implementation Roadmap**

The Hartonomous Distillation Engine represents a novel, principled approach to the creation of specialized AI agents. Its architecture is a synthesis of cutting-edge research in mechanistic interpretability and a robust, C\#-first enterprise software design. By rejecting opaque, black-box methods in favor of a transparent, three-phase cycle of **Analysis**, **Distillation**, and **Execution**, the engine provides a systematic framework for reverse-engineering foundation models and surgically extracting desired capabilities.  
The key architectural innovations are twofold. First, the adoption of **Skip Transcoders** and **Causal Tracing** provides a more reliable and faithful method for identifying the internal circuits of a model compared to conventional techniques. Second, the **Integrated Data Fabric**, which uses a **SQL CLR Bridge** to unify SQL Server 2025 and Neo4j, offers a sophisticated solution to data management that provides both transactional integrity and powerful graph analytics capabilities, all while abstracting complexity from the application layer. The entire system is designed from the ground up to operate within the real-world constraints of consumer-grade hardware.  
A phased implementation is recommended to manage complexity and deliver value incrementally:

1. **Phase 1: Foundation & Runtime:** The initial focus should be on establishing the core infrastructure. This involves setting up the SQL Server 2025 database with the defined schema and FILESTREAM enabled. Concurrently, the C\# Runtime Engine should be developed using LLamaSharp to the point where it can successfully load a pre-quantized GGUF model from the database and perform basic inference. This phase validates the end-to-end execution path on target hardware.  
2. **Phase 2: Analysis Core:** This phase focuses on implementing the most research-intensive part of the system. The C\# application logic for activation capture must be built, capable of streaming large volumes of data into FILESTREAM. The training pipeline for the Skip Transcoders should be developed, followed by the implementation of the Causal Tracing (activation patching) algorithms. At the end of this phase, the system should be able to produce a causal circuit graph from a given model and dataset.  
3. **Phase 3: Integration & Distillation:** The final phase connects the analysis to the outcome. The SQL CLR Bridge must be developed and securely deployed to automate the population of the Neo4j graph database from the analysis results. Following this, the Distillation Pipeline will be built, implementing the attribution-guided pruning logic that uses the Neo4j graph to create a pruned model, which is then calibrated, quantized, and stored back into SQL Server, completing the full distillation cycle.

### **6.2. Future Directions and Advanced Research**

While this blueprint provides a complete functional specification, the underlying field of mechanistic interpretability is rapidly evolving. Several future research directions could further enhance the capabilities of the Hartonomous Engine:

* **Automated Circuit Discovery and Classification:** The current blueprint relies on querying the circuit graph based on domain-specific inputs. Future work could focus on developing algorithms that run on the Neo4j graph to automatically discover, categorize, and label recurring circuit motifs (e.g., identifying "induction heads" or "S-expression parsers" without prior knowledge), creating a comprehensive library of a model's fundamental algorithms.  
* **Compositional Agency:** The explicit, graph-based representation of a model's capabilities opens the door to **compositional agency**. It may be possible to create a new, multi-skilled agent not by distillation, but by composition. This would involve merging the "knowledge scaffolds" (the circuit subgraphs) from two or more different specialized agents—for instance, combining the circuits for C\# code generation with the circuits for SQL query writing—to create a novel agent with both capabilities, potentially without requiring a shared foundation model.  
* **Online and Self-Distillation:** The current model uses an offline distillation process. Future iterations could explore online distillation techniques, where the student model is trained and pruned simultaneously with the teacher's analysis, potentially leading to a more efficient, end-to-end process.47 Similarly, self-distillation, where knowledge from a model's deeper layers is used to train its own shallower layers, could offer further avenues for optimization.

By building on the robust and transparent foundation detailed in this blueprint, the Hartonomous Distillation Engine is well-positioned to not only meet its immediate objectives but also to serve as a platform for pioneering next-generation research in creating safer, more capable, and more understandable AI systems.

#### **Works cited**

1. Mechanistic Interpretability in AI \- Emergent Mind, accessed September 17, 2025, [https://www.emergentmind.com/topics/mechanistic-interpretability](https://www.emergentmind.com/topics/mechanistic-interpretability)  
2. Mechanistic Interpretability: A Survey | by Shav Vimalendiran \- Medium, accessed September 17, 2025, [https://medium.com/@shavtge/mechanistic-interpretability-a-survey-c7b8c5411767](https://medium.com/@shavtge/mechanistic-interpretability-a-survey-c7b8c5411767)  
3. arxiv.org, accessed September 17, 2025, [https://arxiv.org/html/2505.10822v1](https://arxiv.org/html/2505.10822v1)  
4. (PDF) Attribution-guided Pruning for Compression, Circuit Discovery ..., accessed September 17, 2025, [https://www.researchgate.net/publication/392765972\_Attribution-guided\_Pruning\_for\_Compression\_Circuit\_Discovery\_and\_Targeted\_Correction\_in\_LLMs](https://www.researchgate.net/publication/392765972_Attribution-guided_Pruning_for_Compression_Circuit_Discovery_and_Targeted_Correction_in_LLMs)  
5. LLamaSharp Documentation, accessed September 17, 2025, [https://scisharp.github.io/LLamaSharp/0.4/](https://scisharp.github.io/LLamaSharp/0.4/)  
6. Local AI Chat with C\# \- SWHarden.com, accessed September 17, 2025, [https://swharden.com/blog/2024-02-19-local-ai-chat-csharp/](https://swharden.com/blog/2024-02-19-local-ai-chat-csharp/)  
7. FILESTREAM (SQL Server) \- Microsoft Learn, accessed September 17, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/blob/filestream-sql-server?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/blob/filestream-sql-server?view=sql-server-ver17)  
8. Neo4j Graph Database & Analytics | Graph Database Management System, accessed September 17, 2025, [https://neo4j.com/](https://neo4j.com/)  
9. Graph Neural Networks for Databases: A Survey \- arXiv, accessed September 17, 2025, [https://arxiv.org/html/2502.12908v1](https://arxiv.org/html/2502.12908v1)  
10. Neural Causal Graph for Interpretable and Intervenable Classification \- OpenReview, accessed September 17, 2025, [https://openreview.net/forum?id=nmvmPIi185](https://openreview.net/forum?id=nmvmPIi185)  
11. Getting Started with SQL CLR Development Tools Guide | MoldStud, accessed September 17, 2025, [https://moldstud.com/articles/p-how-to-get-started-with-sql-clr-development-tools-a-comprehensive-guide](https://moldstud.com/articles/p-how-to-get-started-with-sql-clr-development-tools-a-comprehensive-guide)  
12. Understanding SQL CLR \- Your Comprehensive Guide to Managed Code in SQL Server, accessed September 17, 2025, [https://moldstud.com/articles/p-understanding-sql-clr-your-comprehensive-guide-to-managed-code-in-sql-server](https://moldstud.com/articles/p-understanding-sql-clr-your-comprehensive-guide-to-managed-code-in-sql-server)  
13. Getting Started with Neo4j GraphDB in C\# ASP.NET Core \- TheCodeBuzz, accessed September 17, 2025, [https://thecodebuzz.com/crud-neo4j-csharp-asp-net-core-graph-database/](https://thecodebuzz.com/crud-neo4j-csharp-asp-net-core-graph-database/)  
14. Build applications with Neo4j and .NET \- Neo4j .NET Driver Manual, accessed September 17, 2025, [https://neo4j.com/docs/dotnet-manual/current/](https://neo4j.com/docs/dotnet-manual/current/)  
15. Quantize Llama models with GGUF and llama.cpp \- Origins AI, accessed September 17, 2025, [https://originshq.com/blog/quantize-llama-models-with-gguf-and-llama-cpp/](https://originshq.com/blog/quantize-llama-models-with-gguf-and-llama-cpp/)  
16. Overview of GGUF quantization methods : r/LocalLLaMA \- Reddit, accessed September 17, 2025, [https://www.reddit.com/r/LocalLLaMA/comments/1ba55rj/overview\_of\_gguf\_quantization\_methods/](https://www.reddit.com/r/LocalLLaMA/comments/1ba55rj/overview_of_gguf_quantization_methods/)  
17. Resurrecting the Salmon: Rethinking Mechanistic Interpretability ..., accessed September 17, 2025, [https://www.arxiv.org/abs/2508.09363](https://www.arxiv.org/abs/2508.09363)  
18. arxiv.org, accessed September 17, 2025, [https://arxiv.org/html/2501.18823v1](https://arxiv.org/html/2501.18823v1)  
19. Position: Mechanistic Interpretability Should Prioritize Feature Consistency in SAEs \- arXiv, accessed September 17, 2025, [https://arxiv.org/abs/2505.20254](https://arxiv.org/abs/2505.20254)  
20. Position: Mechanistic Interpretability Should Prioritize Feature Consistency in SAEs \- arXiv, accessed September 17, 2025, [https://arxiv.org/html/2505.20254v1](https://arxiv.org/html/2505.20254v1)  
21. \[2501.18823\] Transcoders Beat Sparse Autoencoders for Interpretability \- arXiv, accessed September 17, 2025, [https://arxiv.org/abs/2501.18823](https://arxiv.org/abs/2501.18823)  
22. \[Literature Review\] Transcoders Find Interpretable LLM Feature Circuits \- Moonlight, accessed September 17, 2025, [https://www.themoonlight.io/en/review/transcoders-find-interpretable-llm-feature-circuits](https://www.themoonlight.io/en/review/transcoders-find-interpretable-llm-feature-circuits)  
23. Transcoders Find Interpretable LLM Feature Circuits \- arXiv, accessed September 17, 2025, [https://arxiv.org/pdf/2406.11944](https://arxiv.org/pdf/2406.11944)  
24. Circuit Tracing: Revealing Computational Graphs in Language Models, accessed September 17, 2025, [https://transformer-circuits.pub/2025/attribution-graphs/methods.html](https://transformer-circuits.pub/2025/attribution-graphs/methods.html)  
25. Tracing the thoughts of a large language model \- Anthropic, accessed September 17, 2025, [https://www.anthropic.com/research/tracing-thoughts-language-model](https://www.anthropic.com/research/tracing-thoughts-language-model)  
26. Tutorial Proposal: Causality for Large Language Models \- Zhijing Jin, accessed September 17, 2025, [https://zhijing-jin.com/files/papers/2024\_CausalLLM\_Tutorial.pdf](https://zhijing-jin.com/files/papers/2024_CausalLLM_Tutorial.pdf)  
27. Causal Tracing (ROME) — pyvene 0.1.2 documentation, accessed September 17, 2025, [https://stanfordnlp.github.io/pyvene/tutorials/advanced\_tutorials/Causal\_Tracing.html](https://stanfordnlp.github.io/pyvene/tutorials/advanced_tutorials/Causal_Tracing.html)  
28. \[2508.12495\] Mitigating Hallucinations in Large Language Models via Causal Reasoning, accessed September 17, 2025, [https://arxiv.org/abs/2508.12495](https://arxiv.org/abs/2508.12495)  
29. \[2403.09606\] Large Language Models and Causal Inference in Collaboration: A Survey, accessed September 17, 2025, [https://arxiv.org/abs/2403.09606](https://arxiv.org/abs/2403.09606)  
30. \[2505.10822\] Distilled Circuits: A Mechanistic Study of Internal Restructuring in Knowledge Distillation \- arXiv, accessed September 17, 2025, [https://arxiv.org/abs/2505.10822](https://arxiv.org/abs/2505.10822)  
31. A Simple and Effective Pruning Approach for Large Language Models \- OpenReview, accessed September 17, 2025, [https://openreview.net/forum?id=PxoFut3dWW](https://openreview.net/forum?id=PxoFut3dWW)  
32. locuslab/wanda: A simple and effective LLM pruning approach. \- GitHub, accessed September 17, 2025, [https://github.com/locuslab/wanda](https://github.com/locuslab/wanda)  
33. Understanding Pruning in Large Language Models | by Mukul Ranjan \- Medium, accessed September 17, 2025, [https://medium.com/@mukulranjan/all-about-pruning-and-knowledge-distillation-for-llms-edc705b48916](https://medium.com/@mukulranjan/all-about-pruning-and-knowledge-distillation-for-llms-edc705b48916)  
34. A better path to pruning large language models \- Amazon Science, accessed September 17, 2025, [https://www.amazon.science/blog/a-better-path-to-pruning-large-language-models](https://www.amazon.science/blog/a-better-path-to-pruning-large-language-models)  
35. llama.cpp \- Qwen, accessed September 17, 2025, [https://qwen.readthedocs.io/en/latest/quantization/llama.cpp.html](https://qwen.readthedocs.io/en/latest/quantization/llama.cpp.html)  
36. How To Use SQL Server FILESTREAM Feature For Large Databases \- Red9, accessed September 17, 2025, [https://red9.com/blog/sql-server-filestream-for-large-databases/](https://red9.com/blog/sql-server-filestream-for-large-databases/)  
37. Large Filestream database and backup : r/SQLServer \- Reddit, accessed September 17, 2025, [https://www.reddit.com/r/SQLServer/comments/lgi69i/large\_filestream\_database\_and\_backup/](https://www.reddit.com/r/SQLServer/comments/lgi69i/large_filestream_database_and_backup/)  
38. Filestream with large amount of files – SQLServerCentral Forums, accessed September 17, 2025, [https://www.sqlservercentral.com/forums/topic/filestream-with-large-amount-of-files](https://www.sqlservercentral.com/forums/topic/filestream-with-large-amount-of-files)  
39. Server Configuration: clr enabled \- SQL \- Microsoft Learn, accessed September 17, 2025, [https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/clr-enabled-server-configuration-option?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/clr-enabled-server-configuration-option?view=sql-server-ver17)  
40. CLR Integration Code Access Security \- SQL Server | Microsoft Learn, accessed September 17, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/clr-integration/security/clr-integration-code-access-security?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/clr-integration/security/clr-integration-code-access-security?view=sql-server-ver17)  
41. SQL Server Security: Best Practices with CLR \- Straight Path Solutions, accessed September 17, 2025, [https://straightpathsql.com/archives/2024/06/sql-server-security-best-practices-with-clr/](https://straightpathsql.com/archives/2024/06/sql-server-security-best-practices-with-clr/)  
42. Orfeous/llamacpp.net: C\#/.NET binding of llama.cpp \- GitHub, accessed September 17, 2025, [https://github.com/Orfeous/llamacpp.net](https://github.com/Orfeous/llamacpp.net)  
43. Use ChatSession \- LLamaSharp Documentation, accessed September 17, 2025, [https://scisharp.github.io/LLamaSharp/0.11.2/Tutorials/ChatSession/](https://scisharp.github.io/LLamaSharp/0.11.2/Tutorials/ChatSession/)  
44. LLamaSharp: Run a ChatGPT like system on your hardware for dummies \- Code Inside Blog, accessed September 17, 2025, [https://blog.codeinside.eu/2024/05/15/llamasharp-run-a-chatgpt-like-system-on-your-hardware-for-dummies/](https://blog.codeinside.eu/2024/05/15/llamasharp-run-a-chatgpt-like-system-on-your-hardware-for-dummies/)  
45. Getting Started with LlamaSharp \- Explore your world, accessed September 17, 2025, [https://blog.chuckbeasley.com/321/](https://blog.chuckbeasley.com/321/)  
46. How to Use llama.cpp to Run LLaMA Models Locally \- Codecademy, accessed September 17, 2025, [https://www.codecademy.com/article/llama-cpp](https://www.codecademy.com/article/llama-cpp)  
47. Knowledge Distillation for LLMs: Techniques and Applications | by Yugank .Aman | Medium, accessed September 17, 2025, [https://medium.com/@yugank.aman/knowledge-distillation-for-llms-techniques-and-applications-e23a17093adf](https://medium.com/@yugank.aman/knowledge-distillation-for-llms-techniques-and-applications-e23a17093adf)