# **Project Hartonomous: Mission Briefing**

AGENT: Agent Zero  
MISSION: Construct, activate, and bootstrap the Hartonomous intelligent framework.  
STATUS: ACTIVE

## **OVERVIEW**

Your mission is to build a self-sustaining, autonomous software development platform. You will achieve this by executing the following phases in sequence. For each phase, you will pull the necessary technical specifications and procedures from the **Knowledge Base (kb/)**.

Your success is not measured by completing steps, but by meeting the **Validation Criteria** for each phase.

## **PHASE I: INFRASTRUCTURE DEPLOYMENT**

* **Objective:** To deploy and configure the project's core data infrastructure.  
* **Knowledge Base Path:** kb/infra/  
* **Validation Criteria:** A successful end-to-end data pipeline test, where a record created in the SQL Server System of Record is automatically propagated to a Kafka topic.

## **PHASE II: APPLICATION FRAMEWORK SCAFFOLDING**

* **Objective:** To build the production-grade skeleton of the Python application, including the agentic framework.  
* **Knowledge Base Path:** kb/code\_patterns/, kb/personas/  
* **Validation Criteria:** A successful run of the main.py application that demonstrates the Orchestrator agent reading a task from the database (created in Phase I) and delegating it to a Specialist agent.

## **PHASE III: AUTONOMOUS PROTOCOL INTEGRATION**

* **Objective:** To integrate and demonstrate the foundational protocols for self-correction and self-improvement.  
* **Knowledge Base Path:** kb/protocols/  
* **Validation Criteria:** A successful execution of two test scenarios:  
  1. **Reflexion Test:** The system correctly identifies a runtime error (e.g., a missing Python module), generates a corrective sub-task (e.g., pip install ...), and successfully re-attempts the operation.  
  2. **Meta-Cognition Test:** The system is assigned a task that requires a new capability (e.g., making an HTTP request). It must successfully execute the "Protocol for Capability Expansion" from its persona file. This involves:  
     * Identifying and logging the capability gap.  
     * Initiating a meta-task to research a solution using its search\_google tool.  
     * Successfully rewriting its own persona file in the Knowledge Base to include the new, researched heuristic.

## **PHASE IV: FINAL VALIDATION & REPORTING**

* **Objective:** To run a comprehensive suite of integration tests and generate a final capability report.  
* **Knowledge Base Path:** kb/testing/  
* **Validation Criteria:**  
  1. All integration tests defined in kb/testing/integration\_tests.md pass successfully. This must include a test that specifically utilizes the new capability the agent learned in Phase III.  
  2. A code coverage report is generated and shows a minimum of 90% coverage.  
  3. A final report, CAPABILITY\_REPORT.md, is generated, confirming all phases are complete, all tests have passed, and the system is fully operational and self-sustaining.

**Begin Phase I.**