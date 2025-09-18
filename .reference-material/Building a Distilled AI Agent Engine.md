

# **A Functional Blueprint for the Hartonomous Distillation Engine**

## **Executive Summary: The Hartonomous Distillation Engine Paradigm**

### **Introduction to White-Box Distillation**

The Hartonomous Distillation Engine represents a paradigm shift in the creation of specialized artificial intelligence agents. Traditional knowledge distillation (KD) techniques compress large "teacher" models into smaller "student" models by training the student to mimic the teacher's input-output behavior.1 This approach, while effective for model compression, treats the teacher model as an opaque black box, transferring its predictive capabilities but not its underlying reasoning processes. The Hartonomous Engine, in contrast, pioneers a "white-box" or "mechanistic" distillation methodology. This process involves a deep, systematic reverse-engineering of the teacher foundation model's internal computational mechanisms. By identifying and extracting its core building blocks—interpretable features and causal circuits—the engine constructs a verifiable and auditable knowledge base. It is from this transparent cognitive map that specialized agents are forged, ensuring their capabilities are not merely inherited but fundamentally understood.

### **The Architectural Trinity: Interpretability, Orchestration, and Indexing**

The engine's innovation is built upon the synthesis of three distinct technological pillars, each addressing a critical aspect of creating transparent and robust AI:

1. **Mechanistic Interpretability:** At its core, the engine leverages cutting-edge techniques to deconstruct the foundation model's cognition. Sparse Autoencoders (SAEs) are employed to disentangle the model's dense, polysemantic neural activations into a sparse set of monosemantic, human-understandable features.3 Following feature extraction, Causal Tracing and attribution graph methods are used to map the computational circuits—the pathways of interaction between features that give rise to specific behaviors, such as multi-step reasoning.5  
2. **Database-Centric Orchestration:** The entire multi-stage distillation pipeline is orchestrated not by conventional Python-based workflow managers, but by the transactional and robust environment of SQL Server 2025\. Utilizing new capabilities for calling external REST APIs directly from T-SQL, SQL Server acts as an active workflow engine, managing the complex sequence of inference, interpretation, and synthesis with the integrity and reliability inherent to a mature relational database system.6  
3. **Graph-Based Knowledge Indexing:** The abstract, internal "mind" of the foundation model is transformed into a concrete, queryable knowledge graph within a Neo4j database. This process models the extracted features and their causal relationships as a network of nodes and edges, turning opaque neural pathways into a structured, analyzable cognitive map that can be interrogated to understand the model's reasoning.9

### **Core Value Proposition**

The primary objective of the Hartonomous Distillation Engine is to produce specialized AI agents that are not only efficient and tailored to specific domains but are also fundamentally transparent and verifiable. By building agents from an explicit map of the parent model's internal logic, the final agent's reasoning can be traced back to specific, human-interpretable circuits. This traceability directly addresses the critical industry needs for AI safety, alignment, and explainability, moving beyond performance benchmarks to deliver systems whose internal decision-making processes can be audited, debugged, and trusted.11

## **System Architecture and Component Blueprint**

### **High-Level Architectural Diagram**

The Hartonomous Engine is a distributed system composed of five core, independently deployable components that communicate via a standardized REST API interface. This modular architecture allows for scalability and maintainability, with a central database acting as the orchestrator.

* **Component 1: Foundation Model Inference Host:** A server running llama.cpp exposes an OpenAI-compatible REST API. Its primary functions are to perform text generation and, critically, to capture and return the raw internal activations from specified model layers for a given input prompt.  
* **Component 2: Interpretability Layer:** A suite of Python-based microservices, wrapped in a REST API. This layer is responsible for training Sparse Autoencoders on the collected activations and performing Causal Tracing analysis to discover computational circuits.  
* **Component 3: Neo4j Knowledge Graph Index:** The central repository for the model's cognitive architecture. It stores the extracted features, circuits, and their causal relationships as a graph, accessible via its own REST API for data ingestion and querying.  
* **Component 4: SQL Server 2025 Orchestration Engine:** The system's central nervous system. It uses T-SQL stored procedures and the sp\_invoke\_external\_rest\_endpoint function to manage the entire distillation workflow, calling the other components in a predefined, transactional sequence.  
* **Component 5: Agent Synthesis Module:** A service, also exposed via a REST API, that performs either circuit-informed knowledge distillation or targeted pruning to create the final, specialized agent model file based on the knowledge stored in the Neo4j graph.

### **Data Flow and Process Choreography**

The architecture facilitates a novel "AI Factory" pattern where a relational database serves as the central orchestrator for a complex, asynchronous machine learning workflow. This represents a significant departure from typical Python-centric orchestration frameworks. The choice is deliberate; the new sp\_invoke\_external\_rest\_endpoint feature in SQL Server 2025 8 allows T-SQL to directly invoke the containerized Python services that constitute the engine's functional components. This design pattern confers benefits not typically associated with ML pipelines, such as transactional control (e.g., an entire job can be rolled back if a critical API call fails), robust and centralized logging (every API call and its result can be logged to a SQL table), and enhanced security through SQL Server's native credential management. This approach leverages the decades-long maturity and reliability of RDBMS technology for orchestrating a cutting-edge AI workflow.  
The end-to-end process is as follows:

1. A user initiates a distillation job by calling a master stored procedure in SQL Server, providing a corpus of domain-specific text.  
2. The SQL orchestrator iteratively calls the **Inference Host** to process the text, retrieve the internal activations, and store them in a staging table.  
3. Once data collection is complete, SQL Server calls the **Interpretability Layer** to train SAEs on the stored activations, extracting monosemantic features.  
4. The orchestrator then triggers the Causal Tracing service within the same layer to identify the circuits (causal relationships between features).  
5. With the features and circuits identified, SQL Server calls a service to populate the **Neo4j Knowledge Graph**, translating the interpretability results into a structured graph format.  
6. Finally, the orchestrator invokes the **Agent Synthesis Module**, which queries the Neo4j graph for domain-relevant circuits and uses this information to distill or prune a new, specialized model.  
7. The final GGUF model file is stored, and its path is recorded in the SQL Server job table, marking the completion of the process.

## **The Foundation Model: Hardware-Aware Inference on Constrained Systems**

### **Foundation Model Selection**

The selection of the foundation model is a critical first step, balancing raw capability with the practical constraints of the target hardware. The ideal candidate must be a powerful, general-purpose transformer model 12 that is compatible with the  
llama.cpp ecosystem for efficient inference. Models from the Llama 3.1, Mistral, or Gemma families are strong contenders due to their open-source nature and robust community support.13 The chosen model should be large enough to contain a rich diversity of complex knowledge circuits, yet not so large that it cannot be run effectively on the specified hardware after quantization. A model in the 7 to 13 billion parameter range represents the optimal starting point for this architecture.14

### **Model Quantization Protocol with GGUF**

Given the 8GB VRAM constraint of an NVIDIA RTX 4060, quantization is not optional; it is a mandatory requirement. A 7B parameter model stored in standard 16-bit floating-point (FP16) precision requires approximately 14GB of memory and will not fit.14 The GGUF format, used by  
llama.cpp, provides a robust solution for model compression through quantization.15  
The quantization process involves using the quantize utility provided with llama.cpp.15 The protocol is as follows:

1. Download the full-precision model weights from a source like Hugging Face.  
2. Convert the model to the GGUF FP16 format using the convert.py script.  
3. Apply the desired quantization method (e.g., q4\_k\_m) using the quantize executable.

The choice of quantization level involves a direct trade-off between model size and performance. Lower-bit quantization reduces the memory footprint but can degrade model accuracy. An empirical analysis is required to select the optimal level for the Hartonomous Engine.

| Quantization Method | Model Size (GB) | VRAM Usage (GB) | System RAM Usage (GB) | Tokens/Second | Perplexity (Wikitext2) |
| :---- | :---- | :---- | :---- | :---- | :---- |
| FP16 (Baseline) | 14.1 | \> 8 (Infeasible) | N/A | N/A | 4.85 |
| Q6\_K | 5.9 | 6.2 | 12.8 | \~18 | 4.98 |
| Q5\_K\_M | 5.1 | 5.4 | 12.8 | \~22 | 5.05 |
| Q4\_K\_M | 4.3 | 4.6 | 12.8 | \~26 | 5.18 |

*Table 1: Foundation Model Quantization and Performance Benchmarks. Results are hypothetical for a 7B model on target hardware, with 50% layer offload to GPU. Perplexity measures how well the model predicts a sample of text; lower is better.*  
Based on this analysis, Q5\_K\_M offers a superior balance of performance preservation and resource usage, fitting comfortably within the 8GB VRAM limit while incurring only a minor increase in perplexity compared to the baseline.16

### **Configuration of the llama.cpp Server**

The llama.cpp server will be configured for hybrid CPU/GPU execution to leverage both the limited VRAM and the large system RAM. This is achieved by setting the \-ngl (number of GPU layers) parameter. For a 7B model and an 8GB GPU, approximately half of the model's layers can be offloaded to VRAM, with the remainder being processed by the CPU, utilizing the available 192GB+ of system RAM.14  
The server will be started with flags to expose an OpenAI-compatible REST API, which standardizes communication with the SQL Server orchestrator. A crucial customization will be the implementation of an additional, non-standard API endpoint, /get\_activations. This endpoint will accept a prompt, a list of layer indices, and a token position, and will return the raw activation vectors from those locations in the model's computational graph. This endpoint is the primary data source for the entire interpretability pipeline.

## **The Interpretability Layer: Deconstructing Model Cognition**

### **Feature Extraction via Sparse Autoencoders (SAEs)**

A fundamental challenge in understanding neural networks is that individual neurons are often *polysemantic*, activating for multiple unrelated concepts. This is a consequence of *superposition*, where the model learns more features than it has neurons to store them.4 Sparse Autoencoders are a key technique to resolve this issue. An SAE is a simple neural network trained to reconstruct the LLM's activation vectors, but with a crucial constraint: its internal hidden layer is much larger than the input dimension and is forced to be  
*sparse*. This forces the SAE to learn a basis of *monosemantic features*, where each dimension in its hidden layer corresponds to a single, human-understandable concept.3  
The implementation will use an open-source library such as SAELens or e2e\_sae.18 The process is as follows:

1. A large, diverse text corpus is fed through the foundation model via the /get\_activations endpoint to collect millions of activation vectors from target layers (e.g., the MLP sub-layers).  
2. An SAE is trained for each target layer on this dataset of activations.  
3. The trained SAEs are validated on metrics of reconstruction loss (how well they can reproduce the original activation) and feature sparsity (the average number of active features for a given input).  
   The final output is a "feature dictionary" for each layer—a set of vectors where each vector represents a distinct concept, such as "the Golden Gate Bridge" or "a Python for loop".4

### **Circuit Discovery via Causal Tracing**

With a vocabulary of interpretable features, the next step is to map their interactions. Causal tracing and attribution graph techniques are used to discover the computational *circuits*—subgraphs of interacting features that implement specific behaviors.5  
The methodology combines SAEs with causal intervention:

1. **Causal Tracing:** A library like causal-tracer 22 is used to identify the specific layers and token positions in a prompt that are causally responsible for a particular output. For example, in the prompt "The capital of the state containing Dallas is", tracing would identify the activations at the token "Dallas" in early layers and at "capital" in mid-layers as critical for producing the answer "Austin".  
2. **Attribution with SAEs:** Instead of analyzing the raw, polysemantic activations at these critical locations, they are projected into the sparse, monosemantic feature space using the trained SAEs. This reveals precisely *which interpretable features* were causally necessary for the computation.  
3. **Graph Construction:** By repeating this process over thousands of examples, a causal graph of influence between features is constructed. This analysis might reveal, for instance, that the feature for "Dallas" reliably activates a feature for "Texas," which, when co-occurring with a feature for "capital city," strongly activates the features that lead to the output token "Austin".5 This discovered subgraph of causally linked features  
   *is* the circuit.

A critical, non-obvious technical consideration arises from the interaction between quantization and interpretability. The entire engine relies on a quantized GGUF model to function on the target hardware. However, quantization inherently alters the model's weights and, consequently, the activation values that flow through the network.23 Mechanistic interpretability techniques are highly sensitive to these precise activation values. Running SAEs and causal tracing on the native, low-precision activations of the quantized model could produce distorted or invalid results, leading to the discovery of "phantom" circuits or the failure to identify real ones. To ensure the scientific validity of the entire process, the  
llama.cpp server's /get\_activations endpoint must be configured to *de-quantize* the activations back to a higher precision (FP16 or FP32) before they are returned to the Interpretability Layer. This adds a minor computational overhead but is essential for generating a high-fidelity map of the model's cognition.

## **The Knowledge Graph Index: A Neo4j Representation of an AI's Mind**

### **Graph Schema Design**

To transform the abstract findings of the interpretability layer into a queryable asset, a formal graph schema is defined for the Neo4j database. This schema models the fundamental components of the LLM's cognitive architecture.

| Element Type | Label / Type | Properties / Description |
| :---- | :---- | :---- |
| Node | Layer | layer\_index (int), type (string: 'MLP', 'Attention'). Represents a layer in the transformer architecture. |
| Node | SAE\_Feature | feature\_id (int), layer\_index (int), description (string), sparsity\_score (float). Represents a single monosemantic feature. |
| Node | Token | token\_id (int), text (string). Represents a token from the model's vocabulary. |
| Node | Circuit | circuit\_id (string), name (string), description (string), domain (string). Represents a discovered computational subgraph. |
| Relationship | HAS\_FEATURE | Connects a (:Layer) to its (:SAE\_Feature) nodes. |
| Relationship | ACTIVATES\_ON | Connects a (:SAE\_Feature) to a (:Token) that strongly activates it. |
| Relationship | CAUSALLY\_INFLUENCES | A directed, weighted relationship between (:SAE\_Feature) nodes. weight (float) property represents causal strength. |
| Relationship | PART\_OF | Connects (:SAE\_Feature) nodes to the (:Circuit) they belong to. |

*Table 2: Neo4j Knowledge Graph Schema Definition. This schema provides the formal data model for storing the foundation model's internal mechanisms.*

### **Graph Population and Interrogation**

The SQL Server orchestrator populates this graph by calling a dedicated Python service. This service receives the feature and circuit data from the Interpretability Layer, translates it into a series of Cypher CREATE and MERGE statements, and executes them against the Neo4j database's REST API.  
Once populated, this graph becomes a powerful tool for analyzing the model's internal workings. Complex cognitive processes can be explored with simple Cypher queries:

* **Knowledge Discovery:** To list all interpretable features involved in circuits related to medical diagnosis:  
  Cypher  
  MATCH (c:Circuit {domain: 'Medical Diagnosis'})--\>(f:SAE\_Feature)  
  RETURN f.description, f.sparsity\_score

* **Causal Path Analysis:** To find the reasoning pathways that connect the concept of a "fever" to the concept of an "antibiotic prescription":  
  Cypher  
  MATCH path \= (start:SAE\_Feature {description: 'symptom of fever'})  
               \--\>  
               (end:SAE\_Feature {description: 'concept of antibiotic prescription'})  
  RETURN path

* **Universality Analysis:** Graph similarity algorithms within Neo4j's Graph Data Science library can be used to compare the structure of circuits discovered in different models, providing empirical evidence for or against hypotheses like "Analogous Feature Universality," which posits that different LLMs learn similar conceptual representations.25

## **The Orchestration Engine: T-SQL as the Central Nervous System**

### **API Exposure of Python Components**

To enable orchestration by SQL Server, all Python-based components (inference, SAE training, causal tracing, graph population) must be wrapped in a simple, stateless REST API. The FastAPI framework is recommended for this purpose due to its high performance and ease of use.26 Key endpoints will include:  
/generate, /get\_activations, /train\_sae, /run\_causal\_trace, and /populate\_neo4j. Each endpoint will be designed to accept a JSON payload and return a JSON response, a format that SQL Server 2025 can natively handle.

### **T-SQL Workflow Implementation**

The core of the orchestration logic resides in a suite of T-SQL stored procedures within a dedicated SQL Server database. The first step is to enable the feature:  
EXEC sp\_configure 'external rest endpoint enabled', 1; RECONFIGURE;.6  
To manage security, API keys and other secrets will not be hardcoded in scripts. Instead, they will be stored securely using DATABASE SCOPED CREDENTIAL objects. This allows T-SQL to reference credentials by name, which SQL Server then injects into the outgoing HTTP requests.6  
The workflow is implemented as a chain of stored procedures, with each procedure responsible for a specific stage of the distillation process. The new sp\_invoke\_external\_rest\_endpoint procedure is the workhorse, used to call the external Python services. The JSON responses from these services are parsed natively within T-SQL using the OPENJSON function, allowing the results to be easily inserted into logging and tracking tables.28

| Workflow Step | T-SQL Stored Procedure | External API Endpoint Called | Input Parameters | Output / Action |
| :---- | :---- | :---- | :---- | :---- |
| 1\. Job Initiation | usp\_StartDistillationJob | N/A | @DomainCorpus (text) | Creates a new job record in the DistillationJobs table. |
| 2\. Activation Collection | usp\_GetModelActivations | POST /get\_activations | @JobId, @LayerIndices | Iterates through the corpus, calls the API, parses JSON response, and stores activations in ActivationData table. |
| 3\. SAE Training | usp\_TrainSAEs | POST /train\_sae | @JobId | Triggers SAE training on the collected activations. Stores model paths in SAEModels table. |
| 4\. Circuit Discovery | usp\_DiscoverCircuits | POST /run\_causal\_trace | @JobId, @TargetBehaviors | Initiates causal tracing analysis to identify circuits. |
| 5\. Knowledge Indexing | usp\_PopulateKnowledgeGraph | POST /populate\_neo4j | @JobId | Sends extracted features and circuits to be indexed in the Neo4j graph. |
| 6\. Agent Synthesis | usp\_SynthesizeAgent | POST /synthesize\_agent | @JobId, @SynthesisMethod ('distill' or 'prune') | Triggers the creation of the final specialized agent. Stores the model file path in the DistillationJobs table. |

*Table 3: T-SQL Orchestration Procedure Mapping. This table provides the operational guide for the engine, linking the conceptual workflow to the concrete T-SQL implementation.*

## **Agent Synthesis: Forging Specialized Models from Distilled Knowledge**

### **Circuit-Informed Knowledge Distillation**

This method creates a new, smaller "student" model from scratch, using a novel distillation approach that leverages the knowledge graph. Standard knowledge distillation trains a student to match the teacher's final output probabilities (soft labels).1 Circuit-informed distillation adds a crucial second objective: forcing the student to learn the  
*same internal reasoning pathways* as the teacher.  
The methodology is as follows:

1. Query the Neo4j graph to retrieve all circuits and their constituent features relevant to the target specialization (e.g., "Legal Argumentation").  
2. Train a compact student model using a composite loss function. This function includes the standard cross-entropy loss against the teacher's outputs.  
3. Crucially, it also includes an auxiliary "circuit-matching" loss. During training, the student's internal activations are passed through the teacher's SAE encoder to project them into the teacher's feature space. The loss term then penalizes deviations between the student's feature activations and the target activation patterns of the key circuits identified in step 1\. This explicitly teaches the student to emulate the teacher's internal "thought processes".32

### **Specialization via Targeted Pruning (Ablation)**

An alternative to distillation is to start with a full copy of the quantized foundation model and surgically remove unwanted capabilities. This "precision surgery" uses the interpretability results to make the model more specialized.33  
The methodology is:

1. Query the Neo4j graph to identify all circuits and features that are *not* related to the target domain.  
2. Perform targeted ablation on a copy of the GGUF model. This involves programmatically setting the weights corresponding to the irrelevant features and circuits to zero.34  
3. Modifying the weights of a quantized GGUF model is a non-trivial operation. The proposed workflow is to de-quantize the specific weight tensors identified for pruning, perform the zeroing-out operation in full precision, and then re-quantize only the modified tensors. This surgical approach preserves the efficiency of the original model while removing its capacity for out-of-domain tasks.

This pruning approach is faster than full distillation but may result in a sparse model that is less computationally efficient at inference time compared to a dense, purpose-built distilled model. The choice between methods depends on the trade-off between development time and final agent performance.

## **Conclusion and Future Trajectories**

### **Summary of the Hartonomous Engine**

The Hartonomous Distillation Engine provides a comprehensive, verifiable, and hardware-aware blueprint for creating specialized AI agents. Its unique architecture, which weds mechanistic interpretability with database-centric orchestration and graph-based knowledge indexing, offers a path toward AI systems that are not only powerful but also transparent and auditable. By deconstructing a foundation model into its fundamental computational components and using that knowledge to forge new agents, the engine moves beyond the limitations of black-box model creation, establishing a new standard for controllable and trustworthy AI.

### **Future Research Directions**

The framework established by the Hartonomous Engine opens several promising avenues for future research and development:

* **Automated Circuit Labeling:** While SAEs can discover monosemantic features, assigning them clear, human-readable descriptions often requires manual effort. Future work could focus on developing more sophisticated methods to automatically generate high-quality labels for both features and the circuits they form, further scaling the interpretability process.  
* **Dynamic and Continual Distillation:** The current blueprint describes a batch process. A future iteration could create a dynamic system where the distillation process is continuous. As the parent foundation model is fine-tuned or updated, the knowledge graph could be incrementally updated, allowing the specialized agents to evolve and incorporate new knowledge automatically.  
* **Proactive Safety and Alignment Audits:** The Neo4j knowledge graph represents a detailed map of the model's potential behaviors. This map can be proactively audited to identify potentially harmful, biased, or undesirable circuits.5 The targeted pruning methodology could then be used to surgically ablate these circuits, providing a powerful, interpretability-driven tool for enhancing AI safety and alignment before a model is deployed.

#### **Works cited**

1. Understanding Knowledge Distillation: From Hinton's Paper to OpenAI's Mini Models | by Asim Adnan Eijaz | Medium, accessed September 17, 2025, [https://medium.com/@asimadnan/understanding-knowledge-distillation-from-hintons-paper-to-openai-s-mini-models-e5a761b0dc47](https://medium.com/@asimadnan/understanding-knowledge-distillation-from-hintons-paper-to-openai-s-mini-models-e5a761b0dc47)  
2. Knowledge distillation \- Wikipedia, accessed September 17, 2025, [https://en.wikipedia.org/wiki/Knowledge\_distillation](https://en.wikipedia.org/wiki/Knowledge_distillation)  
3. A Survey on Sparse Autoencoders: Interpreting the Internal ... \- arXiv, accessed September 17, 2025, [https://arxiv.org/abs/2503.05613](https://arxiv.org/abs/2503.05613)  
4. Understanding LLMs: Insights from Mechanistic ... \- LessWrong, accessed September 17, 2025, [https://www.lesswrong.com/posts/XGHf7EY3CK4KorBpw/understanding-llms-insights-from-mechanistic](https://www.lesswrong.com/posts/XGHf7EY3CK4KorBpw/understanding-llms-insights-from-mechanistic)  
5. On the Biology of a Large Language Model, accessed September 17, 2025, [https://transformer-circuits.pub/2025/attribution-graphs/biology.html](https://transformer-circuits.pub/2025/attribution-graphs/biology.html)  
6. 11 Aug SQL Server 2025's REST API Feature: Powerful Integration or Security Risk? \- Monin, accessed September 17, 2025, [https://monin-it.be/sql-server-2025s-rest-api-feature-powerful-integration-or-security-risk/](https://monin-it.be/sql-server-2025s-rest-api-feature-powerful-integration-or-security-risk/)  
7. T-SQL REST API Integration in SQL Server 2025: Streamlining T-SQL Snapshot Backups, accessed September 17, 2025, [https://www.nocentino.com/posts/2025-05-19-t-sql-rest-api-integration-in-sql-server-2025-streamlining-t-sql-snapshot-backups/](https://www.nocentino.com/posts/2025-05-19-t-sql-rest-api-integration-in-sql-server-2025-streamlining-t-sql-snapshot-backups/)  
8. sp\_invoke\_external\_rest\_endpoint (Transact-SQL) \- SQL Server ..., accessed September 17, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-invoke-external-rest-endpoint-transact-sql?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-invoke-external-rest-endpoint-transact-sql?view=sql-server-ver17)  
9. A Graph Neural Network to approximate Network Centrality metrics in Neo4j \- Medium, accessed September 17, 2025, [https://medium.com/neo4j/a-graph-neural-network-to-approximate-network-centralities-in-neo4j-2ee96705a464](https://medium.com/neo4j/a-graph-neural-network-to-approximate-network-centralities-in-neo4j-2ee96705a464)  
10. Generative AI \- Ground LLMs with Knowledge Graphs \- Neo4j, accessed September 17, 2025, [https://neo4j.com/generativeai/](https://neo4j.com/generativeai/)  
11. Exploring Mechanistic Interpretability in Large Language Models: Challenges, Approaches, and Insights \- ResearchGate, accessed September 17, 2025, [https://www.researchgate.net/publication/392330791\_Exploring\_Mechanistic\_Interpretability\_in\_Large\_Language\_Models\_Challenges\_Approaches\_and\_Insights](https://www.researchgate.net/publication/392330791_Exploring_Mechanistic_Interpretability_in_Large_Language_Models_Challenges_Approaches_and_Insights)  
12. Large language model \- Wikipedia, accessed September 17, 2025, [https://en.wikipedia.org/wiki/Large\_language\_model](https://en.wikipedia.org/wiki/Large_language_model)  
13. Running LLMs Locally on Consumer Devices \- IJRASET, accessed September 17, 2025, [https://www.ijraset.com/best-journal/running-llms-locally-on-consumer-devices](https://www.ijraset.com/best-journal/running-llms-locally-on-consumer-devices)  
14. A Simple, Practical Guide to Running Large-Language Models on Your Laptop \- Medium, accessed September 17, 2025, [https://medium.com/predict/a-simple-comprehensive-guide-to-running-large-language-models-locally-on-cpu-and-or-gpu-using-c0c2a8483eee](https://medium.com/predict/a-simple-comprehensive-guide-to-running-large-language-models-locally-on-cpu-and-or-gpu-using-c0c2a8483eee)  
15. Quantize Llama models with GGUF and llama.cpp – Maxime Labonne, accessed September 17, 2025, [https://mlabonne.github.io/blog/posts/Quantize\_Llama\_2\_models\_using\_ggml.html](https://mlabonne.github.io/blog/posts/Quantize_Llama_2_models_using_ggml.html)  
16. Quantize Llama models with GGUF and llama.cpp \- Origins AI, accessed September 17, 2025, [https://originshq.com/blog/quantize-llama-models-with-gguf-and-llama-cpp/](https://originshq.com/blog/quantize-llama-models-with-gguf-and-llama-cpp/)  
17. Use Sparse Autoencoders to Discover Unknown Concepts, Not to Act on Known Concepts, accessed September 17, 2025, [https://arxiv.org/html/2506.23845v1](https://arxiv.org/html/2506.23845v1)  
18. ApolloResearch/e2e\_sae: Sparse Autoencoder Training Library \- GitHub, accessed September 17, 2025, [https://github.com/ApolloResearch/e2e\_sae](https://github.com/ApolloResearch/e2e_sae)  
19. jbloomAus/SAELens: Training Sparse Autoencoders on Language Models \- GitHub, accessed September 17, 2025, [https://github.com/jbloomAus/SAELens](https://github.com/jbloomAus/SAELens)  
20. Towards Global-level Mechanistic Interpretability: A Perspective of Modular Circuits of Large Language Models | OpenReview, accessed September 17, 2025, [https://openreview.net/forum?id=do5vVfKEXZ](https://openreview.net/forum?id=do5vVfKEXZ)  
21. Circuits in Transformers Mechanistic Interpretability 2 \- Rohan Hitchcock, accessed September 17, 2025, [https://rohanhitchcock.com/notes/2023-6-slt-alignment-talk-mech-interp.pdf](https://rohanhitchcock.com/notes/2023-6-slt-alignment-talk-mech-interp.pdf)  
22. causal-tracer · PyPI, accessed September 17, 2025, [https://pypi.org/project/causal-tracer/](https://pypi.org/project/causal-tracer/)  
23. GGUF Quantization: Making Large Language Models Accessible to Everyone \- Medium, accessed September 17, 2025, [https://medium.com/@riddhimanghatak/gguf-quantization-making-large-language-models-accessible-to-everyone-9ad6401d8688](https://medium.com/@riddhimanghatak/gguf-quantization-making-large-language-models-accessible-to-everyone-9ad6401d8688)  
24. Mind the Gap: A Practical Attack on GGUF Quantization \- arXiv, accessed September 17, 2025, [https://www.arxiv.org/pdf/2505.23786](https://www.arxiv.org/pdf/2505.23786)  
25. \[2410.06981\] Quantifying Feature Space Universality Across Large Language Models via Sparse Autoencoders \- arXiv, accessed September 17, 2025, [https://arxiv.org/abs/2410.06981](https://arxiv.org/abs/2410.06981)  
26. Streaming local LLM with FastAPI, Llama.cpp and Langchain \- Stack Overflow, accessed September 17, 2025, [https://stackoverflow.com/questions/77867894/streaming-local-llm-with-fastapi-llama-cpp-and-langchain](https://stackoverflow.com/questions/77867894/streaming-local-llm-with-fastapi-llama-cpp-and-langchain)  
27. Using Llama-cpp with FastAPI to connect a html. · Issue \#166 ..., accessed September 17, 2025, [https://github.com/abetlen/llama-cpp-python/issues/166](https://github.com/abetlen/llama-cpp-python/issues/166)  
28. Work with JSON Data in SQL Server \- Microsoft Learn, accessed September 17, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/json/json-data-sql-server?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/json/json-data-sql-server?view=sql-server-ver17)  
29. Using the OPENJSON Function In SQL Server | by SqlInSix Tech Blog | Medium, accessed September 17, 2025, [https://sqlinsix.medium.com/using-the-openjson-function-in-sql-server-deb8a58f6d04](https://sqlinsix.medium.com/using-the-openjson-function-in-sql-server-deb8a58f6d04)  
30. sql server \- Parse JSON in TSQL \- Stack Overflow, accessed September 17, 2025, [https://stackoverflow.com/questions/2867501/parse-json-in-tsql](https://stackoverflow.com/questions/2867501/parse-json-in-tsql)  
31. A Mechanistic Study of Internal Restructuring in Knowledge Distillation \- arXiv, accessed September 17, 2025, [https://arxiv.org/html/2505.10822v1](https://arxiv.org/html/2505.10822v1)  
32. What is Knowledge distillation? | IBM, accessed September 17, 2025, [https://www.ibm.com/think/topics/knowledge-distillation](https://www.ibm.com/think/topics/knowledge-distillation)  
33. Fairness Pruning: Precision Surgery to Reduce Bias in LLMs | Towards Data Science, accessed September 17, 2025, [https://towardsdatascience.com/fairness-pruning-precision-surgery-to-reduce-bias-in-llms/](https://towardsdatascience.com/fairness-pruning-precision-surgery-to-reduce-bias-in-llms/)  
34. Pruning Large Language Models by Identifying and Preserving Functional Networks \- arXiv, accessed September 17, 2025, [https://arxiv.org/html/2508.05239v1](https://arxiv.org/html/2508.05239v1)  
35. Circuit Breaking: Removing Model Behaviors with Targeted Ablation \- arXiv, accessed September 17, 2025, [https://arxiv.org/html/2309.05973v2](https://arxiv.org/html/2309.05973v2)  
36. Towards Interpretable Sequence Continuation: Analyzing Shared Circuits in Large Language Models \- arXiv, accessed September 17, 2025, [https://arxiv.org/html/2311.04131v5](https://arxiv.org/html/2311.04131v5)  
37. \[2503.11232\] PrivacyScalpel: Enhancing LLM Privacy via Interpretable Feature Intervention with Sparse Autoencoders \- arXiv, accessed September 17, 2025, [https://arxiv.org/abs/2503.11232](https://arxiv.org/abs/2503.11232)