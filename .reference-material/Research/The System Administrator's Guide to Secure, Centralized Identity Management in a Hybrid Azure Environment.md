

# **The System Administrator's Guide to Secure, Centralized Identity Management in a Hybrid Azure Environment**

## **Introduction**

This guide provides a comprehensive, phased methodology for system administrators to implement a secure, centralized identity management system using Microsoft Entra ID. The objective is to integrate disparate services—including on-premises and cloud-based Linux servers, SQL Server instances, and modern applications like Neo4j—into a unified authentication and authorization framework.

The instructions herein are built on three core principles:

1. **Security by Design**: Every step prioritizes security, from externalizing configuration and secrets to implementing the Principle of Least Privilege (PoLP).  
2. **Automation and Repeatability**: The process leverages Infrastructure as Code (IaC) with Terraform and idempotent scripting with Azure CLI and Bash to ensure the environment is reproducible, scalable, and resilient to human error.  
3. **Operational Clarity**: The guide is structured into distinct, logical phases. Each step clearly defines its purpose, commands, and expected outcomes, eliminating ambiguity and ensuring a cohesive workflow from foundation to final verification.

## **Phase 1: Foundational Environment and Security Configuration**

This initial phase establishes the secure and configurable foundation upon which all subsequent components will be built. By externalizing all configuration variables and using Infrastructure as Code (IaC) from the outset, the environment is designed to be secure, repeatable, and free of hardcoded values, directly addressing best practices for modern system administration.

### **1.1: Establishing the Centralized Configuration File (config.env)**

**Objective**: The first step is to create a single source of truth for all environment-specific variables. This file, config.env, will be sourced by all subsequent scripts, ensuring consistency and preventing the exposure of sensitive information within version-controlled code.

**Procedure**: Create a file named config.env in the root of the project directory. It is critical to add this filename to the project's .gitignore file to prevent accidental commits of secrets or environment-specific details. Populate this file with the variables that will define the entire environment, as detailed in the table below.

The creation of a centralized config.env file is a foundational security practice that decouples the configuration (the "what") from the automation logic (the "how"). This separation allows the same automation scripts to be used across different environments (development, staging, production) simply by providing an environment-specific config.env file, a critical capability for any modern DevOps workflow. This approach prevents "variable sprawl" and ensures that a change, such as a resource group name, only needs to be made in one place.

**Table: Environment Variables (config.env)**

| Variable Name | Purpose | Example Value |
| :---- | :---- | :---- |
| AZ\_SUBSCRIPTION\_ID | The unique identifier for the target Azure subscription. | 00000000-0000-0000-0000-000000000000 |
| AZ\_TENANT\_ID | The unique identifier for the Microsoft Entra ID tenant. | 11111111-1111-1111-1111-111111111111 |
| AZ\_LOCATION | The Azure region for deploying all resources. | eastus |
| AZ\_RESOURCE\_GROUP\_NAME | The name of the primary resource group for this project. | rg-central-auth-prod |
| AZ\_KEY\_VAULT\_NAME | A globally unique name for the Azure Key Vault. | kv-centralauth-prod-987xyz |
| AZ\_LINUX\_VM\_NAME | The name of the target Linux Virtual Machine. | linux-web-01 |
| AZ\_SQL\_SERVER\_NAME | The name of the Arc-enabled SQL Server host. | onprem-sql-svr-01 |
| ENTRA\_GROUP\_LINUX\_ADMINS | Display name for the Entra ID group for Linux admins. | sg-linux-admins |
| ENTRA\_GROUP\_SQL\_ADMINS | Display name for the Entra ID group for SQL admins. | sg-sql-admins |
| ENTRA\_GROUP\_NEO4J\_READERS | Display name for the Entra ID group for Neo4j read-only users. | sg-neo4j-readers |
| AUTOMATION\_SP\_NAME | The display name for the automation Service Principal. | sp-infra-automation |

### **1.2: Initializing the Environment**

**Objective**: To prepare the administrator's shell environment for executing subsequent commands by logging into Azure and setting the correct subscription context.

**Procedure**: Create and execute a Bash script named init-env.sh. This script will source the config.env file to load all variables into the current shell session, initiate an interactive browser login to Azure, and set the active subscription context for all subsequent Azure CLI commands.

Bash

\#\!/bin/bash  
\# init-env.sh

\# Source the centralized configuration file  
if \[ \-f config.env \]; then  
  export $(grep \-v '^\#' config.env | xargs)  
else  
  echo "Error: config.env not found."  
  exit 1  
fi

echo "Logging in to Azure..."  
az login \--tenant "$AZ\_TENANT\_ID"

echo "Setting active subscription..."  
az account set \--subscription "$AZ\_SUBSCRIPTION\_ID"

echo "Environment initialized successfully."  
echo "Subscription: $(az account show \--query name \-o tsv)"  
echo "Resource Group: $AZ\_RESOURCE\_GROUP\_NAME"

### **1.3: Provisioning Core Infrastructure with Terraform**

**Objective**: To use Terraform to declaratively and repeatably create the foundational Azure resources: the resource group and the Azure Key Vault.

**Procedure**: Create the following Terraform configuration files. These files define the desired state of the core infrastructure. Using Terraform from the outset establishes a robust and scalable Infrastructure as Code (IaC) practice, complete with state management, which is superior to executing a series of one-off imperative commands.

1. **variables.tf**: Defines the input variables.  
   Terraform  
   variable "resource\_group\_name" {  
     type        \= string  
     description \= "The name of the resource group."  
   }

   variable "location" {  
     type        \= string  
     description \= "The Azure region for all resources."  
   }

   variable "key\_vault\_name" {  
     type        \= string  
     description \= "The globally unique name for the Azure Key Vault."  
   }

   variable "tenant\_id" {  
     type \= string  
     description \= "The Azure Tenant ID."  
   }

2. **main.tf**: Defines the resources to be created. The Key Vault is the central repository for secrets that will be generated in later phases; provisioning it now ensures it is ready to receive those secrets, demonstrating a logical build-up of dependencies.  
   Terraform  
   terraform {  
     required\_providers {  
       azurerm \= {  
         source  \= "hashicorp/azurerm"  
         version \= "\~\>3.0"  
       }  
     }  
   }

   provider "azurerm" {  
     features {}  
   }

   resource "azurerm\_resource\_group" "main" {  
     name     \= var.resource\_group\_name  
     location \= var.location  
   }

   resource "azurerm\_key\_vault" "main" {  
     name                \= var.key\_vault\_name  
     location            \= azurerm\_resource\_group.main.location  
     resource\_group\_name \= azurerm\_resource\_group.main.name  
     tenant\_id           \= var.tenant\_id  
     sku\_name            \= "standard"

     soft\_delete\_retention\_days \= 7  
     purge\_protection\_enabled   \= false \# Set to true for production environments  
   }

3. **config.tfvars**: Create a file to pass the environment variables to Terraform.  
   Terraform  
   resource\_group\_name \= "rg-central-auth-prod"  
   location            \= "eastus"  
   key\_vault\_name      \= "kv-centralauth-prod-987xyz"  
   tenant\_id           \= "11111111-1111-1111-1111-111111111111"

   *Note: The values in this file should match those in your config.env.*

**Execution Commands**: Run the following commands from your terminal to provision the infrastructure:

Bash

\# Initialize the Terraform workspace  
terraform init

\# Create an execution plan  
terraform plan \-var-file="config.tfvars"

\# Apply the plan to create the resources  
terraform apply \-auto-approve \-var-file="config.tfvars"

## **Phase 2: Global Identity and Access Management in Microsoft Entra ID**

This phase centrally defines the security principals—groups and service principals—and their high-level permissions within Microsoft Entra ID. This creates the identity layer that will be consumed by all downstream services, enforcing a consistent Role-Based Access Control (RBAC) model across the entire hybrid environment.

### **2.1: Creating Entra ID Security Groups for Tiered Access**

**Objective**: To create dedicated security groups for each administrative and user tier. This approach abstracts permissions away from individual users, which greatly simplifies ongoing identity management. Adding or removing a user from a group automatically propagates their permissions across all integrated services.1

**Procedure**: The following Bash script creates the security groups defined in the config.env file. The script is designed to be **idempotent**—it can be run multiple times without causing errors or unintended changes. A simple az ad group create command will fail on subsequent runs if the group already exists.3 To build a robust and repeatable script, it is necessary to first check if the resource exists before attempting creation. This pattern is a critical skill for reliable automation.4

Bash

\#\!/bin/bash  
\# create-entra-groups.sh

\# Source the centralized configuration file  
if \[ \-f config.env \]; then  
  export $(grep \-v '^\#' config.env | xargs)  
else  
  echo "Error: config.env not found."  
  exit 1  
fi

\# Array of group names to create  
ENTRA\_GROUPS=(  
  "$ENTRA\_GROUP\_LINUX\_ADMINS"  
  "$ENTRA\_GROUP\_SQL\_ADMINS"  
  "$ENTRA\_GROUP\_NEO4J\_READERS"  
)

for GROUP\_NAME in "${ENTRA\_GROUPS\[@\]}"; do  
  echo "Checking for Entra ID group: $GROUP\_NAME"  
    
  \# Check if the group already exists  
  if\! az ad group show \--group "$GROUP\_NAME" &\>/dev/null; then  
    echo "Group '$GROUP\_NAME' not found. Creating..."  
      
    \# Create the group. The mail nickname must not contain hyphens.  
    az ad group create \--display-name "$GROUP\_NAME" \--mail-nickname "$(echo $GROUP\_NAME | tr \-d '-')"  
      
    echo "Group '$GROUP\_NAME' created successfully."  
  else  
    echo "Group '$GROUP\_NAME' already exists."  
  fi  
done

### **2.2: Defining and Assigning Azure RBAC Roles**

**Objective**: To grant the newly created Entra ID groups the necessary permissions at the Azure level to manage and access resources.

**Procedure**: Use the az role assignment create command to assign built-in Azure roles to the Entra ID groups. A critical aspect of this step is understanding the distinction between Azure's control plane and data plane.

* **Control Plane**: Permissions to manage the Azure resources themselves (e.g., create, configure, delete a VM). The "Contributor" role is a common control plane role.  
* **Data Plane**: Permissions to access the service or data provided by the resource (e.g., log in to the VM's operating system).

Assigning a "Contributor" role allows an administrator to *configure* a VM but does *not* grant them the ability to log in. For that, a specific data plane role is required.2 This separation is a fundamental security principle in Azure.

The script below assigns the appropriate data plane roles for VM login at the resource group scope.

Bash

\#\!/bin/bash  
\# assign-rbac-roles.sh

\# Source the centralized configuration file  
if \[ \-f config.env \]; then  
  export $(grep \-v '^\#' config.env | xargs)  
else  
  echo "Error: config.env not found."  
  exit 1  
fi

\# Get the Object ID for the Linux Admins group  
LINUX\_ADMIN\_GROUP\_ID=$(az ad group show \--group "$ENTRA\_GROUP\_LINUX\_ADMINS" \--query "id" \-o tsv)

if; then  
  echo "Error: Could not find Object ID for group '$ENTRA\_GROUP\_LINUX\_ADMINS'."  
  exit 1  
fi

echo "Assigning 'Virtual Machine Administrator Login' role to group '$ENTRA\_GROUP\_LINUX\_ADMINS'..."

\# Assign the role idempotently. The command succeeds without error if the assignment already exists.  
az role assignment create \\  
  \--assignee-object-id "$LINUX\_ADMIN\_GROUP\_ID" \\  
  \--assignee-principal-type "Group" \\  
  \--role "Virtual Machine Administrator Login" \\  
  \--scope "/subscriptions/$AZ\_SUBSCRIPTION\_ID/resourceGroups/$AZ\_RESOURCE\_GROUP\_NAME"

echo "Role assignment complete."

The Virtual Machine Administrator Login role has the unique ID 1c0163c0-47e6-4577-8991-ea5c82e286e4 and grants SSH access with sudo privileges.1 For non-administrative users, the

Virtual Machine User Login role (fb879df8-f326-4884-b1cf-06f3ad86be52) should be used.

### **2.3: Creating and Securing the Automation Service Principal**

**Objective**: To create a dedicated non-human identity, known as a Service Principal, for running automated tasks. This identity will operate under the Principle of Least Privilege, possessing only the permissions necessary to perform its functions.

**Procedure**: The default az ad sp create-for-rbac command assigns the highly privileged "Contributor" role, which violates the Principle of Least Privilege.7 A more secure approach is to first define a custom RBAC role with only the minimally required permissions and then assign this custom role during the Service Principal's creation. The secret generated during this process must be immediately stored in the Azure Key Vault provisioned in Phase 1\.

1. Create a JSON file for the custom role definition (automation-role.json):  
   This role grants only the permissions needed to manage VM extensions, which is the primary function of the automation principal in this guide.  
   JSON  
   {  
     "Name": "Custom \- Automation Extension Manager",  
     "Description": "Allows managing virtual machine extensions on Azure VMs and Arc-enabled servers.",  
     "Actions": \[  
       "Microsoft.Compute/virtualMachines/extensions/read",  
       "Microsoft.Compute/virtualMachines/extensions/write",  
       "Microsoft.Compute/virtualMachines/extensions/delete",  
       "Microsoft.HybridCompute/machines/extensions/read",  
       "Microsoft.HybridCompute/machines/extensions/write",  
       "Microsoft.HybridCompute/machines/extensions/delete"  
     \],  
     "NotActions":,  
     "DataActions":,  
     "NotDataActions":,  
     "AssignableScopes":  
   }

   *Replace YOUR\_SUBSCRIPTION\_ID with the value from config.env.*  
2. **Create the custom role and the Service Principal, then store its secret:**  
   Bash  
   \#\!/bin/bash  
   \# create-automation-sp.sh

   \# Source the centralized configuration file  
   if \[ \-f config.env \]; then  
     export $(grep \-v '^\#' config.env | xargs)  
   else  
     echo "Error: config.env not found."  
     exit 1  
   fi

   \# Update AssignableScopes in the JSON template  
   sed \-i "s|YOUR\_SUBSCRIPTION\_ID|$AZ\_SUBSCRIPTION\_ID|g" automation-role.json

   echo "Creating custom RBAC role for automation..."  
   ROLE\_DEFINITION=$(az role definition create \--role-definition @automation-role.json)  
   ROLE\_ID=$(echo $ROLE\_DEFINITION | jq \-r '.name')

   echo "Creating Service Principal '$AUTOMATION\_SP\_NAME'..."  
   SP\_CREDENTIALS=$(az ad sp create-for-rbac \\  
     \--name "$AUTOMATION\_SP\_NAME" \\  
     \--role "Custom \- Automation Extension Manager" \\  
     \--scopes "/subscriptions/$AZ\_SUBSCRIPTION\_ID/resourceGroups/$AZ\_RESOURCE\_GROUP\_NAME")

   if \[ $? \-ne 0 \]; then  
     echo "Error: Failed to create Service Principal."  
     exit 1  
   fi

   SP\_SECRET=$(echo $SP\_CREDENTIALS | jq \-r '.password')

   echo "Storing Service Principal secret in Key Vault '$AZ\_KEY\_VAULT\_NAME'..."  
   az keyvault secret set \\  
     \--vault-name "$AZ\_KEY\_VAULT\_NAME" \\  
     \--name "${AUTOMATION\_SP\_NAME}\-client-secret" \\  
     \--value "$SP\_SECRET"

   echo "Automation Service Principal setup complete. Secret stored securely."

## **Phase 3: Securing Linux Virtual Machine Access with Entra ID**

This phase transitions the Linux server from potentially insecure and difficult-to-manage static SSH keys to dynamic, centrally-managed authentication using Microsoft Entra ID. This change enhances security, simplifies user onboarding and offboarding, and improves the auditability of server access.

### **3.1: Hardening the sshd\_config File**

**Objective**: To apply industry-standard security configurations to the SSH daemon before enabling Entra ID integration. The AADSSHLoginForLinux extension is an authentication plugin; it does not, by itself, harden the underlying SSH service. A comprehensive security posture requires addressing the service's configuration directly, based on established benchmarks from organizations like the Center for Internet Security (CIS) or Mozilla.8

**Procedure**: The following script uses sed to idempotently modify /etc/ssh/sshd\_config, ensuring that critical security settings are enforced. The use of grep before modification prevents duplicate entries and allows the script to be re-run safely.

Bash

\#\!/bin/bash  
\# harden-sshd.sh

SSHD\_CONFIG="/etc/ssh/sshd\_config"  
SETTINGS=(  
  "PermitRootLogin no"  
  "PasswordAuthentication no"  
  "PubkeyAuthentication yes"  
  "LogLevel VERBOSE"  
  "KexAlgorithms curve25519-sha256@libssh.org,ecdh-sha2-nistp521,ecdh-sha2-nistp384,ecdh-sha2-nistp256,diffie-hellman-group-exchange-sha256"  
  "Ciphers chacha20-poly1305@openssh.com,aes256-gcm@openssh.com,aes128-gcm@openssh.com,aes256-ctr,aes192-ctr,aes128-ctr"  
  "MACs hmac-sha2-512-etm@openssh.com,hmac-sha2-256-etm@openssh.com,umac-128-etm@openssh.com,hmac-sha2-512,hmac-sha2-256"  
)

echo "Hardening SSH configuration at $SSHD\_CONFIG..."

for setting in "${SETTINGS\[@\]}"; do  
  key=$(echo "$setting" | awk '{print $1}')  
    
  \# Check if the setting key exists  
  if grep \-qE "^\\s\*\#?\\s\*$key\\s" "$SSHD\_CONFIG"; then  
    \# Key exists, so modify it (uncomment if necessary)  
    sudo sed \-i \-E "s/^\\s\*\#?\\s\*$key\\s.\*/$setting/" "$SSHD\_CONFIG"  
    echo "Set: $setting"  
  else  
    \# Key does not exist, append it  
    echo "$setting" | sudo tee \-a "$SSHD\_CONFIG" \> /dev/null  
    echo "Appended: $setting"  
  fi  
done

echo "Restarting SSH service to apply changes..."  
sudo systemctl restart sshd

echo "SSH hardening complete."

### **3.2: Deploying the AADSSHLoginForLinux VM Extension**

**Objective**: To install the necessary agent on the Linux VM to enable it to process Entra ID authentication requests.

**Procedure**: Use the Azure CLI command az vm extension set to install the AADSSHLoginForLinux extension. This command should be executed by an identity with the appropriate permissions, such as the automation service principal created in Phase 2\. It is important to use the current extension name, AADSSHLoginForLinux, as the older preview version, AADLoginForLinux, is deprecated.10

Bash

\#\!/bin/bash  
\# deploy-ssh-extension.sh

\# Source the centralized configuration file  
if \[ \-f config.env \]; then  
  export $(grep \-v '^\#' config.env | xargs)  
else  
  echo "Error: config.env not found."  
  exit 1  
fi

echo "Deploying AADSSHLoginForLinux extension to VM '$AZ\_LINUX\_VM\_NAME'..."

az vm extension set \\  
  \--publisher Microsoft.Azure.ActiveDirectory \\  
  \--name AADSSHLoginForLinux \\  
  \--resource-group "$AZ\_RESOURCE\_GROUP\_NAME" \\  
  \--vm-name "$AZ\_LINUX\_VM\_NAME"

echo "Extension deployment command issued."

### **3.3: Configuring Network and Firewall Rules**

**Objective**: To ensure the Linux VM can communicate with the necessary Microsoft Entra ID and Azure Arc endpoints for authentication and management. This often requires configuration at both the Azure network layer and the host-based firewall.

**Procedure**:

1. **Azure Network Security Group (NSG)**: An outbound security rule must be created in the VM's associated NSG to allow TCP traffic on ports 80 and 443 to the AzureActiveDirectory Service Tag. Service Tags are managed groups of IP addresses for Azure services, which simplifies firewall rule management.12  
2. **Local Firewall (UFW on Ubuntu)**: The local firewall on the server must also be configured to allow the required outbound traffic. The following commands configure Ubuntu's Uncomplicated Firewall (UFW) to deny all incoming traffic by default while allowing outbound traffic and explicitly permitting inbound SSH connections on the standard port.  
   Bash  
   \# Deny all incoming traffic by default  
   sudo ufw default deny incoming

   \# Allow all outgoing traffic by default  
   sudo ufw default allow outgoing

   \# Allow incoming SSH connections  
   sudo ufw allow ssh

   \# Enable the firewall  
   sudo ufw \--force enable

Table: Required Network Endpoints and Firewall Rules  
This consolidated table provides a checklist of network requirements that can be provided to a network security team for implementation on corporate firewalls or proxy servers.12

| Service | Direction | Protocol | Port | Destination | Purpose |
| :---- | :---- | :---- | :---- | :---- | :---- |
| Entra ID Auth | Outbound | TCP | 80, 443 | Service Tag: AzureActiveDirectory | For VM to validate user credentials against Entra ID. |
| Azure Arc Agent | Outbound | TCP | 443 | \*.his.arc.azure.com | Metadata and hybrid identity services. |
| Azure Arc Agent | Outbound | TCP | 443 | management.azure.com | Azure Resource Manager for resource management. |
| Azure Arc Agent | Outbound | TCP | 443 | \*.guestconfiguration.azure.com | Extension management and guest configuration services. |
| Azure Storage | Outbound | TCP | 443 | \*.blob.core.windows.net | Download source for VM extensions. |

## **Phase 4: Integrating SQL Server with Microsoft Entra ID**

This phase eliminates local SQL logins and traditional Windows Authentication in favor of a unified Entra ID authentication model for all SQL Server instances, whether they reside in Azure VMs or on-premises. This is achieved by leveraging Azure Arc to extend Azure's management and security plane to hybrid environments.

### **4.1: Onboarding Servers to Azure Arc**

**Objective**: To connect on-premises or multi-cloud servers to Azure, enabling management through the Azure control plane as if they were native Azure resources.

**Procedure**: The process involves installing the Azure Connected Machine agent (azcmagent) on the target server. A complete operational plan includes not only installation but also health checks and a clean uninstallation process for lifecycle management.

1. **Installation and Connection**: On the target server (e.g., onprem-sql-svr-01), run the azcmagent connect command. This command requires parameters specifying the target subscription, resource group, and location, along with credentials for an identity authorized to onboard servers. Using a service principal is the recommended approach for automation.15  
   PowerShell  
   \# On the target server, after downloading and installing the agent package

   \# Example using a Service Principal  
   azcmagent connect \-\-service-principal-id \<SP\_APP\_ID\> \-\-service-principal-secret \<SP\_SECRET\> \-\-tenant-id $env:AZ\_TENANT\_ID \-\-subscription-id $env:AZ\_SUBSCRIPTION\_ID \-\-resource-group $env:AZ\_RESOURCE\_GROUP\_NAME \-\-location $env:AZ\_LOCATION

2. **Health Verification**: After connection, verify the agent's status using azcmagent show and azcmagent check. The show command confirms the connection state and Azure resource details, while the check command performs a series of network connectivity tests to required endpoints.16  
3. **Clean Uninstallation**: To properly remove a server from Arc management, a specific sequence must be followed to avoid orphaned resources. First, remove any VM extensions from the Azure portal or via CLI. Second, run azcmagent disconnect on the server to remove the Azure resource and clear the local agent's state.18 If the Azure resource has already been deleted, the  
   \--force-local-only flag must be used.20 Finally, uninstall the agent package from the operating system.21

### **4.2: Enabling Entra ID Authentication for SQL Server**

**Objective**: To configure the Arc-enabled SQL Server instance to use Microsoft Entra ID for authentication, replacing traditional SQL and Windows logins.

**Procedure**: The process differs slightly for SQL Server running on a native Azure VM versus an Arc-enabled server, but the core prerequisite is the same: the server's identity must be granted permissions to query the Microsoft Graph API to validate user tokens.

1. **Grant Graph API Permissions**: The SQL Server instance, represented in Azure by a Managed Identity, needs permissions to read user and group information from Entra ID. Assign the User.Read.All, GroupMember.Read.All, and Application.Read.All application permissions to the server's Managed Identity service principal.12 This can be done using the Microsoft Graph PowerShell module.  
   PowerShell  
   \# Connect to Microsoft Graph with necessary scopes  
   Connect-MgGraph \-Scopes "AppRoleAssignment.ReadWrite.All" \-TenantId $env:AZ\_TENANT\_ID

   \# Get the Service Principals for Microsoft Graph and the SQL Server's Managed Identity  
   $Graph\_SP \= Get-MgServicePrincipal \-Filter "DisplayName eq 'Microsoft Graph'"  
   $MSI \= Get-MgServicePrincipal \-Filter "displayName eq '$($env:AZ\_SQL\_SERVER\_NAME)'"

   \# Assign the User.Read.All role  
   $AppRole \= $Graph\_SP.AppRoles | Where-Object {$\_.Value \-eq "User.Read.All"}  
   New-MgServicePrincipalAppRoleAssignment \-ServicePrincipalId $MSI.Id \-BodyParameter @{principalId=$MSI.Id; resourceId=$Graph\_SP.Id; appRoleId=$AppRole.Id}

   \# Repeat for GroupMember.Read.All and Application.Read.All

2. **Enable Entra Authentication**:  
   * **For SQL on Azure VM**: Use the az sql vm enable-azure-ad-auth command, specifying the VM name and resource group.12  
   * **For Arc-enabled SQL Server**: The process is managed through the Azure portal or a more complex script. It involves setting an Entra ID admin, creating an App Registration for the SQL instance, and managing a certificate in Azure Key Vault to facilitate the authentication flow.22 Navigate to the SQL Server \- Azure Arc resource in the portal, select "Microsoft Entra ID and Purview," and follow the "Set Admin" workflow.

### **4.3: Implementing Least Privilege with T-SQL**

**Objective**: To create a secure, role-based access model within the SQL database itself, mapping Entra ID groups to granular, custom database roles. This adheres to the Principle of Least Privilege by avoiding overly permissive built-in roles.

**Procedure**: After an Entra ID admin has been set for the SQL instance, that admin can connect to the database and execute T-SQL scripts to create the security structure. The following scripts are idempotent, using IF NOT EXISTS checks to ensure they can be re-run without causing errors.

Using built-in roles like db\_datareader grants read access to all user tables in the database, which is often more permission than an application requires.23 A more secure pattern is to create custom roles with permissions scoped to specific schemas.24

SQL

\-- Connect to the target user database as the Entra ID Admin

\-- Phase 1: Create Server-Level Logins for Entra ID Groups  
\-- This must be run in the context of the 'master' database.  
USE \[master\];  
GO

IF NOT EXISTS (SELECT 1 FROM sys.server\_principals WHERE name \= N'sg-sql-admins')  
BEGIN  
    CREATE LOGIN \[sg\-sql\-admins\] FROM EXTERNAL PROVIDER;  
END  
GO

\-- Phase 2: Create Database Users Mapped to the Logins  
\-- This must be run in the context of the target user database.  
USE;  
GO

IF NOT EXISTS (SELECT 1 FROM sys.database\_principals WHERE name \= N'sg-sql-admins')  
BEGIN  
    CREATE USER \[sg\-sql\-admins\] FROM LOGIN \[sg\-sql\-admins\];  
END  
GO

\-- Phase 3: Create Custom, Least-Privilege Database Roles  
IF NOT EXISTS (SELECT 1 FROM sys.database\_principals WHERE name \= N'App\_DataReader' AND type \= 'R')  
BEGIN  
    CREATE ROLE App\_DataReader;  
END  
GO

IF NOT EXISTS (SELECT 1 FROM sys.database\_principals WHERE name \= N'App\_Executor' AND type \= 'R')  
BEGIN  
    CREATE ROLE App\_Executor;  
END  
GO

\-- Phase 4: Grant Minimal, Schema-Scoped Permissions to Custom Roles  
GRANT SELECT ON SCHEMA::dbo TO App\_DataReader;  
GRANT EXECUTE ON SCHEMA::dbo TO App\_Executor;  
GO

\-- Phase 5: Add Entra ID Group Users to the Custom Roles  
ALTER ROLE App\_DataReader ADD MEMBER \[sg\-sql\-admins\];  
ALTER ROLE App\_Executor ADD MEMBER \[sg\-sql\-admins\];  
GO

This script establishes a clear separation of duties: App\_DataReader can only read data, while App\_Executor can only execute stored procedures. This is a significant security improvement over assigning broad permissions.25

## **Phase 5: Advanced Application Integration via OpenID Connect (OIDC)**

This phase demonstrates how the centralized Entra ID identity model can be extended beyond Microsoft services to third-party applications that support modern authentication protocols like OpenID Connect (OIDC). Integrating Neo4j showcases the scalability and versatility of the architecture.

### **5.1: Configuring Neo4j for Entra ID SSO**

**Objective**: To integrate a Neo4j graph database with Microsoft Entra ID for single sign-on, allowing users to authenticate using their corporate credentials.

**Procedure**: The integration involves configuration in both Microsoft Entra ID and the Neo4j server's configuration file. The connection between these two systems is established through claims within the JSON Web Token (JWT) issued by Entra ID upon successful authentication.27

1. **Configure Microsoft Entra ID**:  
   * In the Azure portal, navigate to **Microsoft Entra ID \> App registrations** and select **New registration**.28  
   * Provide a name (e.g., Neo4j SSO).  
   * Under "Redirect URI," select "Single-page application (SPA)" and enter the URI for Neo4j Browser: http://\<your-neo4j-server\>:7474/browser/?idp\_id=azure\&auth\_flow\_step=redirect\_uri.27  
   * Navigate to **API permissions** and add the openid, profile, and email delegated permissions for Microsoft Graph.  
   * Navigate to **Token configuration \> Add groups claim**. Select "Security groups" to ensure the user's group memberships are included in the token. This is the critical step that allows for role mapping in Neo4j.27  
2. **Configure Neo4j Server (neo4j.conf)**:  
   * Edit the neo4j.conf file to enable and configure the OIDC provider. The connection between Entra ID and Neo4j hinges on the JWT token claims. The administrator must ensure that the claims configured in Entra ID (like groups) exactly match what Neo4j is configured to expect (dbms.security.oidc.azure.claims.groups). A mismatch is a common point of failure.  
   * The well\_known\_discovery\_uri provides Neo4j with all the necessary endpoints and keys from Entra ID automatically.27

Properties  
\# Enable OIDC and native authentication providers  
dbms.security.authentication\_providers\=oidc-azure,native  
dbms.security.authorization\_providers\=oidc-azure,native

\# OIDC Provider Settings  
dbms.security.oidc.azure.display\_name\=Azure  
dbms.security.oidc.azure.auth\_flow\=pkce  
dbms.security.oidc.azure.config\=token\_type\_principal=id\_token;token\_type\_authentication=id\_token

\# Entra ID Specific Settings  
dbms.security.oidc.azure.well\_known\_discovery\_uri\=https://login.microsoftonline.com/YOUR\_TENANT\_ID/v2.0/.well-known/openid-configuration  
dbms.security.oidc.azure.audience\=YOUR\_APPLICATION\_CLIENT\_ID  
dbms.security.oidc.azure.params\=client\_id=YOUR\_APPLICATION\_CLIENT\_ID;response\_type=code;scope=openid profile email

\# Claims Mapping  
dbms.security.oidc.azure.claims.username\=sub  
dbms.security.oidc.azure.claims.groups\=groups

\# Role Mapping (Entra ID Group Object ID \-\> Neo4j Role)  
dbms.security.oidc.azure.authorization.group\_to\_role\_mapping\="e8b6ddfa-688d-4ace-987d-6cc5516af188" \= reader; \\  
                                                          "9e2a31e1-bdd1-47fe-844d-767502bd138d" \= admin  
*Replace YOUR\_TENANT\_ID, YOUR\_APPLICATION\_CLIENT\_ID, and the group Object IDs with your specific values.*

3. **Restart the Neo4j service** to apply the configuration changes.

## **Phase 6: Comprehensive Verification and Auditing**

This final phase validates that the entire integrated system is functioning as expected and equips the administrator with the knowledge to monitor its security and health over time. A system's implementation is not complete until it has been thoroughly verified.

### **6.1: End-to-End Connection Testing**

**Objective**: To perform a series of practical tests to confirm that users in the designated Entra ID groups can successfully authenticate to each service and are granted the correct level of access.

**Procedure**:

1. **Linux SSH**: From a client machine with the Azure CLI installed and logged in, attempt to SSH into the Linux VM.  
   Bash  
   \# User must be logged into Azure CLI on their client machine  
   az login

   \# SSH using the Entra ID username and the VM's public IP address  
   ssh \<user\>@\<VM\_IP\_Address\>

2. **SQL Server**: Use SQL Server Management Studio (SSMS) to connect.  
   * **Server name**: \<your-server-name\>.database.windows.net (for Azure SQL) or the server's hostname.  
   * **Authentication**: Select Azure Active Directory \- Universal with MFA.  
   * **User name**: Enter the Entra ID user principal name (e.g., user@domain.com).  
   * SSMS will trigger a browser-based authentication flow.29 Alternatively, for automated connections using a service principal,  
     sqlcmd can be used with the \--authentication-method ActiveDirectoryServicePrincipal flag.31  
3. **Neo4j**:  
   * Open a web browser and navigate to the Neo4j Browser URL (e.g., http://\<your-neo4j-server\>:7474).  
   * The interface should present a "Log in with Azure" button.  
   * Clicking this button should redirect to the standard Microsoft login page for authentication. Upon successful login, the user is redirected back to the Neo4j Browser.

### **6.2: Auditing System Logs**

**Objective**: To locate and interpret security logs to verify successful authentications and investigate failures. Auditing provides objective proof that the security model is working.

**Procedure**:

* **Linux**: On the Linux VM, the system authentication log (/var/log/auth.log on Debian/Ubuntu systems) will contain entries from the AADSSHLoginForLinux extension. Successful logins and failures can be identified by searching for the service name.  
* **SQL Server**: SQL Server Auditing can be configured to capture all login events. When reviewing the audit logs, successful logins from Entra ID will be recorded as using an EXTERNAL PROVIDER.  
* **Microsoft Entra ID**: The most valuable source for auditing is the centralized **Sign-in logs** within the Microsoft Entra admin center. Every authentication attempt for any integrated application—SSH, SQL Server, or Neo4j—will be recorded here. This provides a single pane of glass to see who is attempting to access which resources, from where, and whether they were successful. This centralized view is a primary benefit of the entire architecture.

### **6.3: Routine Health Checks**

**Objective**: To provide scripts for ongoing monitoring of the system's health and configuration integrity.

**Procedure**:

1. **Arc Agent Status**: Periodically run a script on Arc-enabled servers to verify agent health and connectivity. This script should utilize azcmagent show to confirm the connection status and azcmagent check to validate network paths to Azure endpoints.17  
2. **RBAC Assignment Audit**: Run a routine Azure CLI script to export all role assignments for the primary resource group. This allows for periodic review to ensure no unauthorized permissions have been granted and that the principle of least privilege is being maintained.33  
   Bash  
   \#\!/bin/bash  
   \# audit-rbac.sh

   \# Source the centralized configuration file  
   if \[ \-f config.env \]; then  
     export $(grep \-v '^\#' config.env | xargs)  
   else  
     echo "Error: config.env not found."  
     exit 1  
   fi

   TIMESTAMP=$(date \+"%Y%m%d-%H%M%S")  
   OUTPUT\_FILE="rbac-audit-$TIMESTAMP.json"

   echo "Exporting RBAC assignments for resource group '$AZ\_RESOURCE\_GROUP\_NAME' to $OUTPUT\_FILE..."

   az role assignment list \\  
     \--resource-group "$AZ\_RESOURCE\_GROUP\_NAME" \\  
     \--output json \> "$OUTPUT\_FILE"

   echo "Audit complete."

## **Conclusion**

By following the phased approach detailed in this guide, a system administrator can successfully construct a robust, secure, and scalable identity management solution. The core tenets of this architecture—centralized configuration, idempotent automation, and the principle of least privilege—address common failures in hybrid environment management by establishing a clear, repeatable, and auditable process. The integration of diverse platforms like Linux, SQL Server, and Neo4j under the unified security umbrella of Microsoft Entra ID demonstrates a modern approach to identity management that enhances security posture while simplifying administrative overhead. The provided scripts and configurations serve as a production-ready baseline for building and maintaining a secure hybrid cloud infrastructure.

#### **Works cited**

1. Azure Linux SSH Authentication: Entra Id \- BuiltWithCaffeine, accessed August 12, 2025, [https://blog.builtwithcaffeine.cloud/posts/linux-azure-configure-entra-id-access/](https://blog.builtwithcaffeine.cloud/posts/linux-azure-configure-entra-id-access/)  
2. Microsoft Entra authentication \- Azure SQL Database & Azure SQL Managed Instance & Azure Synapse Analytics | Azure Docs, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-sql/database/authentication-aad-overview](https://docs.azure.cn/en-us/azure-sql/database/authentication-aad-overview)  
3. az ad group create is not idempotent · Issue \#8624 · Azure/azure-cli \- GitHub, accessed August 12, 2025, [https://github.com/Azure/azure-cli/issues/8624](https://github.com/Azure/azure-cli/issues/8624)  
4. How to Check Resource Existence in Azure CLI \- Edi Wang, accessed August 12, 2025, [https://edi.wang/post/2020/2/23/how-to-check-resource-existence-in-azure-cli](https://edi.wang/post/2020/2/23/how-to-check-resource-existence-in-azure-cli)  
5. Provisioning an Azure Resource Group \- Katie Kodes, accessed August 12, 2025, [https://katiekodes.com/provision-azure-resource-group/](https://katiekodes.com/provision-azure-resource-group/)  
6. Virtual Machine Administrator Login \- 1c0163c0-47e6-4577-8991-ea5c82e286e4, accessed August 12, 2025, [https://www.azadvertizer.net/azrolesadvertizer/1c0163c0-47e6-4577-8991-ea5c82e286e4.html](https://www.azadvertizer.net/azrolesadvertizer/1c0163c0-47e6-4577-8991-ea5c82e286e4.html)  
7. Connect an existing Windows server to Azure Arc, accessed August 12, 2025, [https://jumpstart.azure.com/azure\_arc\_jumpstart/azure\_arc\_servers/general/onboard\_server\_win](https://jumpstart.azure.com/azure_arc_jumpstart/azure_arc_servers/general/onboard_server_win)  
8. CIS Ubuntu Benchmark | Google Distributed Cloud (software only) for VMware, accessed August 12, 2025, [https://cloud.google.com/kubernetes-engine/distributed-cloud/vmware/docs/concepts/cis-ubuntu-benchmark](https://cloud.google.com/kubernetes-engine/distributed-cloud/vmware/docs/concepts/cis-ubuntu-benchmark)  
9. OpenSSH \- Security Assurance and Security Operations \- Mozilla, accessed August 12, 2025, [https://infosec.mozilla.org/guidelines/openssh](https://infosec.mozilla.org/guidelines/openssh)  
10. Sign in to a Linux virtual machine in Azure by using Microsoft Entra ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/entra/identity/devices/howto-vm-sign-in-azure-ad-linux](https://learn.microsoft.com/en-us/entra/identity/devices/howto-vm-sign-in-azure-ad-linux)  
11. Azure AD Login for Azure Linux VMs \- Petri IT Knowledgebase, accessed August 12, 2025, [https://petri.com/azure-ad-login-azure-linux-vms/](https://petri.com/azure-ad-login-azure-linux-vms/)  
12. Enable Microsoft Entra authentication \- SQL Server on Azure VMs, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-sql/virtual-machines/windows/configure-azure-ad-authentication-for-sql-vm](https://docs.azure.cn/en-us/azure-sql/virtual-machines/windows/configure-azure-ad-authentication-for-sql-vm)  
13. Connected Machine agent network requirements \- Azure Arc, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-arc/servers/network-requirements](https://docs.azure.cn/en-us/azure-arc/servers/network-requirements)  
14. Connected Machine agent network requirements \- Azure Arc | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/network-requirements](https://learn.microsoft.com/en-us/azure/azure-arc/servers/network-requirements)  
15. azcmagent connect CLI reference \- Azure Arc \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-connect](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-connect)  
16. Azure Arc and Defender for Servers: Connectivity and Monitoring Script, accessed August 12, 2025, [https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/azure-arc-and-defender-for-servers-connectivity-and-monitoring-script/4428271](https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/azure-arc-and-defender-for-servers-connectivity-and-monitoring-script/4428271)  
17. azcmagent check CLI reference \- Azure Arc | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-check](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-check)  
18. Manage and maintain the Azure Connected Machine agent \- Azure ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-agent](https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-agent)  
19. azcmagent disconnect CLI reference \- Azure Arc \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-disconnect](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-disconnect)  
20. Troubleshoot Azure Connected Machine agent connection issues \- Azure Arc | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/troubleshoot-agent-onboard](https://learn.microsoft.com/en-us/azure/azure-arc/servers/troubleshoot-agent-onboard)  
21. Resolving Azure Linux Agent Stuck at "Creating" Status for Azure Arc-enabled servers, accessed August 12, 2025, [https://learn.microsoft.com/en-us/answers/questions/1851238/resolving-azure-linux-agent-stuck-at-creating-stat](https://learn.microsoft.com/en-us/answers/questions/1851238/resolving-azure-linux-agent-stuck-at-creating-stat)  
22. Tutorial: Set up Microsoft Entra authentication for SQL Server, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/azure-ad-authentication-sql-server-setup-tutorial?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/azure-ad-authentication-sql-server-setup-tutorial?view=sql-server-ver17)  
23. Database-level roles \- SQL Server \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/database-level-roles?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/database-level-roles?view=sql-server-ver17)  
24. A Standard Database Security Script Generator – SQLServerCentral, accessed August 12, 2025, [https://www.sqlservercentral.com/articles/a-standard-database-security-script-generator](https://www.sqlservercentral.com/articles/a-standard-database-security-script-generator)  
25. SQL Server Roles: A Practical Guide \- Satori Cyber, accessed August 12, 2025, [https://satoricyber.com/sql-server-security/sql-server-roles/](https://satoricyber.com/sql-server-security/sql-server-roles/)  
26. How to script SQL server database role? \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/6300740/how-to-script-sql-server-database-role](https://stackoverflow.com/questions/6300740/how-to-script-sql-server-database-role)  
27. Configuring Neo4j Single Sign-On (SSO) \- Operations Manual, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/tutorial/tutorial-sso-configuration/](https://neo4j.com/docs/operations-manual/current/tutorial/tutorial-sso-configuration/)  
28. Configure OIDC SSO for gallery and custom applications \- Microsoft Entra ID, accessed August 12, 2025, [https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/add-application-portal-setup-oidc-sso](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/add-application-portal-setup-oidc-sso)  
29. Connecting to Azure SQL with Microsoft Entra ID in dbForge SQL Complete \- Documentation, accessed August 12, 2025, [https://docs.devart.com/sqlcomplete/getting-started/connecting-microsoft-entra-id-authentication-for-azure-sql-databases.html](https://docs.devart.com/sqlcomplete/getting-started/connecting-microsoft-entra-id-authentication-for-azure-sql-databases.html)  
30. Connect to Azure SQL Database with Microsoft Entra multifactor authentication, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-sql/database/active-directory-interactive-connect-azure-sql-db](https://docs.azure.cn/en-us/azure-sql/database/active-directory-interactive-connect-azure-sql-db)  
31. Authenticate with Microsoft Entra ID in sqlcmd \- SQL Server, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-authentication?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-authentication?view=sql-server-ver17)  
32. Better Azure Arc Agent Onboarding Script \- GitHub Gist, accessed August 12, 2025, [https://gist.github.com/JustinGrote/b7fac2b239420b4befd753d21952c3ec](https://gist.github.com/JustinGrote/b7fac2b239420b4befd753d21952c3ec)  
33. Assign Azure roles using Azure CLI \- Azure RBAC | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-cli](https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-cli)