

# **An Architectural Guide to Implementing Microsoft Entra ID Authentication and Least-Privilege RBAC on an Azure Arc-Enabled Ubuntu Server**

## **Strategic Overview: Unifying Identity for Hybrid Infrastructure**

### **1.1. Executive Summary**

This document presents a definitive, production-grade implementation plan for re-architecting the security posture of a mission-critical, on-premises Ubuntu server. The core objective is to establish Microsoft Entra ID as the single, authoritative source of truth for identity across all layers of the system. This strategic shift centralizes user lifecycle management, enables the enforcement of consistent, modern security policies such as Multi-Factor Authentication (MFA) through Conditional Access, and provides a unified, auditable trail of access across the operating system and its hosted data services: SQL Server 2025, Neo4j, and Milvus.

The methodology is founded on a meticulous "clean and configure" approach. This process begins with a forensic audit to identify and surgically remove any residual configurations from prior integration attempts, ensuring a pristine foundation. Following this cleanup, a new, robust integration with Microsoft Entra ID will be established for all authentication vectors, from OS-level SSH access to the data-plane authentication for each hosted application. The final state will be governed by a comprehensive, multi-layered, and granular least-privilege Role-Based Access Control (RBAC) model, ensuring that every identity has only the minimum necessary permissions to perform its designated function.

### **1.2. Architectural Principles**

The design and implementation detailed in this report are guided by a set of foundational architectural principles, ensuring a secure, manageable, and resilient system.

* **Centralized Identity:** All human and programmatic access to the server and its services will be authenticated against Microsoft Entra ID. The use of local user accounts will be minimized, restricted to essential system functions, and subjected to rigorous auditing. This principle eliminates the security risks associated with managing disparate sets of credentials and streamlines the user onboarding and offboarding processes. When an employee's account is disabled in Entra ID, their access to all facets of this system is immediately and automatically revoked.1  
* **Least Privilege Access:** Security principals—whether users, groups, or service principals—will be granted only the explicit permissions required to perform their duties. This principle is not a single setting but a philosophy applied at every layer of the architecture: the Azure control plane for server management, the Ubuntu operating system for shell access, and within each application's data plane for data access. This approach dramatically reduces the potential impact of a compromised account.2  
* **Defense-in-Depth:** The security model is intentionally layered, creating multiple barriers for a potential adversary. Controls are implemented at the network level through the secure tunnel provided by Azure Arc, at the operating system level via hardened SSH configurations and Entra ID authentication, and finally at the application level with distinct, granular RBAC models for SQL Server, Neo4j, and Milvus.  
* **Comprehensive Auditability:** Every significant access event and administrative action must be logged and auditable through a central mechanism. This includes SSH logins tracked in both local system logs and Entra ID Sign-in Logs, Azure control plane actions recorded in the Azure Activity Log, and data access patterns audited within the respective database platforms. This creates a complete picture of system activity for security monitoring and compliance purposes.5  
* **Idempotency and Automation:** All configuration steps outlined in this plan are designed to be executed via scripts. These scripts will be idempotent, meaning they can be run multiple times with the same outcome, preventing configuration drift and ensuring a consistent, verifiable state. This reliance on automation minimizes the risk of manual error and provides a repeatable process for deployment and maintenance.7

## **Phase 1: System Baselining and Preparation**

Before any modifications are made, a rigorous baselining and preparation phase is essential. This phase serves two critical functions: it establishes a comprehensive safety net for rollback and recovery, and it provides the necessary intelligence to perform the subsequent cleanup with surgical precision. Neglecting this preparatory work introduces significant risk and undermines the goal of achieving a pristine final configuration.

### **2.1. Comprehensive Configuration Backup**

The first and most critical action is to create a complete, point-in-time snapshot of the server's configuration state. This backup is the primary mechanism for disaster recovery, allowing for a full restoration of the server to its pre-change state if any unforeseen issues arise.

#### **Procedure and Scripting**

A robust backup can be achieved using a shell script that leverages the tar utility. The script will be designed to archive critical system and application directories while preserving essential metadata such as file permissions and ownership, which are vital for a successful restoration.9

The following script, backup\_configs.sh, provides a template for this process. It archives specified directories into a compressed tarball, named with the server's hostname and a precise timestamp for clear versioning.

Bash

\#\!/bin/bash  
\#  
\# backup\_configs.sh \- A script to create a comprehensive backup of critical server configurations.  
\#

\# \--- Configuration \---  
\# List of directories and files to back up. Add all relevant application config paths.  
BACKUP\_FILES="/etc /home /var/log /var/lib/sqlserver /etc/neo4j"  
\# Destination directory for the backup archive. Ensure this path has sufficient space.  
DEST="/mnt/backups"

\# \--- Execution \---  
\# Create destination directory if it doesn't exist  
mkdir \-p $DEST

\# Create the archive filename.  
HOSTNAME=$(hostname \-s)  
TIMESTAMP=$(date \+"%Y%m%d-%H%M%S")  
ARCHIVE\_FILE="$HOSTNAME\-config-backup-$TIMESTAMP.tgz"

\# Print start status message.  
echo "Starting backup of critical configurations..."  
echo "Source files: $BACKUP\_FILES"  
echo "Destination archive: $DEST/$ARCHIVE\_FILE"  
date  
echo

\# Backup the files using tar.  
\# 'c' \- create archive  
\# 'z' \- compress with gzip  
\# 'p' \- preserve permissions  
\# 'f' \- use archive file  
sudo tar czpf $DEST/$ARCHIVE\_FILE $BACKUP\_FILES

\# Verify exit code  
if \[ $? \-eq 0 \]; then  
  echo  
  echo "Backup completed successfully."  
  date  
  echo  
  echo "Archive details:"  
  ls \-lh $DEST/$ARCHIVE\_FILE  
else  
  echo  
  echo "Backup FAILED. Please check for errors."  
  date  
fi

exit 0

#### **Execution and Automation**

To execute this script, it must first be made executable and then run with sudo to ensure it can access all necessary system files.9

Bash

chmod \+x backup\_configs.sh  
sudo./backup\_configs.sh

For ongoing protection, this script should be automated using cron. By placing the script in a standard location like /usr/local/bin/ and adding an entry to the root user's crontab, automated backups can be scheduled. For example, to run the backup daily at 2:00 AM, the following crontab entry can be used 9:

Bash

\# Edit the root crontab  
sudo crontab \-e

\# Add the following line to the file:  
\# m h  dom mon dow   command  
0 2 \* \* \* /usr/local/bin/backup\_configs.sh

#### **Off-Server Storage**

A backup stored on the same server it protects is vulnerable to server-level failures such as disk corruption or a security compromise. It is a non-negotiable best practice to move the backup archive to a secure, remote location. This can be accomplished using tools like rsync or scp to transfer the archive to another server, an NFS share, or, preferably, cloud storage such as an Azure Blob Storage account.9

The backup created in this step is more than a simple recovery tool. It serves as a forensic baseline. After the entire implementation process is complete, a differential comparison (diff) can be performed between the new, active configuration files and their original versions stored in this archive. This provides an exact, auditable record of every change made, offering definitive proof that the operation was surgical and that no unrelated custom server configurations were altered.

### **2.2. Auditing for Existing Entra ID Configurations**

The core requirement of a "surgical and clean" process necessitates a forensic audit to identify every trace of previous or failed Microsoft Entra ID integration attempts. Different integration methods leave behind different artifacts, and a partial or failed implementation of one can conflict with the new, correct implementation. This audit must be exhaustive to ensure a truly clean slate.

#### **Audit Checklist**

The following checklist provides a structured approach to identifying residual configurations across the system.

1. **Azure VM Extensions:** The primary mechanism for modern integration is the AADSSHLoginForLinux VM extension. Check for its presence, or that of its legacy predecessor AADLoginForLinux, using the Azure CLI. This command queries the Azure Resource Manager about the extensions deployed to the Arc-enabled server.1  
   Bash  
   az connectedmachine extension list \\  
     \--resource-group \<YourResourceGroup\> \\  
     \--machine-name \<YourArcServerName\> \\  
     \--output table

2. **Pluggable Authentication Modules (PAM) Configuration:** Older or alternative integration methods often modify the PAM stack. Inspect the PAM configuration for the SSH daemon and common authentication modules for any non-standard entries, particularly those referencing pam\_aad.so or pam\_sss.so.  
   Bash  
   grep \-E 'pam\_aad|pam\_sss' /etc/pam.d/sshd /etc/pam.d/common-auth

3. **SSH Daemon (sshd) Configuration:** The SSH server configuration file, /etc/ssh/sshd\_config, is a common point of modification. Scrutinize this file for specific directives that indicate a previous integration. The AADSSHLoginForLinux extension, for example, relies on certificate-based authentication and may add or modify AuthorizedKeysCommand or AuthenticationMethods directives. Also, look for AllowGroups or DenyGroups statements, as these can interfere with Entra ID group-based access control.1  
   Bash  
   grep \-iE 'AuthorizedKeysCommand|AuthenticationMethods|AllowGroups|DenyGroups' /etc/ssh/sshd\_config

4. **System Security Services Daemon (SSSD) and RealmD:** A common method for joining Linux machines to Active Directory domains (including Entra Domain Services) involves SSSD and the realmd service. Check if the server is joined to any domain and inspect the SSSD configuration file for any defined domains.13  
   Bash  
   \# Check if joined to any realm  
   realm list

   \# Check the status of the SSSD service  
   systemctl status sssd

   \# Inspect the SSSD configuration file if it exists  
   if \[ \-f /etc/sssd/sshd.conf \]; then sudo cat /etc/sssd/sshd.conf; fi

5. **Local User Accounts:** Examine the list of local user accounts in /etc/passwd. Previous integration attempts may have created local "shadow" accounts that mirror Entra ID User Principal Names (UPNs). These should be identified for removal.  
   Bash  
   cut \-d: \-f1 /etc/passwd

6. **SQL Server Principals:** The SQL Server instance itself may contain remnants of a prior Entra ID integration. Connect to the SQL Server instance and execute Transact-SQL queries against the system catalog views to identify any server-level logins or database-level users that are based on Entra ID principals. Principals from an external provider like Entra ID are identified by type 'E' (External User) or 'X' (External Group).14  
   SQL  
   \-- Check for server-level logins from Entra ID in the master database  
   USE master;  
   SELECT name, type\_desc FROM sys.server\_principals WHERE type IN ('E', 'X');

   \-- A script to check all databases for Entra ID users will be provided in the decommissioning phase.

The existence of configurations from multiple, distinct integration methods—such as the modern AADSSHLoginForLinux extension and the older SSSD/realm join method—is a common failure mode in complex hybrid environments. A failed attempt to implement one followed by an attempt at another can leave conflicting artifacts, such as competing PAM modules or sshd\_config directives. These conflicts lead to unpredictable authentication behavior and inconsistent security policy enforcement. Therefore, this audit is not merely a cleanup prerequisite but a critical diagnostic step to understand the server's state and plan the remediation accurately.

### **2.3. Azure Arc Agent Health Verification**

The Azure Connected Machine agent (azcmagent) is the foundational communication channel between the on-premises server and the Azure control plane. All subsequent configurations, including extension deployment and RBAC assignments, depend on the health and stability of this agent. Verifying its status is a mandatory prerequisite.

#### **Agent Status and Connectivity Validation**

A series of commands should be executed on the server to perform a comprehensive health check.

* **High-Level Status Check:** The azcmagent show command provides the most important top-level information, including the agent's connection status, the Azure resource ID it's mapped to, and the associated tenant ID. The status should report as "Connected".15  
  Bash  
  sudo azcmagent show

* **Detailed Network Connectivity Test:** The azcmagent check command performs a more granular series of network connectivity checks. It attempts to reach all required Azure service endpoints, verifying that network firewalls, proxies, or other intermediaries are not blocking critical communication paths. A successful output from this command provides high confidence that the agent can function reliably.18  
  Bash  
  sudo azcmagent check

* **Local Service Verification:** The agent relies on a local systemd service called the Hybrid Instance Metadata Service (HIMDS). This service is responsible for managing the local identity and metadata. Its status should be active and running.15  
  Bash  
  sudo systemctl status himds.service

If any of these checks fail, the underlying issues (e.g., network configuration, agent corruption) must be resolved before proceeding with the rest of this plan.

## **Phase 2: Surgical Decommissioning of Legacy Integrations**

Following the comprehensive audit in Phase 1, this phase focuses on the methodical and precise removal of all identified legacy or conflicting configurations. The goal is to revert the system to a known-good, non-integrated state, creating a clean foundation for the new implementation. The order of operations in this phase is critical to avoid creating orphaned resources or encountering dependency errors.

### **3.1. Scripted Removal of Conflicting Configurations**

To ensure consistency and minimize manual error, the removal process will be automated through a master shell script. This script will leverage standard Linux text-processing utilities like sed to perform surgical edits on configuration files, ensuring that only the targeted lines are removed.

#### **SSHD and PAM Configuration Cleanup**

For any extraneous lines identified in /etc/ssh/sshd\_config or the /etc/pam.d/ directory during the audit, the sed command with the in-place editing flag (-i) will be used. The .bak suffix creates an immediate backup of the original file before modification, providing a simple rollback mechanism for that specific file.19

The following script, cleanup\_legacy\_integrations.sh, demonstrates this process. It should be customized with the specific patterns identified in the audit.

Bash

\#\!/bin/bash  
\#  
\# cleanup\_legacy\_integrations.sh \- A script to surgically remove legacy Entra ID configurations.  
\#

echo "Starting surgical cleanup of legacy configurations..."

\# \--- SSHD Config Cleanup \---  
SSHD\_CONFIG="/etc/ssh/sshd\_config"  
if; then  
    echo "Cleaning $SSHD\_CONFIG..."  
    \# Add specific patterns identified during the audit.  
    \# Example: Remove a specific AuthorizedKeysCommand line.  
    sudo sed \-i.bak '/AuthorizedKeysCommand \\/usr\\/bin\\/aadsshcert/d' "$SSHD\_CONFIG"  
    \# Example: Remove a specific AllowGroups line.  
    sudo sed \-i.bak '/^AllowGroups.\*entra-group/d' "$SSHD\_CONFIG"  
    echo "sshd\_config cleanup complete. Original saved as $SSHD\_CONFIG.bak"  
fi

\# \--- PAM Config Cleanup \---  
SSHD\_PAM\_CONFIG="/etc/pam.d/sshd"  
if; then  
    echo "Cleaning $SSHD\_PAM\_CONFIG..."  
    \# Example: Remove lines containing pam\_aad.so  
    sudo sed \-i.bak '/pam\_aad.so/d' "$SSHD\_PAM\_CONFIG"  
    echo "sshd PAM config cleanup complete. Original saved as $SSHD\_PAM\_CONFIG.bak"  
fi

\# \--- SSSD and RealmD Uninstallation \---  
if systemctl is-active \--quiet sssd; then  
    echo "SSSD service is active. Attempting to leave realm and purge packages..."  
    \# Attempt to leave any joined realms (command will fail gracefully if not joined)  
    REALM\_NAME=$(realm list | awk 'NR==2 {print $1}')  
    if; then  
        echo "Leaving realm: $REALM\_NAME"  
        sudo realm leave "$REALM\_NAME"  
    fi  
    echo "Disabling and purging SSSD and related packages..."  
    sudo systemctl disable \--now sssd  
    sudo apt-get purge \-y sssd sssd-tools realmd adcli  
    echo "SSSD and RealmD packages have been purged."  
else  
    echo "SSSD service not found or inactive. Skipping SSSD cleanup."  
fi

\# \--- Legacy Extension Removal \---  
\# This part requires Azure CLI and credentials.  
\# Replace with your specific resource group and machine name.  
RESOURCE\_GROUP="\<YourResourceGroup\>"  
MACHINE\_NAME="\<YourArcServerName\>"  
LEGACY\_EXT\_NAME="AADLoginForLinux"

echo "Checking for legacy Azure VM extension '$LEGACY\_EXT\_NAME'..."  
EXT\_EXISTS=$(az connectedmachine extension list \-g "$RESOURCE\_GROUP" \--machine-name "$MACHINE\_NAME" \--query "" \-o tsv)

if; then  
    echo "Legacy extension found. Removing..."  
    az connectedmachine extension delete \\  
      \--name "$LEGACY\_EXT\_NAME" \\  
      \--machine-name "$MACHINE\_NAME" \\  
      \--resource-group "$RESOURCE\_GROUP" \\  
      \--yes  
    echo "Legacy extension removed."  
else  
    echo "No legacy extension found."  
fi

echo "Cleanup script finished."

### **3.2. Resetting the Azure Arc Agent (If Necessary)**

In cases where the Azure Arc agent itself is suspected of being in a corrupted or unrecoverable state, a full uninstall and reinstall is the most reliable path to resolution. This process must be executed in a precise sequence to avoid orphaning cloud resources or leaving residual configurations on the local machine.

#### **Critical Sequence of Operations**

1. **Remove All VM Extensions:** Before touching the Arc agent, all extensions managed by it must be uninstalled. The agent is the control mechanism for these extensions; if the agent is disconnected or removed first, Azure loses its ability to manage the extensions, effectively orphaning them on the server. This is a critical first step.22  
   Bash  
   \# List all extensions  
   az connectedmachine extension list \-g \<RG\> \--machine-name \<Machine\> \-o table  
   \# Delete each extension by name  
   az connectedmachine extension delete \-g \<RG\> \--machine-name \<Machine\> \--name \<ExtensionName\> \--yes

2. **Disconnect the Agent:** On the server, run the azcmagent disconnect command. This performs two actions: it deletes the corresponding Azure Arc-enabled server resource in Azure Resource Manager and it clears the local agent's configuration and state.22  
   Bash  
   sudo azcmagent disconnect

   In the event that the resource has already been deleted from the Azure portal, this command will fail. In such cases, the \--force-local-only flag must be used to instruct the agent to clean up its local state without attempting to contact Azure.23  
   Bash  
   sudo azcmagent disconnect \--force-local-only

3. **Uninstall the Agent Software:** Once disconnected, the agent software package can be removed from the system. The azcmagent uninstall command handles the complete removal of the agent's binaries and service files.16  
   Bash  
   sudo azcmagent uninstall

4. **Reinstall and Reconnect:** After a full removal, the server can be re-onboarded to Azure Arc by running the standard installation script generated from the Azure portal. This ensures a fresh installation with a new identity and clean configuration.

### **3.3. Cleansing SQL Server Entra ID Principals**

To prevent conflicts with the new, granular RBAC model, any existing Entra ID-based logins and users within the SQL Server 2025 instance must be removed. This process involves dependencies that must be handled correctly; a login cannot be dropped if it has associated database users, and a user cannot be dropped if it owns objects within a database.

The following T-SQL script automates this cleansing process. It dynamically generates and executes the necessary commands to iterate through all user databases, transfer ownership of any owned schemas or roles, drop the Entra ID database users, and finally, drop the server-level Entra ID logins.

SQL

\-- \=================================================================================  
\-- Script: Cleanse-EntraIDPrincipals.sql  
\-- Description: Drops all Microsoft Entra ID database users and server logins.  
\--              Transfers ownership of schemas and roles to 'dbo' before dropping.  
\-- WARNING: This script permanently removes access. Run with extreme caution.  
\-- \=================================================================================  
SET NOCOUNT ON;

DECLARE @db\_name NVARCHAR(128);  
DECLARE @user\_name NVARCHAR(128);  
DECLARE @sql\_command NVARCHAR(MAX);

\-- Step 1: Drop Entra ID users from all user databases  
PRINT '--- Starting cleanup of Entra ID users in all databases \---';

DECLARE db\_cursor CURSOR FOR  
SELECT name FROM sys.databases WHERE database\_id \> 4 AND state \= 0; \-- Online user databases

OPEN db\_cursor;  
FETCH NEXT FROM db\_cursor INTO @db\_name;

WHILE @@FETCH\_STATUS \= 0  
BEGIN  
    PRINT 'Processing database: ' \+ QUOTENAME(@db\_name);

    \-- Transfer schema ownership  
    SET @sql\_command \= 'USE ' \+ QUOTENAME(@db\_name) \+ ';  
    DECLARE @schema\_name NVARCHAR(128), @owner\_name NVARCHAR(128), @transfer\_sql NVARCHAR(MAX);  
    DECLARE schema\_cursor CURSOR FOR  
    SELECT s.name, p.name  
    FROM sys.schemas s  
    JOIN sys.database\_principals p ON s.principal\_id \= p.principal\_id  
    WHERE p.type IN (''E'', ''X'');

    OPEN schema\_cursor;  
    FETCH NEXT FROM schema\_cursor INTO @schema\_name, @owner\_name;  
    WHILE @@FETCH\_STATUS \= 0  
    BEGIN  
        PRINT ''  \- Transferring ownership of schema \['' \+ @schema\_name \+ ''\] from \['' \+ @owner\_name \+ ''\] to \[dbo\]'';  
        SET @transfer\_sql \= ''USE '' \+ QUOTENAME(@db\_name) \+ ''; ALTER AUTHORIZATION ON SCHEMA::'' \+ QUOTENAME(@schema\_name) \+ '' TO dbo;'';  
        EXEC sp\_executesql @transfer\_sql;  
        FETCH NEXT FROM schema\_cursor INTO @schema\_name, @owner\_name;  
    END;  
    CLOSE schema\_cursor;  
    DEALLOCATE schema\_cursor;';  
    EXEC sp\_executesql @sql\_command;

    \-- Drop the users  
    DECLARE user\_cursor CURSOR FOR  
    SELECT name FROM sys.database\_principals WHERE type IN ('E', 'X'); \-- E=External User, X=External Group

    OPEN user\_cursor;  
    FETCH NEXT FROM user\_cursor INTO @user\_name;

    WHILE @@FETCH\_STATUS \= 0  
    BEGIN  
        PRINT '  \- Dropping user: ' \+ QUOTENAME(@user\_name) \+ ' from database ' \+ QUOTENAME(@db\_name);  
        SET @sql\_command \= 'USE ' \+ QUOTENAME(@db\_name) \+ '; DROP USER ' \+ QUOTENAME(@user\_name) \+ ';';  
        BEGIN TRY  
            EXEC sp\_executesql @sql\_command;  
        END TRY  
        BEGIN CATCH  
            PRINT '    \- FAILED to drop user ' \+ QUOTENAME(@user\_name) \+ '. It may still own objects. Please check manually.';  
            PRINT '    \- Error: ' \+ ERROR\_MESSAGE();  
        END CATCH  
        FETCH NEXT FROM user\_cursor INTO @user\_name;  
    END;  
    CLOSE user\_cursor;  
    DEALLOCATE user\_cursor;

    FETCH NEXT FROM db\_cursor INTO @db\_name;  
END;  
CLOSE db\_cursor;  
DEALLOCATE db\_cursor;

\-- Step 2: Drop Entra ID logins from the server  
PRINT '--- Starting cleanup of Entra ID server logins \---';

DECLARE login\_cursor CURSOR FOR  
SELECT name FROM sys.server\_principals WHERE type IN ('E', 'X');

OPEN login\_cursor;  
FETCH NEXT FROM login\_cursor INTO @user\_name;

WHILE @@FETCH\_STATUS \= 0  
BEGIN  
    PRINT '  \- Dropping login: ' \+ QUOTENAME(@user\_name);  
    SET @sql\_command \= 'DROP LOGIN ' \+ QUOTENAME(@user\_name) \+ ';';  
    BEGIN TRY  
        EXEC sp\_executesql @sql\_command;  
    END TRY  
    BEGIN CATCH  
        PRINT '    \- FAILED to drop login ' \+ QUOTENAME(@user\_name) \+ '. It may own server-level objects. Please check manually.';  
        PRINT '    \- Error: ' \+ ERROR\_MESSAGE();  
    END CATCH  
    FETCH NEXT FROM login\_cursor INTO @user\_name;  
END;  
CLOSE login\_cursor;  
DEALLOCATE login\_cursor;

PRINT '--- Entra ID principal cleanup complete \---';

## **Phase 3: Implementing Entra ID Authentication for the Ubuntu Host (SSH)**

With a clean system baseline, the next phase is to implement the modern, secure method for OS-level authentication using Microsoft Entra ID. This is achieved by deploying the AADSSHLoginForLinux VM extension, which integrates the server's SSH daemon with the Entra ID authentication flow, enabling certificate-based, passwordless sign-in.

### **4.1. Installing and Configuring the AADSSHLoginForLinux Extension**

The AADSSHLoginForLinux extension is the Microsoft-supported mechanism for enabling Entra ID logins on Linux machines, including those managed by Azure Arc. Its deployment is managed through the Azure control plane.

#### **Prerequisites**

Before installing the extension, the Microsoft.HybridConnectivity resource provider must be registered for the Azure subscription. This is a one-time operation per subscription that enables the underlying connectivity service used for SSH over Arc.25

Bash

\# Check registration status  
az provider show \-n Microsoft.HybridConnectivity \-o tsv \--query registrationState

\# Register if not already registered  
az provider register \-n Microsoft.HybridConnectivity

#### **Extension Installation**

The extension is installed using the Azure CLI, targeting the specific Azure Arc-enabled server resource. This command instructs Azure to push the extension package down to the server via the Arc agent, which then handles the local installation and configuration.1

Bash

az connectedmachine extension create \\  
  \--machine-name \<YourArcServerName\> \\  
  \--name AADSSHLoginForLinux \\  
  \--publisher Microsoft.Azure.ActiveDirectory \\  
  \--type AADSSHLoginForLinux \\  
  \--resource-group \<YourResourceGroup\>

#### **Verification**

After the command completes, the installation can be verified both from Azure and on the local server.

* **Azure CLI Verification:** Check the provisioning state of the extension. It should report "Succeeded".  
  Bash  
  az connectedmachine extension show \\  
    \--machine-name \<YourArcServerName\> \\  
    \--name AADSSHLoginForLinux \\  
    \--resource-group \<YourResourceGroup\> \\  
    \--query "provisioningState"

* **Local Server Verification:** The extension's installation modifies the PAM configuration for SSH. Check for the addition of pam\_aad.so in /etc/pam.d/sshd. Additionally, the presence of the /usr/bin/aadsshcert binary confirms the installation of the necessary components.

### **4.2. Configuring Azure RBAC for SSH Access**

With the extension installed, access to the server is no longer controlled by local user accounts but by Azure RBAC role assignments. This centralizes access control and ties login permissions directly to a user's identity in Entra ID.

#### **Role Definitions**

Two primary built-in Azure roles are used to govern SSH access 25:

* **Virtual Machine Administrator Login:** Users assigned this role can log in with root or sudo privileges. The role's unique ID is 1c0163c0-47e6-4577-8991-ea5c82e286e4.  
* **Virtual Machine User Login:** Users assigned this role can log in as a standard, non-privileged user. The role's unique ID is fb879df8-f326-4884-b1cf-06f3ad86be52.

#### **Best Practice Implementation**

The most scalable and manageable approach is to assign these roles to Entra ID security groups rather than individual users.26

1. **Create Entra ID Security Groups:** In the Microsoft Entra admin center, create two dedicated security groups. A clear naming convention is recommended.  
   * SEC-Ubuntu-Server-Admins  
   * SEC-Ubuntu-Server-Users  
2. **Assign Roles to Groups:** Using the Azure CLI, assign the appropriate role to each group, scoping the assignment directly to the Azure Arc-enabled server resource. This ensures the permissions apply only to this specific server.  
   Bash  
   \# Get the Object ID for the Admin group  
   ADMIN\_GROUP\_ID=$(az ad group show \--group "SEC-Ubuntu-Server-Admins" \--query "id" \-o tsv)

   \# Get the Object ID for the User group  
   USER\_GROUP\_ID=$(az ad group show \--group "SEC-Ubuntu-Server-Users" \--query "id" \-o tsv)

   \# Get the Resource ID for the Arc server  
   ARC\_SERVER\_ID=$(az connectedmachine show \--name \<YourArcServerName\> \-g \<YourResourceGroup\> \--query "id" \-o tsv)

   \# Assign the Administrator role  
   az role assignment create \\  
     \--assignee-object-id "$ADMIN\_GROUP\_ID" \\  
     \--assignee-principal-type Group \\  
     \--role "Virtual Machine Administrator Login" \\  
     \--scope "$ARC\_SERVER\_ID"

   \# Assign the User role  
   az role assignment create \\  
     \--assignee-object-id "$USER\_GROUP\_ID" \\  
     \--assignee-principal-type Group \\  
     \--role "Virtual Machine User Login" \\  
     \--scope "$ARC\_SERVER\_ID"

From this point forward, managing SSH access is a matter of adding or removing users from these two Entra ID security groups. This simplifies administration and allows for access governance through standard Entra ID tools like Access Reviews.

### **4.3. Hardening SSH Configuration**

To ensure that Entra ID is the exclusive authentication method for human users, the SSH daemon configuration must be hardened to disable weaker or alternative authentication mechanisms.

#### **Configuration in /etc/ssh/sshd\_config**

The following changes should be made to /etc/ssh/sshd\_config and the SSH service restarted.

* PasswordAuthentication no: This is the most critical hardening step. It completely disables the ability to log in with a password, mitigating brute-force attacks and credential theft risks. All interactive logins will be forced through the Entra ID certificate flow.27  
* ChallengeResponseAuthentication no: This disables keyboard-interactive authentication methods, which are often used as a fallback for password authentication.  
* PubkeyAuthentication yes: This directive **must** be set to yes. The authentication mechanism used by the AADSSHLoginForLinux extension is based on short-lived OpenSSH certificates, which are a form of public key cryptography. Disabling this would break the Entra ID login functionality.

After making these changes, restart the SSH service to apply them:

Bash

sudo systemctl restart sshd

#### **Control Over authorized\_keys**

A potential bypass to this centralized control is for a user, once logged in, to add their own static public SSH key to their \~/.ssh/authorized\_keys file. This would allow them to log in directly with that key, even if their Entra ID access is later revoked. To mitigate this, file integrity monitoring tools like AIDE can be configured to watch for changes to authorized\_keys files, or stricter file permissions can be enforced via configuration management tools to prevent users from modifying this file.

### **4.4. Client-Side Connection and Validation**

The end-user experience for connecting to the server is streamlined through the Azure CLI.

#### **Client-Side Procedure**

1. **Install Prerequisites:** Users must have the Azure CLI installed on their client machine, along with the ssh extension.11  
   Bash  
   az extension add \--name ssh

2. **Authenticate to Azure:** Users must first authenticate their CLI session with their Entra ID credentials. This will typically open a web browser for interactive login.11  
   Bash  
   az login

3. **Connect to the Server:** The connection is initiated using the az ssh vm command. This command transparently handles the entire authentication flow: it requests a short-lived certificate from Entra ID based on the user's logged-in identity, and then uses that certificate to authenticate to the server.28  
   Bash  
   az ssh vm \--name \<YourArcServerName\> \--resource-group \<YourResourceGroup\>

   Alternatively, if the server has a public IP (less common for on-premises), the IP address can be used:  
   Bash  
   az ssh vm \--ip \<ServerIPAddress\>

A successful login prompt and subsequent shell access confirm that the entire authentication chain—from the user's az login to the Entra ID token issuance, the Azure RBAC check, and the on-server VM extension processing—is functioning correctly. This az ssh vm command is more than a simple convenience wrapper; it is a crucial component of the security model. It directly links the SSH session to the user's active Entra ID session. This allows Conditional Access policies, such as those requiring MFA or a compliant device, to be enforced at the moment the SSH connection is initiated. If a user's Entra ID token expires or is revoked, their ability to initiate a new connection is immediately blocked, providing a level of near-real-time session control that is impossible to achieve with static SSH keys. Therefore, standardizing on the az ssh vm command should be considered a security best practice.

## **Phase 4: Integrating Application-Layer Authentication with Entra ID**

With the host operating system secured, the focus shifts to integrating the data services running on the server. Each application—SQL Server 2025, Neo4j, and Milvus—has a distinct authentication architecture, requiring a tailored approach to align with the central identity strategy.

### **5.1. SQL Server 2025 via Azure Arc**

For SQL Server instances enabled by Azure Arc, Microsoft provides a native, tightly-coupled integration with Entra ID. The Azure Arc agent acts as a secure bridge, allowing the Azure control plane to configure and manage Entra ID authentication directly on the on-premises instance.

#### **Azure-Side Configuration**

The setup process involves creating several resources in Azure that will facilitate the trust relationship between the on-premises SQL Server and Entra ID.

1. **Create a Microsoft Entra App Registration:** This application registration will represent the identity of the SQL Server instance itself when it needs to communicate with Microsoft Graph to validate user tokens.14  
2. **Create and Store a Certificate in Azure Key Vault (AKV):** SQL Server will use a client certificate to authenticate as the app registration. For maximum security, this certificate should be generated and stored within an Azure Key Vault. This avoids exposing private keys on disk or in configuration files.14  
3. **Grant Arc Agent Permissions to AKV:** The managed identity of the Azure Arc-enabled server requires permissions to access the Key Vault. Specifically, it needs get and list permissions on both secrets and certificates to be able to retrieve the certificate on behalf of SQL Server.  
4. **Configure Entra ID on the SQL Server \- Azure Arc Resource:** In the Azure portal, navigate to the SQL Server \- Azure Arc resource that corresponds to the on-premises instance. Within the Microsoft Entra ID and Purview settings blade, the integration is activated. This involves:  
   * Setting a Microsoft Entra account as the initial sysadmin for the SQL instance.  
   * Pointing the configuration to the customer-managed app registration created in step 1\.  
   * Pointing the configuration to the customer-managed certificate stored in AKV from step 2\.14

     Saving this configuration triggers a process where the Arc agent securely downloads the certificate to the server and configures the SQL Server instance to use it for Entra ID authentication.

#### **SQL Server-Side Configuration (T-SQL)**

Once the configuration is successfully pushed down by the Arc agent, the designated Entra ID administrator can connect to the SQL instance. The next step is to create server-level logins for the Entra ID security groups that will be granted access.

SQL

\-- Connect to the 'master' database as the Entra ID admin  
USE master;  
GO

\-- Create logins for the Entra ID security groups  
\-- The name must match the group name in Entra ID  
CREATE LOGIN FROM EXTERNAL PROVIDER;  
GO  
CREATE LOGIN FROM EXTERNAL PROVIDER;  
GO

These commands create server principals that are linked to the corresponding Entra ID objects, enabling members of those groups to authenticate to the SQL Server instance.14

#### **Validation**

To validate the configuration, use a client tool like SQL Server Management Studio (SSMS) or Azure Data Studio. Attempt to connect to the on-premises SQL Server instance, but instead of using SQL Authentication, select one of the "Azure Active Directory" authentication methods, such as "Azure Active Directory \- Universal with MFA". Log in with the credentials of a user who is a member of one of the configured security groups. A successful connection validates the end-to-end authentication flow.30

### **5.2. Neo4j via OpenID Connect (OIDC)**

Neo4j supports modern authentication standards, including OpenID Connect (OIDC), which allows it to delegate authentication to an external identity provider like Microsoft Entra ID.33

#### **Azure-Side Configuration**

1. **Create Entra App Registration:** In the Entra admin center, register a new application for Neo4j.35  
2. **Configure Redirect URI:** The redirect URI is where Entra ID will send the authentication token after a user successfully logs in. For Neo4j Browser, this should be configured as a "Single-page application (SPA)" type URI pointing to the browser interface, for example: http://\<your-server-ip\>:7474/browser/.33  
3. **Configure API Permissions:** For OIDC to function correctly, the application needs permission to request basic identity information. Grant the openid, profile, and email delegated permissions under the Microsoft Graph API.35  
4. **Configure Token Claims for Group-Based Authorization:** The key to mapping Entra ID groups to Neo4j roles is to include the user's group memberships in the authentication token. In the "Token configuration" section of the app registration, select "Add groups claim" and configure it to include "Security groups" in the "ID" token. This will add a groups claim containing an array of the user's group Object IDs to the token that Neo4j receives.33  
5. **Create a Client Secret:** Neo4j will need to authenticate itself to Entra ID to exchange an authorization code for an ID token. Create a client secret, copy its value immediately, and store it securely for use in the Neo4j configuration file.35

#### **Neo4j-Side Configuration (neo4j.conf)**

The integration is configured by adding a new OIDC provider section to the neo4j.conf file.

Properties

\# neo4j.conf

\# Enable OIDC as an authentication and authorization provider.  
\# 'oidc-azure' is a custom name for this provider instance.  
dbms.security.authentication\_providers\=oidc-azure  
dbms.security.authorization\_providers\=oidc-azure

\# \--- OIDC Provider Configuration for Entra ID \---  
\# Display name shown on the login screen  
dbms.security.oidc.azure.display\_name\=Sign in with Microsoft

\# Use the PKCE flow, which is more secure for browser-based applications  
dbms.security.oidc.azure.auth\_flow\=pkce

\# Use the OpenID Connect metadata document for automatic discovery of endpoints  
dbms.security.oidc.azure.well\_known\_discovery\_uri\=https://login.microsoftonline.com/\<Your-Tenant-ID\>/v2.0/.well-known/openid-configuration

\# Configure the parameters sent in the authentication request  
\# Replace with your Application (client) ID  
dbms.security.oidc.azure.params\=client\_id=\<Your-Client-ID\>;response\_type=code;scope=openid profile email

\# Provide the client secret for the token exchange  
dbms.security.oidc.azure.client\_secret\=\<Your-Client-Secret\>

\# Tell Neo4j which claims in the ID token to use for username and groups  
dbms.security.oidc.azure.claims.username\=sub  \# 'sub' (subject) is a guaranteed unique identifier  
dbms.security.oidc.azure.claims.groups\=groups \# This must match the claim name configured in Entra ID

\# Map the Object IDs of Entra ID groups to internal Neo4j roles  
\# This is the core of the authorization model.  
dbms.security.oidc.azure.authorization.group\_to\_role\_mapping\="\<Entra-Group-ObjectID-1\>" \= admin; "\<Entra-Group-ObjectID-2\>" \= data\_analyst

After saving these changes, the Neo4j service must be restarted.

#### **Validation**

Navigate to the Neo4j Browser URL. The login screen should now present a "Sign in with Microsoft" button. Clicking this button should redirect to the standard Microsoft login page. After successful authentication, the user should be redirected back to the Neo4j Browser, logged in, and their assigned roles should reflect the mapping configured in neo4j.conf based on their Entra ID group memberships.33

### **5.3. Strategy for Milvus Authentication**

An analysis of Milvus's security features indicates that it primarily uses an internal username and password system for authentication and does not currently offer native integration with external identity providers via standards like OIDC or SAML.36 Therefore, a different architectural pattern is required to secure access while still leveraging Entra ID as the primary identity authority for the applications that connect to Milvus.

#### **Recommended "Loosely-Coupled" Architecture**

This approach uses Entra ID to manage the identity of the *client application* (not the end-user directly for the Milvus connection) and Azure Key Vault as a secure intermediary for the credentials.

1. **Enable Milvus Internal Authentication:** In the milvus.yaml configuration file, ensure that authentication is enabled:  
   YAML  
   common:  
     security:  
       authorizationEnabled: true

   This activates the internal user management system.37  
2. **Create Service Principals in Entra ID:** For each application or service that needs to connect to Milvus, create a dedicated Service Principal in Microsoft Entra ID. This gives the application a manageable, first-class identity in the central directory.  
3. **Secure Credential Storage in Azure Key Vault:** Create dedicated user accounts within Milvus for your applications (e.g., app\_backend\_writer). Generate a strong, complex password for this Milvus user. Store this username and password securely as two separate secrets within an Azure Key Vault.  
4. **Grant Service Principal Access to Key Vault:** Configure an access policy on the Key Vault to grant the application's Service Principal get permission for the specific secrets containing the Milvus credentials.  
5. Application-Managed Connection Flow: The application's runtime authentication flow is as follows:  
   a. The application starts and authenticates to Microsoft Entra ID using its Service Principal identity (e.g., via a client secret or managed identity).  
   b. Upon successful authentication, it receives an access token for Azure Key Vault.  
   c. The application uses this token to securely retrieve the Milvus username and password from the Key Vault secrets.  
   d. The application then uses these retrieved credentials to establish a connection to the Milvus server.36

This pattern maintains the principle of centralized identity by managing the application's identity in Entra ID, while securely handling the credentials required by the legacy authentication system of Milvus. It avoids hardcoding passwords in application code or configuration files. While the authentication mechanism is distinct, the RBAC model within Milvus should be designed to align conceptually with the roles defined for other services to maintain a consistent least-privilege strategy.

The integration methods for SQL Server and Milvus highlight two important patterns for securing on-premises applications with a cloud identity provider. The SQL Server on Arc integration represents a "tightly-coupled" identity bridge, where the Arc agent acts as a trusted intermediary, natively extending Entra ID's identity fabric onto the server. In contrast, the Milvus solution demonstrates a "loosely-coupled" bridge. Here, Entra ID authenticates the client application, and Azure Key Vault serves as the secure broker for the credentials needed by Milvus's internal authentication system. Understanding these two patterns provides a valuable framework for architects when planning future integrations of on-premises systems with cloud-native identity services.

## **Phase 5: A Multi-Layered, Least-Privilege RBAC Architecture**

A robust security posture requires more than just centralized authentication; it demands a granular, multi-layered Role-Based Access Control (RBAC) model. This phase details the design and implementation of such a model, applying the principle of least privilege at the Azure control plane, the SQL Server data plane, the Neo4j data plane, and the Milvus data plane.

### **6.1. Azure Control Plane: Custom Role for Arc Server Operators**

To allow infrastructure operators to manage the Azure Arc-enabled server and its extensions without granting them broad, subscription-level permissions (like Contributor), a custom Azure role is necessary. This role will be scoped to provide just the permissions needed for day-to-day server management tasks via Azure.

#### **JSON Role Definition**

The following JSON file, ArcServerOperatorRole.json, defines the custom role. It includes permissions to manage the machine resource, its extensions, and its connectivity endpoints.38

JSON

{  
  "Name": "Arc Server Operator",  
  "IsCustom": true,  
  "Description": "Allows management of Azure Arc-enabled servers and their extensions, including SSH access configuration.",  
  "Actions":,  
  "NotActions":,  
  "DataActions":,  
  "NotDataActions":,  
  "AssignableScopes":  
}

**Note:** Replace \<YourSubscriptionID\> and \<YourResourceGroup\> with the appropriate values. The AssignableScopes property restricts this role so it can only be assigned within the specified resource group, preventing privilege escalation in other parts of the subscription.

#### **Idempotent Deployment Script**

To deploy this role in an automated and repeatable fashion, the following Azure CLI script can be used. It first checks if the role already exists. If it does, the script updates it with the definition from the JSON file; otherwise, it creates it. This ensures the process is idempotent.7

Bash

\#\!/bin/bash  
\# deploy\_custom\_role.sh

ROLE\_NAME="Arc Server Operator"  
ROLE\_DEFINITION\_FILE="ArcServerOperatorRole.json"  
RESOURCE\_GROUP="\<YourResourceGroup\>"

\# Check if the role definition file exists  
if; then  
    echo "Error: Role definition file '$ROLE\_DEFINITION\_FILE' not found."  
    exit 1  
fi

\# Check if the custom role already exists  
az role definition list \--name "$ROLE\_NAME" \--custom-role-only true \--scope "/subscriptions/$(az account show \--query id \-o tsv)/resourceGroups/$RESOURCE\_GROUP" | grep \-q "$ROLE\_NAME"

if \[ $? \-eq 0 \]; then  
    echo "Role '$ROLE\_NAME' already exists. Updating definition..."  
    az role definition update \--role-definition "$ROLE\_DEFINITION\_FILE"  
else  
    echo "Role '$ROLE\_NAME' does not exist. Creating..."  
    az role definition create \--role-definition "$ROLE\_DEFINITION\_FILE"  
fi

if \[ $? \-eq 0 \]; then  
    echo "Custom role deployment completed successfully."  
else  
    echo "Custom role deployment FAILED."  
fi

### **6.2. SQL Server Data Plane: Application-Centric Database Roles**

Within SQL Server, access should be granted through user-defined database roles that are tailored to specific application functions, rather than using broad, built-in roles like db\_datareader or db\_datawriter.

#### **T-SQL Script for Role Creation and Mapping**

The following script, setup\_sql\_server\_rbac.sql, creates a set of granular database roles, grants them the minimum necessary permissions on a specific schema, creates database users from the Entra ID group logins, and adds those users as members of the appropriate roles.4

SQL

\-- setup\_sql\_server\_rbac.sql  
\-- Run this script in the context of your application database.  
USE;  
GO

\-- Create a dedicated schema for the application if it doesn't exist  
IF NOT EXISTS (SELECT \* FROM sys.schemas WHERE name \= 'app')  
BEGIN  
    EXEC('CREATE SCHEMA app');  
    PRINT 'Schema \[app\] created.';  
END  
GO

\-- Create application-specific database roles  
CREATE ROLE app\_read\_only;  
CREATE ROLE app\_read\_write;  
CREATE ROLE app\_executor;  
PRINT 'Application database roles created.';  
GO

\-- Grant least-privilege permissions to the roles on the application schema  
GRANT SELECT ON SCHEMA::app TO app\_read\_only;  
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::app TO app\_read\_write;  
GRANT EXECUTE ON SCHEMA::app TO app\_executor;  
PRINT 'Permissions granted to roles.';  
GO

\-- Create database users mapped to the Entra ID group logins  
CREATE USER FROM LOGIN;  
CREATE USER FROM LOGIN;  
PRINT 'Database users created from Entra ID logins.';  
GO

\-- Add the database users to the appropriate roles  
ALTER ROLE app\_read\_only ADD MEMBER;  
ALTER ROLE app\_read\_write ADD MEMBER;  
ALTER ROLE app\_executor ADD MEMBER;  
PRINT 'Users added to roles. SQL Server RBAC setup complete.';  
GO

### **6.3. Neo4j Data Plane: Graph-Based Access Control**

Neo4j provides a powerful, Cypher-based syntax for defining fine-grained access controls, including property-level security on nodes and relationships.

#### **Cypher Script for Role and Privilege Management**

The following script, setup\_neo4j\_rbac.cypher, demonstrates how to create roles and grant specific privileges for common data access patterns.42

Cypher

// setup\_neo4j\_rbac.cypher  
// Run this script as an admin user in Neo4j.

// Create roles for different user personas  
CREATE ROLE data\_analyst;  
CREATE ROLE data\_scientist;

// Grant privileges to the data\_analyst role  
// Allow traversal of the entire graph  
GRANT TRAVERSE ON GRAPH \* TO data\_analyst;  
// Allow reading of only specific properties on specific node labels  
GRANT READ {name, title} ON GRAPH \* NODES Person, Movie TO data\_analyst;  
// Deny read access to sensitive properties  
DENY READ {ssn, salary} ON GRAPH \* NODES Person TO data\_analyst;

// Grant privileges to the data\_scientist role  
// Allow running MATCH queries on all nodes and relationships  
GRANT MATCH {\*} ON GRAPH \* TO data\_scientist;  
// Allow read access to all properties  
GRANT READ {\*} ON GRAPH \* TO data\_scientist;

The mapping of these internal Neo4j roles to Entra ID security groups is managed within the neo4j.conf file, as detailed in Section 5.2.

### **6.4. Milvus Data Plane: Internal RBAC Model**

As Milvus uses an internal RBAC system, it must be configured to align with the overall least-privilege strategy. This involves creating roles and users within Milvus that correspond to the functions defined in the broader architecture.

#### **Python Script for Milvus RBAC Configuration**

The following Python script, setup\_milvus\_rbac.py, uses the pymilvus library to configure the internal RBAC model. It assumes authentication has already been enabled in milvus.yaml.44

Python

\# setup\_milvus\_rbac.py  
from pymilvus import MilvusClient

\# \--- Configuration \---  
MILVUS\_URI \= "http://localhost:19530"  
\# Use the root credentials for initial setup  
TOKEN \= "root:Milvus"

\# Define roles and their privileges  
ROLES\_TO\_CREATE \= {  
    "milvus\_reader":,  
    "milvus\_writer":  
}

\# Define users and their assigned roles  
USERS\_TO\_CREATE \= {  
    "app\_service\_user\_ro": {  
        "password": "\<Generate-A-Strong-Password-RO\>",  
        "roles": \["milvus\_reader"\]  
    },  
    "app\_service\_user\_rw": {  
        "password": "\<Generate-A-Strong-Password-RW\>",  
        "roles": \["milvus\_writer"\]  
    }  
}

\# \--- Execution \---  
client \= MilvusClient(uri=MILVUS\_URI, token=TOKEN)

print("--- Setting up Milvus RBAC \---")

\# Create roles and grant privileges  
for role\_name, privileges in ROLES\_TO\_CREATE.items():  
    try:  
        print(f"Creating role: {role\_name}")  
        client.create\_role(role\_name=role\_name)  
        for privilege in privileges:  
            \# Granting privilege on all collections (\*) in the default database  
            client.grant\_privilege(  
                role\_name=role\_name,  
                object\_type="Collection",  
                object\_name="\*",  
                privilege=privilege  
            )  
        print(f"Granted privileges to {role\_name}")  
    except Exception as e:  
        print(f"Error creating/configuring role {role\_name}: {e}")

\# Create users and grant roles  
for user\_name, details in USERS\_TO\_CREATE.items():  
    try:  
        print(f"Creating user: {user\_name}")  
        client.create\_user(user\_name=user\_name, password=details\["password"\])  
        for role in details\["roles"\]:  
            client.grant\_role(user\_name=user\_name, role\_name=role)  
        print(f"Granted roles to {user\_name}")  
    except Exception as e:  
        print(f"Error creating/configuring user {user\_name}: {e}")

print("--- Milvus RBAC setup complete \---")

The passwords generated for these internal Milvus users should be stored securely in Azure Key Vault and retrieved at runtime by the applications that use them.

### **Master RBAC Mapping Matrix**

To provide a holistic, end-to-end view of the entire RBAC model, the following matrix consolidates the relationship between Entra ID security groups and the permissions they confer across every layer of the technology stack. This table serves as a single pane of glass for auditing and understanding the complete access control strategy.

| Entra ID Security Group | Azure Role (Scope) | SSH Access | SQL Server Role | Neo4j Role | Milvus Role (via App) |
| :---- | :---- | :---- | :---- | :---- | :---- |
| SEC-Ubuntu-Server-Admins | Virtual Machine Administrator Login (Arc Resource) | sudo | sysadmin | admin | admin |
| SEC-SQL-Data-Scientists | Virtual Machine User Login (Arc Resource) | user | app\_read\_only | data\_scientist | milvus\_reader |
| SEC-Neo4j-Analysts | Virtual Machine User Login (Arc Resource) | user | db\_denydatareader | data\_analyst | N/A |
| SEC-App-Backend-Service-Principal | N/A | N/A | app\_read\_write, app\_executor | N/A | milvus\_writer |

This matrix is a powerful tool for visualizing the least-privilege strategy. It makes it simple to answer the critical question: "If a user is added to a specific Entra ID group, what exact permissions will they gain across the entire system?" This clarity is invaluable for both the initial implementation and for ongoing security governance and compliance audits.

## **Phase 6: End-to-End Validation and Operational Monitoring**

The final phase of the implementation involves a comprehensive validation of the entire security architecture, followed by the establishment of ongoing monitoring practices. This ensures that the system not only functions as designed but also remains secure and auditable throughout its operational lifecycle.

### **7.1. Comprehensive Validation Checklist**

A structured test plan is required to verify that every component of the authentication and authorization system is working correctly and that the principle of least privilege is being enforced. The following test cases should be executed by users with the specified Entra ID group memberships.

| Test Case ID | User Persona (Entra Group) | Action | Expected Outcome | Justification |
| :---- | :---- | :---- | :---- | :---- |
| **SSH-01** | SEC-Ubuntu-Server-Admins | Connect via az ssh vm. | Successful login to shell. | Validates admin SSH path. |
| **SSH-02** | SEC-Ubuntu-Server-Admins | Run sudo whoami. | Command succeeds, outputs root. | Validates sudo privilege. |
| **SSH-03** | SEC-SQL-Data-Scientists | Connect via az ssh vm. | Successful login to shell. | Validates user SSH path. |
| **SSH-04** | SEC-SQL-Data-Scientists | Run sudo whoami. | Command fails with permission error. | Validates lack of sudo. |
| **SSH-05** | User not in any group | Connect via az ssh vm. | Connection fails with permission error. | Validates default-deny posture. |
| **SQL-01** | SEC-SQL-Data-Scientists | Connect to SQL Server via SSMS with Entra ID auth. | Successful connection. | Validates Entra ID auth for SQL. |
| **SQL-02** | SEC-SQL-Data-Scientists | Run SELECT \* FROM app.MyTable; | Query succeeds. | Validates app\_read\_only role. |
| **SQL-03** | SEC-SQL-Data-Scientists | Run UPDATE app.MyTable SET...; | Query fails with permission error. | Validates least privilege. |
| **SQL-04** | SEC-App-Backend-Service-Principal | Connect and run INSERT and EXECUTE. | Commands succeed. | Validates app\_read\_write and app\_executor roles. |
| **NEO4J-01** | SEC-Neo4j-Analysts | Log in to Neo4j Browser via OIDC. | Successful login after Entra ID redirect. | Validates OIDC integration. |
| **NEO4J-02** | SEC-Neo4j-Analysts | Run MATCH (p:Person) RETURN p.name, p.title; | Query succeeds. | Validates data\_analyst role with property-level security. |
| **NEO4J-03** | SEC-Neo4j-Analysts | Run MATCH (p:Person) RETURN p.ssn; | Query fails or returns nulls due to permission error. | Validates DENY on sensitive properties. |
| **MILVUS-01** | Application using app\_service\_user\_ro | Connect to Milvus and perform a search. | Connection and search succeed. | Validates Milvus reader role. |
| **MILVUS-02** | Application using app\_service\_user\_ro | Attempt to insert data into a collection. | Operation fails with permission error. | Validates Milvus least privilege. |

### **7.2. Auditing and Monitoring**

Effective security requires continuous monitoring to detect anomalies, unauthorized access attempts, and policy violations. The following log sources are critical for maintaining the security posture of the server.

* **Microsoft Entra ID Sign-in Logs:** These logs provide a centralized audit trail for all authentication events. Filter for the application registrations created for Neo4j and for any applications using Service Principals to connect to SQL Server or Milvus. This allows for monitoring of application-level sign-ins and can be used to detect anomalous login patterns.  
* **Azure Monitor:** The Azure Activity Log will capture all management actions performed against the Azure Arc-enabled server resource, such as role assignments or extension modifications. Logs from the Arc agent itself can be collected into a Log Analytics workspace to monitor the health and connectivity of the agent over time.  
* **Ubuntu Server Logs:** The primary log file for authentication events on the server is /var/log/auth.log. Successful SSH logins initiated via the AADSSHLoginForLinux extension will be recorded here, providing an on-server record of access. The journalctl utility can be used to inspect the detailed logs from the sshd and himds services for troubleshooting.  
* **Database Audit Logs:** Both SQL Server and Neo4j have robust, built-in auditing capabilities. It is strongly recommended to configure auditing within each database to track significant events, such as failed login attempts, permission changes, and access to sensitive data tables or nodes. These logs provide the deepest visibility into data-plane activity.  
* **Linux Audit Daemon (auditd):** For advanced host-level intrusion detection, the auditd service should be configured. It can be set up to monitor critical system files for unauthorized changes. Creating a watch rule for /etc/ssh/sshd\_config, /etc/pam.d/sshd, and users' .ssh/authorized\_keys files will generate an immediate audit event if these files are modified, alerting administrators to potential security tampering or attempts to bypass the centralized authentication controls.5

## **Conclusion**

The successful execution of this architectural plan will transform the security posture of the on-premises Ubuntu server from a state of ambiguity to one of clarity, control, and compliance. By systematically decommissioning legacy configurations and implementing a modern, centralized identity solution based on Microsoft Entra ID, the server's attack surface is significantly reduced. The integration of the host OS and its diverse application stack—SQL Server 2025, Neo4j, and Milvus—under a single identity provider streamlines administration, enhances security through policies like Conditional Access, and provides a unified audit trail.

The cornerstone of this architecture is the multi-layered, least-privilege RBAC model. By defining granular roles at the Azure control plane, the OS level, and within each application's data plane, the system ensures that every identity is granted only the precise permissions required for its function. This deliberate and meticulous approach to access control is fundamental to a zero-trust security strategy and is the most effective defense against the lateral movement of an attacker in the event of a credential compromise.

The final configured state is not only secure but also manageable and scalable. Access control is no longer a function of managing disparate local accounts or configuration files but is instead a function of managing membership in Microsoft Entra ID security groups. This simplifies governance, enables automated access reviews, and aligns the management of this critical on-premises resource with modern cloud security best practices. Adherence to the validation and monitoring protocols outlined in the final phase will ensure this pristine and secure state is maintained throughout the server's operational lifecycle.

## **Appendix: Consolidated Scripts and Configuration Manifests**

This appendix contains the full, consolidated versions of all scripts and configuration files referenced throughout this report for ease of implementation.

### **A.1. Configuration Backup Script (backup\_configs.sh)**

Bash

\#\!/bin/bash  
\#  
\# backup\_configs.sh \- A script to create a comprehensive backup of critical server configurations.  
\#

\# \--- Configuration \---  
\# List of directories and files to back up. Add all relevant application config paths.  
BACKUP\_FILES="/etc /home /var/log /var/lib/sqlserver /etc/neo4j"  
\# Destination directory for the backup archive. Ensure this path has sufficient space.  
DEST="/mnt/backups"

\# \--- Execution \---  
\# Create destination directory if it doesn't exist  
mkdir \-p $DEST

\# Create the archive filename.  
HOSTNAME=$(hostname \-s)  
TIMESTAMP=$(date \+"%Y%m%d-%H%M%S")  
ARCHIVE\_FILE="$HOSTNAME\-config-backup-$TIMESTAMP.tgz"

\# Print start status message.  
echo "Starting backup of critical configurations..."  
echo "Source files: $BACKUP\_FILES"  
echo "Destination archive: $DEST/$ARCHIVE\_FILE"  
date  
echo

\# Backup the files using tar.  
\# 'c' \- create archive  
\# 'z' \- compress with gzip  
\# 'p' \- preserve permissions  
\# 'f' \- use archive file  
sudo tar czpf $DEST/$ARCHIVE\_FILE $BACKUP\_FILES

\# Verify exit code  
if \[ $? \-eq 0 \]; then  
  echo  
  echo "Backup completed successfully."  
  date  
  echo  
  echo "Archive details:"  
  ls \-lh $DEST/$ARCHIVE\_FILE  
else  
  echo  
  echo "Backup FAILED. Please check for errors."  
  date  
fi

exit 0

### **A.2. Legacy Integration Cleanup Script (cleanup\_legacy\_integrations.sh)**

Bash

\#\!/bin/bash  
\#  
\# cleanup\_legacy\_integrations.sh \- A script to surgically remove legacy Entra ID configurations.  
\#

echo "Starting surgical cleanup of legacy configurations..."

\# \--- SSHD Config Cleanup \---  
SSHD\_CONFIG="/etc/ssh/sshd\_config"  
if; then  
    echo "Cleaning $SSHD\_CONFIG..."  
    \# Add specific patterns identified during the audit.  
    \# Example: Remove a specific AuthorizedKeysCommand line.  
    sudo sed \-i.bak '/AuthorizedKeysCommand \\/usr\\/bin\\/aadsshcert/d' "$SSHD\_CONFIG"  
    \# Example: Remove a specific AllowGroups line.  
    sudo sed \-i.bak '/^AllowGroups.\*entra-group/d' "$SSHD\_CONFIG"  
    echo "sshd\_config cleanup complete. Original saved as $SSHD\_CONFIG.bak"  
fi

\# \--- PAM Config Cleanup \---  
SSHD\_PAM\_CONFIG="/etc/pam.d/sshd"  
if; then  
    echo "Cleaning $SSHD\_PAM\_CONFIG..."  
    \# Example: Remove lines containing pam\_aad.so  
    sudo sed \-i.bak '/pam\_aad.so/d' "$SSHD\_PAM\_CONFIG"  
    echo "sshd PAM config cleanup complete. Original saved as $SSHD\_PAM\_CONFIG.bak"  
fi

\# \--- SSSD and RealmD Uninstallation \---  
if systemctl is-active \--quiet sssd; then  
    echo "SSSD service is active. Attempting to leave realm and purge packages..."  
    \# Attempt to leave any joined realms (command will fail gracefully if not joined)  
    REALM\_NAME=$(realm list | awk 'NR==2 {print $1}')  
    if; then  
        echo "Leaving realm: $REALM\_NAME"  
        sudo realm leave "$REALM\_NAME"  
    fi  
    echo "Disabling and purging SSSD and related packages..."  
    sudo systemctl disable \--now sssd  
    sudo apt-get purge \-y sssd sssd-tools realmd adcli  
    echo "SSSD and RealmD packages have been purged."  
else  
    echo "SSSD service not found or inactive. Skipping SSSD cleanup."  
fi

\# \--- Legacy Extension Removal \---  
\# This part requires Azure CLI and credentials.  
\# Replace with your specific resource group and machine name.  
RESOURCE\_GROUP="\<YourResourceGroup\>"  
MACHINE\_NAME="\<YourArcServerName\>"  
LEGACY\_EXT\_NAME="AADLoginForLinux"

echo "Checking for legacy Azure VM extension '$LEGACY\_EXT\_NAME'..."  
EXT\_EXISTS=$(az connectedmachine extension list \-g "$RESOURCE\_GROUP" \--machine-name "$MACHINE\_NAME" \--query "" \-o tsv)

if; then  
    echo "Legacy extension found. Removing..."  
    az connectedmachine extension delete \\  
      \--name "$LEGACY\_EXT\_NAME" \\  
      \--machine-name "$MACHINE\_NAME" \\  
      \--resource-group "$RESOURCE\_GROUP" \\  
      \--yes  
    echo "Legacy extension removed."  
else  
    echo "No legacy extension found."  
fi

echo "Cleanup script finished."

### **A.3. Custom Azure Role Definition (ArcServerOperatorRole.json)**

JSON

{  
  "Name": "Arc Server Operator",  
  "IsCustom": true,  
  "Description": "Allows management of Azure Arc-enabled servers and their extensions, including SSH access configuration.",  
  "Actions":,  
  "NotActions":,  
  "DataActions":,  
  "NotDataActions":,  
  "AssignableScopes":  
}

### **A.4. Custom Azure Role Deployment Script (deploy\_custom\_role.sh)**

Bash

\#\!/bin/bash  
\# deploy\_custom\_role.sh

ROLE\_NAME="Arc Server Operator"  
ROLE\_DEFINITION\_FILE="ArcServerOperatorRole.json"  
RESOURCE\_GROUP="\<YourResourceGroup\>"

\# Check if the role definition file exists  
if; then  
    echo "Error: Role definition file '$ROLE\_DEFINITION\_FILE' not found."  
    exit 1  
fi

\# Check if the custom role already exists  
az role definition list \--name "$ROLE\_NAME" \--custom-role-only true \--scope "/subscriptions/$(az account show \--query id \-o tsv)/resourceGroups/$RESOURCE\_GROUP" | grep \-q "$ROLE\_NAME"

if \[ $? \-eq 0 \]; then  
    echo "Role '$ROLE\_NAME' already exists. Updating definition..."  
    az role definition update \--role-definition "$ROLE\_DEFINITION\_FILE"  
else  
    echo "Role '$ROLE\_NAME' does not exist. Creating..."  
    az role definition create \--role-definition "$ROLE\_DEFINITION\_FILE"  
fi

if \[ $? \-eq 0 \]; then  
    echo "Custom role deployment completed successfully."  
else  
    echo "Custom role deployment FAILED."  
fi

### **A.5. SQL Server RBAC Setup Script (setup\_sql\_server\_rbac.sql)**

SQL

\-- setup\_sql\_server\_rbac.sql  
\-- Run this script in the context of your application database.  
USE;  
GO

\-- Create a dedicated schema for the application if it doesn't exist  
IF NOT EXISTS (SELECT \* FROM sys.schemas WHERE name \= 'app')  
BEGIN  
    EXEC('CREATE SCHEMA app');  
    PRINT 'Schema \[app\] created.';  
END  
GO

\-- Create application-specific database roles  
CREATE ROLE app\_read\_only;  
CREATE ROLE app\_read\_write;  
CREATE ROLE app\_executor;  
PRINT 'Application database roles created.';  
GO

\-- Grant least-privilege permissions to the roles on the application schema  
GRANT SELECT ON SCHEMA::app TO app\_read\_only;  
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::app TO app\_read\_write;  
GRANT EXECUTE ON SCHEMA::app TO app\_executor;  
PRINT 'Permissions granted to roles.';  
GO

\-- Create database users mapped to the Entra ID group logins  
CREATE USER FROM LOGIN;  
CREATE USER FROM LOGIN;  
PRINT 'Database users created from Entra ID logins.';  
GO

\-- Add the database users to the appropriate roles  
ALTER ROLE app\_read\_only ADD MEMBER;  
ALTER ROLE app\_read\_write ADD MEMBER;  
ALTER ROLE app\_executor ADD MEMBER;  
PRINT 'Users added to roles. SQL Server RBAC setup complete.';  
GO

### **A.6. Neo4j RBAC Setup Script (setup\_neo4j\_rbac.cypher)**

Cypher

// setup\_neo4j\_rbac.cypher  
// Run this script as an admin user in Neo4j.

// Create roles for different user personas  
CREATE ROLE data\_analyst;  
CREATE ROLE data\_scientist;

// Grant privileges to the data\_analyst role  
// Allow traversal of the entire graph  
GRANT TRAVERSE ON GRAPH \* TO data\_analyst;  
// Allow reading of only specific properties on specific node labels  
GRANT READ {name, title} ON GRAPH \* NODES Person, Movie TO data\_analyst;  
// Deny read access to sensitive properties  
DENY READ {ssn, salary} ON GRAPH \* NODES Person TO data\_analyst;

// Grant privileges to the data\_scientist role  
// Allow running MATCH queries on all nodes and relationships  
GRANT MATCH {\*} ON GRAPH \* TO data\_scientist;  
// Allow read access to all properties  
GRANT READ {\*} ON GRAPH \* TO data\_scientist;

### **A.7. Milvus RBAC Setup Script (setup\_milvus\_rbac.py)**

Python

\# setup\_milvus\_rbac.py  
from pymilvus import MilvusClient

\# \--- Configuration \---  
MILVUS\_URI \= "http://localhost:19530"  
\# Use the root credentials for initial setup  
TOKEN \= "root:Milvus"

\# Define roles and their privileges  
ROLES\_TO\_CREATE \= {  
    "milvus\_reader":,  
    "milvus\_writer":  
}

\# Define users and their assigned roles  
USERS\_TO\_CREATE \= {  
    "app\_service\_user\_ro": {  
        "password": "\<Generate-A-Strong-Password-RO\>",  
        "roles": \["milvus\_reader"\]  
    },  
    "app\_service\_user\_rw": {  
        "password": "\<Generate-A-Strong-Password-RW\>",  
        "roles": \["milvus\_writer"\]  
    }  
}

\# \--- Execution \---  
client \= MilvusClient(uri=MILVUS\_URI, token=TOKEN)

print("--- Setting up Milvus RBAC \---")

\# Create roles and grant privileges  
for role\_name, privileges in ROLES\_TO\_CREATE.items():  
    try:  
        print(f"Creating role: {role\_name}")  
        client.create\_role(role\_name=role\_name)  
        for privilege in privileges:  
            \# Granting privilege on all collections (\*) in the default database  
            client.grant\_privilege(  
                role\_name=role\_name,  
                object\_type="Collection",  
                object\_name="\*",  
                privilege=privilege  
            )  
        print(f"Granted privileges to {role\_name}")  
    except Exception as e:  
        print(f"Error creating/configuring role {role\_name}: {e}")

\# Create users and grant roles  
for user\_name, details in USERS\_TO\_CREATE.items():  
    try:  
        print(f"Creating user: {user\_name}")  
        client.create\_user(user\_name=user\_name, password=details\["password"\])  
        for role in details\["roles"\]:  
            client.grant\_role(user\_name=user\_name, role\_name=role)  
        print(f"Granted roles to {user\_name}")  
    except Exception as e:  
        print(f"Error creating/configuring user {user\_name}: {e}")

print("--- Milvus RBAC setup complete \---")

### **A.8. Neo4j Configuration Snippet (neo4j.conf)**

Properties

\# Add this section to your neo4j.conf file

\# Enable OIDC as an authentication and authorization provider.  
dbms.security.authentication\_providers\=oidc-azure  
dbms.security.authorization\_providers\=oidc-azure

\# \--- OIDC Provider Configuration for Entra ID \---  
dbms.security.oidc.azure.display\_name\=Sign in with Microsoft  
dbms.security.oidc.azure.auth\_flow\=pkce  
dbms.security.oidc.azure.well\_known\_discovery\_uri\=https://login.microsoftonline.com/\<Your-Tenant-ID\>/v2.0/.well-known/openid-configuration  
dbms.security.oidc.azure.params\=client\_id=\<Your-Client-ID\>;response\_type=code;scope=openid profile email  
dbms.security.oidc.azure.client\_secret\=\<Your-Client-Secret\>  
dbms.security.oidc.azure.claims.username\=sub  
dbms.security.oidc.azure.claims.groups\=groups  
dbms.security.oidc.azure.authorization.group\_to\_role\_mapping\="\<Entra-Group-ObjectID-1\>" \= admin; "\<Entra-Group-ObjectID-2\>" \= data\_analyst

### **A.9. Milvus Configuration Snippet (milvus.yaml)**

YAML

\# Ensure this section exists and is set to true in your milvus.yaml

common:  
  security:  
    authorizationEnabled: true

#### **Works cited**

1. Sign in to a Linux virtual machine in Azure by using Microsoft Entra ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/entra/identity/devices/howto-vm-sign-in-azure-ad-linux](https://learn.microsoft.com/en-us/entra/identity/devices/howto-vm-sign-in-azure-ad-linux)  
2. Server-Level Roles \- SQL \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/server-level-roles?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/server-level-roles?view=sql-server-ver17)  
3. SQL Server security best practices \- Learn Microsoft, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/sql-server-security-best-practices?view=sql-server-ver16](https://learn.microsoft.com/en-us/sql/relational-databases/security/sql-server-security-best-practices?view=sql-server-ver16)  
4. SQL Server Roles: A Practical Guide \- Satori Cyber, accessed August 12, 2025, [https://satoricyber.com/sql-server-security/sql-server-roles/](https://satoricyber.com/sql-server-security/sql-server-roles/)  
5. Configure Linux system auditing with auditd \- Red Hat, accessed August 12, 2025, [https://www.redhat.com/en/blog/configure-linux-auditing-auditd](https://www.redhat.com/en/blog/configure-linux-auditing-auditd)  
6. How to Conduct a Full Security Audit on Your Linux Server | SecOps® Solution, accessed August 12, 2025, [https://www.secopsolution.com/blog/how-to-conduct-a-full-security-audit-on-your-linux-server](https://www.secopsolution.com/blog/how-to-conduct-a-full-security-audit-on-your-linux-server)  
7. Troubleshoot Azure RBAC | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/role-based-access-control/troubleshooting](https://learn.microsoft.com/en-us/azure/role-based-access-control/troubleshooting)  
8. Tutorial: Create an Azure custom role with Azure CLI \- Azure RBAC ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/role-based-access-control/tutorial-custom-role-cli](https://learn.microsoft.com/en-us/azure/role-based-access-control/tutorial-custom-role-cli)  
9. How to back up using shell scripts \- Ubuntu Server documentation, accessed August 12, 2025, [https://documentation.ubuntu.com/server/how-to/backups/back-up-using-shell-scripts/](https://documentation.ubuntu.com/server/how-to/backups/back-up-using-shell-scripts/)  
10. Archive rotation shell script \- Ubuntu Server documentation, accessed August 12, 2025, [https://documentation.ubuntu.com/server/reference/backups/archive-rotation-shell-script/](https://documentation.ubuntu.com/server/reference/backups/archive-rotation-shell-script/)  
11. Logging into an Azure Linux VM using an Azure AD account \- Jorge Bernhardt, accessed August 12, 2025, [https://www.jorgebernhardt.com/vm-sign-in-azure-ad-linux/](https://www.jorgebernhardt.com/vm-sign-in-azure-ad-linux/)  
12. VM Extension Management with Azure Arc-Enabled Servers \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-vm-extensions](https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-vm-extensions)  
13. Join an Ubuntu VM to Microsoft Entra Domain Services | Azure Docs, accessed August 12, 2025, [https://docs.azure.cn/en-us/entra/identity/domain-services/join-ubuntu-linux-vm](https://docs.azure.cn/en-us/entra/identity/domain-services/join-ubuntu-linux-vm)  
14. Tutorial: Set up Microsoft Entra authentication for SQL Server, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/azure-ad-authentication-sql-server-setup-tutorial?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/azure-ad-authentication-sql-server-setup-tutorial?view=sql-server-ver17)  
15. Azure Arc and Defender for Servers: Connectivity and Monitoring Script, accessed August 12, 2025, [https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/azure-arc-and-defender-for-servers-connectivity-and-monitoring-script/4428271](https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/azure-arc-and-defender-for-servers-connectivity-and-monitoring-script/4428271)  
16. Resolving Azure Linux Agent Stuck at "Creating" Status for Azure Arc-enabled servers, accessed August 12, 2025, [https://learn.microsoft.com/en-us/answers/questions/1851238/resolving-azure-linux-agent-stuck-at-creating-stat](https://learn.microsoft.com/en-us/answers/questions/1851238/resolving-azure-linux-agent-stuck-at-creating-stat)  
17. Resolving Azure Linux Agent Stuck at "Creating" Status for Azure Arc-enabled servers, accessed August 12, 2025, [https://learn.microsoft.com/en-us/answers/questions/2086615/resolving-azure-linux-agent-stuck-at-creating-stat](https://learn.microsoft.com/en-us/answers/questions/2086615/resolving-azure-linux-agent-stuck-at-creating-stat)  
18. azcmagent check CLI reference \- Azure Arc | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-check](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-check)  
19. Deleting Lines with Sed: Master Advanced Techniques \- BitDoze, accessed August 12, 2025, [https://www.bitdoze.com/sed-delete-lines/](https://www.bitdoze.com/sed-delete-lines/)  
20. How to Use sed Command to Delete a Line {with Examples} \- phoenixNAP, accessed August 12, 2025, [https://phoenixnap.com/kb/sed-delete-line](https://phoenixnap.com/kb/sed-delete-line)  
21. Delete all lines containing a string from a file in Bash \- Sentry, accessed August 12, 2025, [https://sentry.io/answers/delete-all-lines-containing-a-string-from-a-file-in-bash/](https://sentry.io/answers/delete-all-lines-containing-a-string-from-a-file-in-bash/)  
22. Manage and maintain the Azure Connected Machine agent \- Azure ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-agent](https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-agent)  
23. azcmagent disconnect CLI reference \- Azure Arc \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-disconnect](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-disconnect)  
24. Troubleshoot Azure Connected Machine agent connection issues \- Azure Arc | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/troubleshoot-agent-onboard](https://learn.microsoft.com/en-us/azure/azure-arc/servers/troubleshoot-agent-onboard)  
25. SSH access to Azure Arc-enabled servers, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-arc/servers/ssh-arc-overview](https://docs.azure.cn/en-us/azure-arc/servers/ssh-arc-overview)  
26. Azure Linux SSH Authentication: Entra Id \- BuiltWithCaffeine, accessed August 12, 2025, [https://blog.builtwithcaffeine.cloud/posts/linux-azure-configure-entra-id-access/](https://blog.builtwithcaffeine.cloud/posts/linux-azure-configure-entra-id-access/)  
27. How to prevent public key login when using AADSSHLoginForLinux ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/answers/questions/1188639/how-to-prevent-public-key-login-when-using-aadsshl](https://learn.microsoft.com/en-us/answers/questions/1188639/how-to-prevent-public-key-login-when-using-aadsshl)  
28. Use \`az ssh vm\` (AADSSHLoginForLinux) with Ansible \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/77897648/use-az-ssh-vm-aadsshloginforlinux-with-ansible](https://stackoverflow.com/questions/77897648/use-az-ssh-vm-aadsshloginforlinux-with-ansible)  
29. Microsoft Entra authentication \- Azure SQL Database & Azure SQL Managed Instance & Azure Synapse Analytics | Azure Docs, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-sql/database/authentication-aad-overview](https://docs.azure.cn/en-us/azure-sql/database/authentication-aad-overview)  
30. Entra ID SQL authentication \- Nerdio Manager for Enterprise, accessed August 12, 2025, [https://nmehelp.getnerdio.com/hc/en-us/articles/26124311294733-Entra-ID-SQL-authentication](https://nmehelp.getnerdio.com/hc/en-us/articles/26124311294733-Entra-ID-SQL-authentication)  
31. Connecting to Azure SQL with Microsoft Entra ID in dbForge SQL Complete \- Documentation, accessed August 12, 2025, [https://docs.devart.com/sqlcomplete/getting-started/connecting-microsoft-entra-id-authentication-for-azure-sql-databases.html](https://docs.devart.com/sqlcomplete/getting-started/connecting-microsoft-entra-id-authentication-for-azure-sql-databases.html)  
32. Connect to Azure SQL Database with Microsoft Entra multifactor authentication, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-sql/database/active-directory-interactive-connect-azure-sql-db](https://docs.azure.cn/en-us/azure-sql/database/active-directory-interactive-connect-azure-sql-db)  
33. Configuring Neo4j Single Sign-On (SSO) \- Operations Manual, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/tutorial/tutorial-sso-configuration/](https://neo4j.com/docs/operations-manual/current/tutorial/tutorial-sso-configuration/)  
34. Neo4j Single Sign-on (SSO) Integration • SAML \- AuthDigital, accessed August 12, 2025, [https://authdigital.com/neo4j-single-sign-on](https://authdigital.com/neo4j-single-sign-on)  
35. Configure OIDC SSO for gallery and custom applications \- Microsoft Entra ID, accessed August 12, 2025, [https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/add-application-portal-setup-oidc-sso](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/add-application-portal-setup-oidc-sso)  
36. Authenticate User Access | milvus-operator, accessed August 12, 2025, [https://milvus-io.github.io/milvus-operator/docs/administration/security/enable-authentication.html](https://milvus-io.github.io/milvus-operator/docs/administration/security/enable-authentication.html)  
37. Authenticate User Access | Milvus Documentation, accessed August 12, 2025, [https://milvus.io/docs/authenticate.md](https://milvus.io/docs/authenticate.md)  
38. Azure custom roles \- Azure RBAC | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/role-based-access-control/custom-roles](https://learn.microsoft.com/en-us/azure/role-based-access-control/custom-roles)  
39. Assign Azure roles using Azure CLI \- Azure RBAC | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-cli](https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-cli)  
40. SQL Server Find Least Privilege for user account \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/53673404/sql-server-find-least-privilege-for-user-account](https://stackoverflow.com/questions/53673404/sql-server-find-least-privilege-for-user-account)  
41. Database-level roles \- SQL Server \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/database-level-roles?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/database-level-roles?view=sql-server-ver17)  
42. Property-based access control \- Operations Manual \- Neo4j, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/authentication-authorization/property-based-access-control/](https://neo4j.com/docs/operations-manual/current/authentication-authorization/property-based-access-control/)  
43. Role-based access control \- Operations Manual \- Neo4j, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/authentication-authorization/manage-privileges/](https://neo4j.com/docs/operations-manual/current/authentication-authorization/manage-privileges/)  
44. RBAC Explained | Milvus Documentation, accessed August 12, 2025, [https://milvus.io/docs/rbac.md](https://milvus.io/docs/rbac.md)  
45. SLES 12 SP5 | Security and Hardening Guide | Setting Up the Linux Audit Framework, accessed August 12, 2025, [https://documentation.suse.com/en-us/sles/12-SP5/html/SLES-all/cha-audit-setup.html](https://documentation.suse.com/en-us/sles/12-SP5/html/SLES-all/cha-audit-setup.html)