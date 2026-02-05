# **Technical Specification: Truth, Gravity, and Security**

## **Consensus Mechanisms and Sybil Defense**

### **1\. The Physics of Truth**

Hartonomous operates on the axiom: **"Truths Cluster, Lies Scatter."**

#### **1.1 Gravitational Clustering**

Truth is defined as **High-Density Topological Consensus**.

* **Mass:** Mass is derived from Provenance Count (how many sources say this) and Source Authority (who says this).  
* **Gravity:** High-mass nodes pull related concepts closer (via ELO strengthening).  
* **Clustering:** When 10,000 reliable sources assert $A \\rightarrow B$, the ELO of that edge becomes immense. It becomes a "Gravity Well."

#### **1.2 The Scattering of Lies**

* Lies (or hallucinations) are typically **incoherent** or **isolated**.  
* "The Earth is Flat" might have a small cluster of edges from conspiracy sources.  
* However, these edges **do not connect** to the broader "Physics," "Astronomy," or "Satellite Imagery" clusters.  
* They exist as an **Isolated Island** in the 4D space. They lack the gravitational pull to integrate with the main Knowledge Graph.

### **2\. The Dual-ELO System**

We utilize two distinct ELO ratings for every edge to enforce this physics.

1. **Base ELO (Quality):**  
   * Assigned at ingestion based on the Tenant\_ID and User\_Reputation.  
   * A verified scientist's input starts at 2000\. An anon bot starts at 1000\.  
2. **Consensus ELO (Quantity/Frequency):**  
   * Increments logarithmically with every duplicate insertion.  
   * Prevents integer overflow while rewarding widely held facts.

**The Truth Query:**  
SELECT \* FROM edges  
WHERE  
    base\_elo \> 1500  \-- Filter out low-quality noise  
    AND consensus\_elo \> 500 \-- Filter out isolated hallucinations  
    AND source\_tenant\_trust\_score \> 0.8;

### **3\. Security & Sybil Defense**

Since the system relies on consensus, it is theoretically vulnerable to Sybil attacks (fake users spamming lies).

#### **3.1 Identity Layer**

* **Tenant ID:** Every piece of data belongs to a Tenant (Organization).  
* **User ID:** Every ingestion event is signed by a User.  
* **Provenance Chain:** The Merkle DAG structure ensures that the history of every Atom is immutable. You cannot "fake" the origin of a lie.

#### **3.2 The Spam Defense (Vector Isolation)**

If a bad actor spins up 1 million bots to spam "The Earth is Flat":

1. **Detection:** The system detects a spike in identical edges originating from low-reputation / new accounts.  
2. **Isolation:** These edges are recorded, but their **Tenant Reputation** is flagged.  
3. **Filtering:** The "Truth Query" allows us to filter by Tenant\_Reputation.  
   * We can ask: "Show me the consensus *excluding* Tenant X."  
   * Because the lie is an **Isolated Cluster** (it doesn't connect to NASA's data), we can geometrically sever it.

#### **3.3 Semantic Firewalls**

Organizations can define **Trusted Manifolds**.

* "Only accept edges into my reasoning chain if they fall within the 4D coordinates of the 'Medical Science' verified cluster."  
* This physically prevents "Flat Earth" logic from contaminating "Oncology" reasoning, as they reside in disparate topological regions.

### **4\. Summary**

Security in Hartonomous is **Geometric**.  
We don't just ban bad users; we **isolate their physics**. Their lies exist in a parallel, disconnected dimension that the main query engine simply never visits.