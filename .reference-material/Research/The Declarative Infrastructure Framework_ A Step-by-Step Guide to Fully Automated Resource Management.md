

# **The Declarative Infrastructure Framework: A Step-by-Step Guide to Fully Automated Resource Management**

## **Introduction: From Manual Toil to Declarative Automation**

This guide presents a modern framework for infrastructure management, shifting the paradigm from traditional, imperative methods—which rely on manual interventions and complex, brittle scripts—to a fully declarative and automated approach. The core philosophy is to manage the entire lifecycle of cloud resources and application configurations through a single, version-controlled configuration file. This method drastically reduces the potential for human error and establishes a system that is inherently repeatable, auditable, and scalable.

By following this plan, a system administrator will construct a powerful and extensible framework. This framework automates every necessary action, from the initial provisioning of Azure resources like Key Vaults to the intricate configuration of application-level users and permissions. The entire process is initiated by a single command, transforming complex deployments into a streamlined, predictable, and secure operation.

---

## **Section 1: Establishing the Automation Foundation**

The initial phase involves laying the critical groundwork for the entire automation framework. This includes installing the necessary command-line tooling and establishing a secure, non-human identity—a Service Principal—that will execute all automated tasks. This focus on creating a secure and correctly permissioned control plane before any resources are provisioned is a fundamental principle of modern infrastructure management.

### **1.1 Preparing the Control Environment: A Prerequisite Checklist**

Before building the automation script, the local or bastion host environment must be equipped with a specific set of command-line tools. These tools provide the necessary interfaces to interact with Azure APIs and parse the configuration file.

* **Azure Command-Line Interface (CLI):** The Azure CLI is the primary tool for interacting with the Azure Resource Manager API. It is essential for creating, configuring, and managing all cloud resources. It can be installed on most major Linux distributions. After installation, its presence and version can be verified with the command az \--version.1 The initial setup requires an interactive login using  
  az login to configure the local environment. For environments where local installation is not preferred, Azure Cloud Shell provides a pre-configured alternative with the CLI and other tools readily available.1  
* **yq Utility:** The master script will be driven by a YAML configuration file. To parse this file efficiently within a shell script, the yq utility (specifically the Mike Farah version) is required. It provides a powerful, jq-like syntax for querying and manipulating YAML, making it ideal for extracting configuration values.3 It can be installed via common package managers, such as  
  brew install yq on macOS or apt-get install yq on Debian-based systems.6  
* **sqlcmd (Go variant):** As the primary application example in this guide involves SQL Server on Linux, a command-line utility is needed to execute Transact-SQL (T-SQL) scripts non-interactively. The modern, Go-based sqlcmd is the recommended tool. It is a cross-platform utility with advanced authentication capabilities, including support for Microsoft Entra ID service principals, which is critical for our automated workflow.8

### **1.2 The Automation Identity: Creating a Least-Privilege Service Principal**

For any non-interactive automation, using a personal user account is an insecure practice. The correct approach is to create a dedicated machine identity, known in Azure as a Service Principal. This identity will be granted only the minimum permissions required to perform its tasks, adhering to the principle of least privilege.10

This initial decision to use a least-privilege Service Principal is foundational; it establishes a security-first posture that influences every subsequent step in the automation process. A common mistake is to grant broad roles like "Contributor" for convenience, but this creates a significant security liability.11 By starting with a restrictive role, the framework forces a deliberate consideration of permissions, making the entire system more secure and compliant by design.

* **Step-by-Step Creation:** A Service Principal can be created with a single Azure CLI command. The output provides the appId (client ID), password (client secret), and tenant ID, which are the credentials the master script will use to authenticate.2  
  Bash  
  az ad sp create-for-rbac \--name "your-automation-sp-name"

* **Applying the Principle of Least Privilege:** Instead of assigning a broad, built-in role, it is best practice to assign the most specific role necessary for the task. For onboarding servers to Azure Arc, a dedicated role named "Azure Connected Machine Onboarding" exists for this purpose. This role should be scoped to the specific resource group where the machines will be managed, further limiting its potential impact.13  
* **Secure Credential Handling:** The client secret generated during the Service Principal creation is highly sensitive and must be protected. It should never be hardcoded directly into scripts. Initially, it can be stored in environment variables or a local configuration file that is explicitly excluded from version control (e.g., via .gitignore). The master script will later be designed to store and retrieve all secrets from Azure Key Vault, creating a secure, self-managing system.

---

## **Section 2: The Blueprint: The Master config.yaml File**

The cornerstone of this framework is the config.yaml file. It serves as the single source of truth, allowing an administrator to define the desired state of the entire infrastructure declaratively. Its structure is designed to be intuitive, modular, and extensible, abstracting the complexity of the underlying automation script.

### **2.1 Architecting the Configuration Schema**

The YAML schema is logically divided into sections, separating Azure-level concerns from application-specific details. This separation makes the framework easier to understand, maintain, and extend to new applications.

* **azure\_config block:** This section contains the global parameters required to target the correct Azure environment.  
  * subscriptionId: The unique identifier of the Azure subscription.  
  * resourceGroupName: The name of the resource group where all resources will be created.  
  * location: The Azure region for resource deployment (e.g., eastus).  
  * servicePrincipal: Contains the credentials for the automation identity (appId, secret, tenantId). These will be used by the script for the initial az login.  
* **key\_vault block:** This section defines the state of the centralized secrets store.  
  * name: The globally unique name for the Azure Key Vault.  
  * secrets: A list of secret objects to be managed within the vault. Each object has:  
    * name: The name of the secret inside Key Vault.  
    * generation\_method: A directive for the script. Can be autogenerate\_password for the script to create a secure random value, or value\_from\_config to use a predefined value.  
    * value: An optional field to provide a predefined secret value when generation\_method is value\_from\_config.  
* **applications block:** This is the extensible core of the configuration, designed as a list to support multiple, distinct application deployments. The initial example focuses on a SQL Server on a Linux virtual machine.  
  * vmName: The name of the target virtual machine.  
  * adminUsername: The administrative username for the VM.  
  * databases: A list of databases to be created on the SQL Server instance.  
  * roles: A list of custom database roles to be created, embodying the principle of least privilege. Each role object contains:  
    * name: The name of the database role (e.g., app\_readonly).  
    * permissions: A list of permissions to grant to this role, specifying the type (e.g., SELECT) and the schema it applies to (e.g., dbo).

This structured approach transforms the configuration file into more than just a set of variables; it becomes a high-level architectural document. A non-technical stakeholder could review the file and understand the deployed components without needing to interpret the script's logic, effectively bridging the gap between infrastructure design and business requirements.

### **2.2 A Complete config.yaml Template**

The following is a fully commented, ready-to-use template. An administrator only needs to replace the placeholder values to define their environment.

YAML

\# \===================================================================  
\# Master Configuration for Fully Automated Deployment  
\# \===================================================================

\# Section 1: Azure Environment Configuration  
azure\_config:  
  subscriptionId: "\<YOUR\_SUBSCRIPTION\_ID\>"  
  resourceGroupName: "my-automated-rg"  
  location: "eastus"  
  \# Credentials for the automation Service Principal.  
  \# It is recommended to load these from environment variables in production.  
  servicePrincipal:  
    appId: "\<YOUR\_SP\_APP\_ID\>"  
    secret: "\<YOUR\_SP\_SECRET\>"  
    tenantId: "\<YOUR\_TENANT\_ID\>"

\# Section 2: Azure Key Vault and Secrets Management  
key\_vault:  
  \# Must be a globally unique name.  
  name: "kv-myautomated-app-unique-12345"  
  secrets:  
    \# This secret will be generated by the script.  
    \- name: "sql-admin-password"  
      generation\_method: "autogenerate\_password"  
    \# This secret uses a predefined value from this config.  
    \- name: "app-user-password"  
      generation\_method: "value\_from\_config"  
      value: "P@ssw0rdValueFromConfig\!"

\# Section 3: Application-Specific Configurations  
\# This is a list, allowing for multiple applications to be defined.  
applications:  
  \- type: "sql\_server\_on\_linux"  
    vmName: "sql-vm-01"  
    adminUsername: "sqladmin"  
    databases:  
      \- name: "ApplicationDB"  
      \- name: "ReportingDB"  
    \# Define least-privilege database roles and their permissions.  
    roles:  
      \- name: "app\_readonly"  
        permissions:  
          \- type: "SELECT"  
            schema: "dbo"  
      \- name: "app\_readwrite"  
        permissions:  
          \- type: "SELECT"  
            schema: "dbo"  
          \- type: "INSERT"  
            schema: "dbo"  
          \- type: "UPDATE"  
            schema: "dbo"  
          \- type: "DELETE"  
            schema: "dbo"  
          \- type: "EXECUTE"  
            schema: "dbo"

---

## **Section 3: The Engine: Building the master\_setup.sh Script**

The master\_setup.sh script is the automation engine that translates the declarative state defined in config.yaml into reality. It is designed with modularity, safety, and idempotency as core principles, ensuring it can be run repeatedly without causing errors or unintended side effects. The script functions as an orchestrated workflow where each phase builds upon the security and state established by the previous one.

### **3.1 Script Architecture and Safeguards**

A robust shell script begins with a solid foundation to prevent common errors and provide clear feedback.

* **Robust Shell Scripting:** The script should start with set \-euo pipefail. This combination of flags ensures that the script will exit immediately if a command fails (-e), if an unset variable is used (-u), or if a command in a pipeline fails (-o pipefail).  
* **Logging and Verbosity:** Simple functions for logging information, warnings, and errors provide clear, color-coded output. This helps the administrator track the script's progress and quickly identify issues.  
* **Idempotency as a Core Principle:** Every function within the script is designed to be idempotent. This means checking for the existence of a resource before attempting to create it. This ensures that running the script multiple times converges on the same desired state without failure.

### **3.2 Phase 1: Configuration Ingestion and Validation**

The first operational phase involves reading the config.yaml file, validating its contents, and authenticating to Azure.

* **Parsing with yq:** The script uses yq to read values from the configuration file and export them as shell variables. This approach is far more reliable than using text-processing tools like grep or sed, as yq understands the YAML structure.  
  Bash  
  \# Example of reading a simple value  
  AZURE\_RG=$(yq '.azure\_config.resourceGroupName' config.yaml)

  \# Example of reading the service principal secret  
  SP\_SECRET=$(yq '.azure\_config.servicePrincipal.secret' config.yaml)

* **Initial Login:** The script performs a non-interactive login to Azure using the Service Principal credentials loaded from the configuration. This single authentication step provides the context for all subsequent Azure CLI commands.17  
  Bash  
  az login \--service-principal \\  
    \-u "$SP\_APP\_ID" \\  
    \-p "$SP\_SECRET" \\  
    \--tenant "$SP\_TENANT\_ID" \> /dev/null

### **3.3 Phase 2: Idempotent Cloud Resource Provisioning**

This phase creates the foundational Azure resources defined in the configuration. Each creation command is preceded by a check to ensure the operation is idempotent.

* **Resource Group Creation:** The script first checks if the resource group exists using az group show. If the command fails (indicating the group does not exist), it proceeds to create it with az group create.  
* **Key Vault Creation:** A similar pattern is used for the Azure Key Vault. The script checks for its existence with az keyvault show before attempting creation with az keyvault create.1  
* **Key Vault Access Policy (Self-Permissioning):** This is a critical step in the automation's security model. After ensuring the Key Vault exists, the script grants its own Service Principal identity the necessary permissions (secret set, get, list) to manage secrets within that vault. This self-permissioning or bootstrapping step is essential for the next phase and is a hallmark of advanced infrastructure-as-code patterns.19  
  Bash  
  \# Get the Object ID of the currently logged-in Service Principal  
  SP\_OBJECT\_ID=$(az ad sp show \--id "$SP\_APP\_ID" \--query "id" \-o tsv)

  \# Grant the Service Principal access to the Key Vault it may have just created  
  az keyvault set-policy \--name "$KV\_NAME" \\  
    \--object-id "$SP\_OBJECT\_ID" \\  
    \--secret-permissions get list set \> /dev/null

The following table summarizes the core Azure CLI commands used in this provisioning phase.

| Command | Purpose in Script | Idempotency Check Method |
| :---- | :---- | :---- |
| az group create | Creates the primary resource group for all resources. | az group show |
| az keyvault create | Provisions the Azure Key Vault for secure secret storage. | az keyvault show |
| az keyvault set-policy | Grants the automation Service Principal permissions on the Key Vault. | The command is inherently idempotent; re-applying the same policy has no effect. |

### **3.4 Phase 3: Automated Secrets Management**

With the Key Vault provisioned and correctly permissioned, the script can now securely manage all sensitive data.

* **Secret Generation:** A simple Bash function using /dev/urandom and openssl can be used to generate cryptographically secure random passwords for any secret configured with generation\_method: autogenerate\_password.  
* **Storing Secrets:** The script iterates through the secrets list from config.yaml. For each item, it determines whether to generate a new value or use the one provided. It then uses the az keyvault secret set command to store the value in Azure Key Vault. This command is idempotent; if a secret with the same name exists, it simply updates its value, which aligns with the declarative model.20  
* **Retrieving Secrets for Later Use:** When a secret is needed for a subsequent step (like setting a SQL admin password), the script retrieves it securely from Key Vault using az keyvault secret show. The \--query value \-o tsv flags ensure only the secret value itself is returned, ready to be used in another command.19

### **3.5 Phase 4: Application Configuration (SQL Server on Linux Example)**

This final phase demonstrates how the framework orchestrates application-level configuration using the resources and secrets provisioned earlier.

* **Non-interactive Authentication with sqlcmd:** To execute T-SQL scripts non-interactively, the automation Service Principal must first be granted access to the SQL Server instance. This is achieved by creating a database user for the Service Principal in the master database. This user is created FROM EXTERNAL PROVIDER, which links it to the Microsoft Entra ID identity.21 Once this user exists, the script can use  
  sqlcmd with Microsoft Entra ID authentication to run further commands.24  
* **Idempotent T-SQL Execution:** The script dynamically generates a T-SQL script based on the databases and roles sections of the config.yaml. Every CREATE statement is wrapped in an IF NOT EXISTS block to ensure the entire script is idempotent. This prevents errors on subsequent runs if, for example, a database or role already exists. Passwords for new SQL logins are retrieved directly from Key Vault and injected into the T-SQL script.  
* **Least Privilege Database Roles:** Following the definitions in config.yaml, the script creates custom database roles (e.g., app\_readonly) and grants them the minimum necessary permissions on specific schemas (e.g., GRANT SELECT ON SCHEMA::dbo TO app\_readonly). This enforces the principle of least privilege at the application data layer, not just the infrastructure layer.26

The following table provides reusable T-SQL patterns for idempotent security management.

| Object Type | T-SQL Pattern | Notes |
| :---- | :---- | :---- |
| Login | IF NOT EXISTS (SELECT name FROM sys.server\_principals WHERE name \= 'MyLogin') CREATE LOGIN \[MyLogin\] WITH PASSWORD \= '...'; | Checks sys.server\_principals at the server level. |
| Database User | IF NOT EXISTS (SELECT name FROM sys.database\_principals WHERE name \= 'MyUser') CREATE USER \[MyUser\] FOR LOGIN \[MyLogin\]; | Checks sys.database\_principals within the target database context. |
| Database Role | IF NOT EXISTS (SELECT name FROM sys.database\_principals WHERE name \= 'MyRole' AND type \= 'R') CREATE ROLE; | The type \= 'R' clause specifically checks for roles. |
| Schema | IF NOT EXISTS (SELECT name FROM sys.schemas WHERE name \= 'MySchema') EXEC('CREATE SCHEMA MySchema'); | CREATE SCHEMA must be executed in its own batch, hence the EXEC(). |
| Grant Permission | GRANT SELECT ON SCHEMA::dbo TO; | GRANT statements are naturally idempotent; re-granting a permission has no negative effect. |

This modular script architecture is a powerful pattern. The application configuration function can be easily swapped or extended. For instance, to support Milvus, one could add a new function that uses the pymilvus library to idempotently create users and roles as defined in a milvus\_server block in the configuration file.29 The core Azure provisioning and secret management logic would remain unchanged, demonstrating the framework's true reusability.

---

## **Section 4: Execution, Verification, and Maintenance**

This section provides the direct instructions for using the framework, verifying its output, and resolving common issues.

### **4.1 The Administrator's Quick-Start Guide**

The process for an administrator to use this automated framework is designed to be exceptionally simple and aligns with the core requirement of the user query.

1. **Clone the Repository:** Obtain the framework files, including master\_setup.sh and config.yaml.template.  
   Bash  
   git clone \<repository\_url\>

2. **Configure the Environment:** Create a personal copy of the configuration file and edit it to define the desired infrastructure. This is the only manual editing step required.  
   Bash  
   cp config.yaml.template config.yaml  
   nano config.yaml

3. **Execute the Master Script:** Run the single master script to provision and configure all resources.  
   Bash  
   bash./master\_setup.sh

### **4.2 Validating the Deployment**

After the script completes successfully, the administrator can run a series of verification commands to confirm that all resources were created and configured as expected.

* **Azure Resource Verification:**  
  * Confirm the resource group exists: az group show \--name "my-automated-rg"  
  * Verify Key Vault creation: az keyvault show \--name "kv-myautomated-app-unique-12345" 1  
  * List secrets stored in the vault: az keyvault secret list \--vault-name "kv-myautomated-app-unique-12345" 20  
* **Application-Level Verification (SQL Server):**  
  * Use sqlcmd to connect to the server and run queries to list the created databases, users, and roles. This validates that the T-SQL portion of the script executed correctly.26  
  * For services managed by an agent, such as Azure Arc, agent health and connectivity can be verified using commands like azcmagent check.30

### **4.3 Troubleshooting Guide**

Automation scripts can fail for various reasons. This guide provides solutions for the most common issues.

| Error Message Snippet | Likely Cause | Resolution Steps |
| :---- | :---- | :---- |
| AuthorizationFailed | The Service Principal lacks the necessary RBAC permissions on the target scope (e.g., subscription or resource group). | 1\. Verify the roles assigned to the Service Principal in the Azure portal. 2\. Ensure the role (e.g., "Azure Connected Machine Onboarding") is assigned at the correct scope. 3\. Re-run the script after correcting permissions. |
| PrincipalNotFound | A Microsoft Entra ID replication delay. A user or group was created and then immediately used in a role assignment before the change propagated. | 1\. This is often transient. Wait a few minutes and re-run the script. 2\. If using az role assignment create, specify the \--assignee-principal-type (e.g., User, Group) to help the API resolve the identity faster.32 |
| KeyVault Secret not found or Access Denied | The Service Principal does not have the correct access policy on the Key Vault. | 1\. Verify the az keyvault set-policy command in the script executed successfully. 2\. In the Azure portal, check the "Access policies" of the Key Vault and confirm the Service Principal has Get and List permissions for secrets. |
| Connection timed out or Network error | A firewall or Azure Network Security Group (NSG) is blocking traffic from the script's execution environment to Azure endpoints or the target VM. | 1\. Ensure outbound access on port 443 is allowed to Azure domains. 2\. For VM connections, check the NSG rules associated with the VM's network interface and subnet to ensure SSH (port 22\) or SQL (port 1433\) traffic is permitted from the source IP. |

---

## **Conclusion: Extending the Framework**

This guide has detailed the creation of a robust, declarative automation framework. By centralizing all configuration into a single YAML file and using an idempotent master script, this approach delivers a secure, repeatable, and easily manageable infrastructure. The true power of this model lies not in the specific SQL Server example, but in the underlying methodology.

The framework is designed for extension. To add support for another application, such as a Milvus vector database or a Neo4j graph database, an administrator would:

1. Add a new block to the applications list in config.yaml with the parameters specific to that service (e.g., milvus\_config).29  
2. Create a new function in master\_setup.sh (e.g., configure\_milvus()) that reads from this new block.  
3. Implement the idempotent setup commands for the new service within that function, using its native command-line tools or SDKs, while reusing the existing functions for Azure resource provisioning and secret management.33

By following this pattern, the framework can evolve to manage a diverse portfolio of applications, transforming it from a single-purpose solution into a comprehensive and standardized methodology for modern cloud operations.

#### **Works cited**

1. Quickstart: Create a key vault using the Azure CLI \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/key-vault/general/quick-create-cli](https://learn.microsoft.com/en-us/azure/key-vault/general/quick-create-cli)  
2. Create Azure service principals using the Azure CLI | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/cli/azure/azure-cli-sp-tutorial-1?view=azure-cli-latest](https://learn.microsoft.com/en-us/cli/azure/azure-cli-sp-tutorial-1?view=azure-cli-latest)  
3. Processing YAML Content With yq | Baeldung on Linux, accessed August 12, 2025, [https://www.baeldung.com/linux/yq-utility-processing-yaml](https://www.baeldung.com/linux/yq-utility-processing-yaml)  
4. mikefarah/yq: yq is a portable command-line YAML, JSON, XML, CSV, TOML and properties processor \- GitHub, accessed August 12, 2025, [https://github.com/mikefarah/yq](https://github.com/mikefarah/yq)  
5. yq | yq, accessed August 12, 2025, [https://mikefarah.gitbook.io/yq](https://mikefarah.gitbook.io/yq)  
6. yq : A command line tool that will help you handle your YAML resources better, accessed August 12, 2025, [https://dev.to/vikcodes/yq-a-command-line-tool-that-will-help-you-handle-your-yaml-resources-better-8j9](https://dev.to/vikcodes/yq-a-command-line-tool-that-will-help-you-handle-your-yaml-resources-better-8j9)  
7. YAML Processing with YQ: A Practical Guide \- The Bottleneck Dev Blog, accessed August 12, 2025, [https://thebottleneckdev.com/blog/processing-yaml-files](https://thebottleneckdev.com/blog/processing-yaml-files)  
8. Run Transact-SQL Commands with the sqlcmd Utility \- SQL Server | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility?view=sql-server-ver17)  
9. microsoft/go-sqlcmd: The new sqlcmd, CLI for SQL Server and Azure SQL (winget install sqlcmd / sqlcmd create mssql / sqlcmd open ads) \- GitHub, accessed August 12, 2025, [https://github.com/microsoft/go-sqlcmd](https://github.com/microsoft/go-sqlcmd)  
10. Connect hybrid machines to Azure at scale \- Azure Arc \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/onboard-service-principal](https://learn.microsoft.com/en-us/azure/azure-arc/servers/onboard-service-principal)  
11. Running a SQL Script from the Azure Pipeline using a Service Principal with Client Secret, accessed August 12, 2025, [https://medium.com/@shekhartarare/running-a-sql-script-from-the-azure-pipeline-using-a-service-principal-with-client-secret-241c3756e271](https://medium.com/@shekhartarare/running-a-sql-script-from-the-azure-pipeline-using-a-service-principal-with-client-secret-241c3756e271)  
12. Running a SQL Script from the Azure Pipeline using a Service Principal with Certificate, accessed August 12, 2025, [https://medium.com/@shekhartarare/running-a-sql-script-from-the-azure-pipeline-using-a-service-principal-with-certificate-7ff6407b2967](https://medium.com/@shekhartarare/running-a-sql-script-from-the-azure-pipeline-using-a-service-principal-with-certificate-7ff6407b2967)  
13. Server-Level Roles \- SQL \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/server-level-roles?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/server-level-roles?view=sql-server-ver17)  
14. SQL Server security best practices \- Learn Microsoft, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/sql-server-security-best-practices?view=sql-server-ver16](https://learn.microsoft.com/en-us/sql/relational-databases/security/sql-server-security-best-practices?view=sql-server-ver16)  
15. SQL Server Find Least Privilege for user account \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/53673404/sql-server-find-least-privilege-for-user-account](https://stackoverflow.com/questions/53673404/sql-server-find-least-privilege-for-user-account)  
16. The Principle of Least Privilege in SQL Server Security \- IDERA, accessed August 12, 2025, [https://www.idera.com/blogs/understanding-the-principle-of-least-privilege-in-sql-server-security/](https://www.idera.com/blogs/understanding-the-principle-of-least-privilege-in-sql-server-security/)  
17. azcmagent connect CLI reference \- Azure Arc \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-connect](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-connect)  
18. Troubleshoot Azure Connected Machine agent connection issues \- Azure Arc, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-arc/servers/troubleshoot-agent-onboard](https://docs.azure.cn/en-us/azure-arc/servers/troubleshoot-agent-onboard)  
19. Assign an Azure Key Vault access policy (CLI) \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/key-vault/general/assign-access-policy](https://learn.microsoft.com/en-us/azure/key-vault/general/assign-access-policy)  
20. az keyvault secret | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/cli/azure/keyvault/secret?view=azure-cli-latest](https://learn.microsoft.com/en-us/cli/azure/keyvault/secret?view=azure-cli-latest)  
21. Entra ID SQL authentication \- Nerdio Manager for Enterprise, accessed August 12, 2025, [https://nmehelp.getnerdio.com/hc/en-us/articles/26124311294733-Entra-ID-SQL-authentication](https://nmehelp.getnerdio.com/hc/en-us/articles/26124311294733-Entra-ID-SQL-authentication)  
22. How do I connect to Azure SQL database using an App Service service principal?, accessed August 12, 2025, [https://stackoverflow.com/questions/78294531/how-do-i-connect-to-azure-sql-database-using-an-app-service-service-principal](https://stackoverflow.com/questions/78294531/how-do-i-connect-to-azure-sql-database-using-an-app-service-service-principal)  
23. Azure Authentication with Service Principal \- Alteryx Help Documentation, accessed August 12, 2025, [https://help.alteryx.com/current/en/designer/data-sources/azure-advanced-authentication-methods/azure-authentication-with-service-principal.html](https://help.alteryx.com/current/en/designer/data-sources/azure-advanced-authentication-methods/azure-authentication-with-service-principal.html)  
24. Authenticate with Microsoft Entra ID in sqlcmd \- SQL Server, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-authentication?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-authentication?view=sql-server-ver17)  
25. Microsoft Entra authentication \- Azure SQL Database & Azure SQL Managed Instance & Azure Synapse Analytics | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview?view=azuresql](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview?view=azuresql)  
26. A Standard Database Security Script Generator – SQLServerCentral, accessed August 12, 2025, [https://www.sqlservercentral.com/articles/a-standard-database-security-script-generator](https://www.sqlservercentral.com/articles/a-standard-database-security-script-generator)  
27. How to script SQL server database role? \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/6300740/how-to-script-sql-server-database-role](https://stackoverflow.com/questions/6300740/how-to-script-sql-server-database-role)  
28. List all permissions for a given role? \- sql server \- DBA Stack Exchange, accessed August 12, 2025, [https://dba.stackexchange.com/questions/36618/list-all-permissions-for-a-given-role](https://dba.stackexchange.com/questions/36618/list-all-permissions-for-a-given-role)  
29. RBAC Explained | Milvus Documentation, accessed August 12, 2025, [https://milvus.io/docs/rbac.md](https://milvus.io/docs/rbac.md)  
30. azcmagent check CLI reference \- Azure Arc | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-check](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-check)  
31. Better Azure Arc Agent Onboarding Script \- GitHub Gist, accessed August 12, 2025, [https://gist.github.com/JustinGrote/b7fac2b239420b4befd753d21952c3ec](https://gist.github.com/JustinGrote/b7fac2b239420b4befd753d21952c3ec)  
32. Troubleshoot Azure RBAC | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/role-based-access-control/troubleshooting](https://learn.microsoft.com/en-us/azure/role-based-access-control/troubleshooting)  
33. Role-based access control \- Operations Manual \- Neo4j, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/authentication-authorization/manage-privileges/](https://neo4j.com/docs/operations-manual/current/authentication-authorization/manage-privileges/)