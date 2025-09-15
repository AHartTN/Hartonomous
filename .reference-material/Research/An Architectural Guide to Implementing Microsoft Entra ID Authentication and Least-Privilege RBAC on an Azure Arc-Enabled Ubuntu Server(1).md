

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

The following script, backup\_configs.sh, provides a complete, executable process. It archives specified directories into a compressed tarball, named with the server's hostname and a precise timestamp for clear versioning.

Bash

\#\!/bin/bash  
\#  
\# backup\_configs.sh \- A script to create a comprehensive backup of critical server configurations.  
\# This script is idempotent as it creates a uniquely timestamped backup file each time it runs.  
\#  
set \-e \# Exit immediately if a command exits with a non-zero status.

\# \--- Configuration \---  
\# List of directories and files to back up. Add all relevant application config paths.  
BACKUP\_TARGETS="/etc /home /var/log /var/lib/sqlserver /etc/neo4j /etc/milvus"  
\# Destination directory for the backup archive. Ensure this path has sufficient space and correct permissions.  
BACKUP\_DESTINATION="/mnt/backups"

\# \--- Execution \---  
echo "--- Preparing for backup \---"  
\# Create destination directory if it doesn't exist  
mkdir \-p "$BACKUP\_DESTINATION"  
echo "Backup destination: $BACKUP\_DESTINATION"

\# Create the archive filename.  
HOSTNAME=$(hostname \-s)  
TIMESTAMP=$(date \+"%Y%m%d-%H%M%S")  
ARCHIVE\_FILE="$HOSTNAME\-config-backup-$TIMESTAMP.tgz"  
ARCHIVE\_PATH="$BACKUP\_DESTINATION/$ARCHIVE\_FILE"

\# Print start status message.  
echo "Starting backup of critical configurations..."  
echo "Source targets: $BACKUP\_TARGETS"  
echo "Destination archive: $ARCHIVE\_PATH"  
date  
echo

\# Backup the files using tar.  
\# 'c' \- create archive  
\# 'z' \- compress with gzip  
\# 'p' \- preserve permissions  
\# 'f' \- use archive file  
sudo tar czpf "$ARCHIVE\_PATH" $BACKUP\_TARGETS

\# Verify exit code  
if \[ $? \-eq 0 \]; then  
  echo  
  echo "Backup completed successfully."  
  date  
  echo  
  echo "Archive details:"  
  ls \-lh "$ARCHIVE\_PATH"  
  echo  
  echo "IMPORTANT: Copy the backup file at $ARCHIVE\_PATH to a secure, off-server location."  
else  
  echo  
  echo "Backup FAILED. Please check for errors."  
  date  
  exit 1  
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
   \# Replace with your actual Resource Group and Server Name  
   az connectedmachine extension list \\  
     \--resource-group "YourResourceGroup" \\  
     \--machine-name "YourArcServerName" \\  
     \--output table

2. **Pluggable Authentication Modules (PAM) Configuration:** Older or alternative integration methods often modify the PAM stack. Inspect the PAM configuration for the SSH daemon and common authentication modules for any non-standard entries, particularly those referencing pam\_aad.so or pam\_sss.so.  
   Bash  
   grep \-E 'pam\_aad|pam\_sss' /etc/pam.d/sshd /etc/pam.d/common-auth

3. **SSH Daemon (sshd) Configuration:** The SSH server configuration file, /etc/ssh/sshd\_config, is a common point of modification. Scrutinize this file for specific directives that indicate a previous integration. The AADSSHLoginForLinux extension, for example, relies on certificate-based authentication and may add or modify AuthorizedKeysCommand or AuthenticationMethods directives. Also, look for AllowGroups or DenyGroups statements, as these can interfere with Entra ID group-based access control.1  
   Bash  
   grep \-iE 'AuthorizedKeysCommand|AuthenticationMethods|AllowGroups|DenyGroups' /etc/ssh/sshd\_config

4. **System Security Services Daemon (SSSD) and RealmD:** A common method for joining Linux machines to Active Directory domains (including Entra Domain Services) involves SSSD and the realmd service. Check if the server is joined to any domain and inspect the SSSD configuration file for any defined domains.14  
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

6. **SQL Server Principals:** The SQL Server instance itself may contain remnants of a prior Entra ID integration. Connect to the SQL Server instance and execute Transact-SQL queries against the system catalog views to identify any server-level logins or database-level users that are based on Entra ID principals. Principals from an external provider like Entra ID are identified by type 'E' (External User) or 'X' (External Group).15  
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

* **High-Level Status Check:** The azcmagent show command provides the most important top-level information, including the agent's connection status, the Azure resource ID it's mapped to, and the associated tenant ID. The status should report as "Connected".16  
  Bash  
  sudo azcmagent show

* **Detailed Network Connectivity Test:** The azcmagent check command performs a more granular series of network connectivity checks. It attempts to reach all required Azure service endpoints, verifying that network firewalls, proxies, or other intermediaries are not blocking critical communication paths. A successful output from this command provides high confidence that the agent can function reliably.18  
  Bash  
  sudo azcmagent check

* **Local Service Verification:** The agent relies on a local systemd service called the Hybrid Instance Metadata Service (HIMDS). Its status should be active and running.16  
  Bash  
  sudo systemctl status himds.service

If any of these checks fail, the underlying issues (e.g., network configuration, agent corruption) must be resolved before proceeding with the rest of this plan.

## **Phase 2: Surgical Decommissioning of Legacy Integrations**

Following the comprehensive audit in Phase 1, this phase focuses on the methodical and precise removal of all identified legacy or conflicting configurations. The goal is to revert the system to a known-good, non-integrated state, creating a clean foundation for the new implementation. The order of operations in this phase is critical to avoid creating orphaned resources or encountering dependency errors.

### **3.1. Scripted Removal of Conflicting Configurations**

To ensure consistency and minimize manual error, the removal process will be automated through a master shell script. This script will leverage standard Linux text-processing utilities like sed to perform surgical edits on configuration files, ensuring that only the targeted lines are removed.20

The following script, cleanup\_legacy\_integrations.sh, is a self-contained, idempotent utility. It checks for the existence of configurations before attempting to remove them.

Bash

\#\!/bin/bash  
\#  
\# cleanup\_legacy\_integrations.sh \- A script to surgically and idempotently remove legacy Entra ID configurations.  
\#  
set \-e \# Exit immediately if a command exits with a non-zero status.

\# \--- Configuration \---  
\# SET THESE VARIABLES to match your environment  
AZURE\_RESOURCE\_GROUP="YourResourceGroup"  
AZURE\_MACHINE\_NAME="YourArcServerName"

\# \--- Execution \---  
echo "--- Starting surgical cleanup of legacy configurations \---"

\# \--- SSHD Config Cleanup \---  
SSHD\_CONFIG="/etc/ssh/sshd\_config"  
echo "Checking $SSHD\_CONFIG for legacy settings..."  
\# Use grep \-q to check if patterns exist before calling sed  
if grep \-q 'AuthorizedKeysCommand /usr/bin/aadsshcert' "$SSHD\_CONFIG"; then  
    echo "Removing legacy 'AuthorizedKeysCommand' from $SSHD\_CONFIG..."  
    sudo sed \-i.bak \-e '/AuthorizedKeysCommand \\/usr\\/bin\\/aadsshcert/d' "$SSHD\_CONFIG"  
    echo "Original saved as $SSHD\_CONFIG.bak"  
fi  
if grep \-qE '^AllowGroups.\*' "$SSHD\_CONFIG"; then  
    echo "Removing legacy 'AllowGroups' directives from $SSHD\_CONFIG..."  
    sudo sed \-i.bak2 \-e '/^AllowGroups.\*/d' "$SSHD\_CONFIG"  
    echo "Original saved as $SSHD\_CONFIG.bak2"  
fi

\# \--- PAM Config Cleanup \---  
SSHD\_PAM\_CONFIG="/etc/pam.d/sshd"  
echo "Checking $SSHD\_PAM\_CONFIG for legacy PAM modules..."  
if grep \-q 'pam\_aad.so' "$SSHD\_PAM\_CONFIG"; then  
    echo "Removing 'pam\_aad.so' from $SSHD\_PAM\_CONFIG..."  
    sudo sed \-i.bak \-e '/pam\_aad.so/d' "$SSHD\_PAM\_CONFIG"  
    echo "Original saved as $SSHD\_PAM\_CONFIG.bak"  
fi

\# \--- SSSD and RealmD Uninstallation \---  
if systemctl \--all \--type\=service | grep \-q 'sssd.service'; then  
    echo "SSSD service found. Attempting to leave realm and purge packages..."  
    \# Attempt to leave any joined realms (command will fail gracefully if not joined)  
    if realm list | grep \-q 'domain-name'; then  
        REALM\_NAME=$(realm list | awk 'NR==2 {print $1}')  
        echo "Leaving realm: $REALM\_NAME"  
        sudo realm leave "$REALM\_NAME" |

| echo "Failed to leave realm, continuing cleanup."  
    fi  
    echo "Disabling and purging SSSD and related packages..."  
    sudo systemctl disable \--now sssd |

| echo "Failed to disable sssd, it may not be running."  
    sudo apt-get purge \-y sssd sssd-tools realmd adcli  
    echo "SSSD and RealmD packages have been purged."  
else  
    echo "SSSD service not found. Skipping SSSD cleanup."  
fi

\# \--- Legacy Extension Removal \---  
LEGACY\_EXT\_NAME="AADLoginForLinux"  
echo "Checking for legacy Azure VM extension '$LEGACY\_EXT\_NAME'..."  
if az connectedmachine extension show \--name "$LEGACY\_EXT\_NAME" \--machine-name "$AZURE\_MACHINE\_NAME" \--resource-group "$AZURE\_RESOURCE\_GROUP" &\>/dev/null; then  
    echo "Legacy extension found. Removing..."  
    az connectedmachine extension delete \\  
      \--name "$LEGACY\_EXT\_NAME" \\  
      \--machine-name "$AZURE\_MACHINE\_NAME" \\  
      \--resource-group "$AZURE\_RESOURCE\_GROUP" \\  
      \--yes  
    echo "Legacy extension removed."  
else  
    echo "No legacy extension '$LEGACY\_EXT\_NAME' found."  
fi

echo "--- Cleanup script finished. It is recommended to restart the sshd service. \---"  
echo "sudo systemctl restart sshd"

### **3.2. Resetting the Azure Arc Agent (If Necessary)**

In cases where the Azure Arc agent itself is suspected of being in a corrupted or unrecoverable state, a full uninstall and reinstall is the most reliable path to resolution. This process must be executed in a precise sequence to avoid orphaning cloud resources or leaving residual configurations on the local machine.

#### **Critical Sequence of Operations**

1. **Remove All VM Extensions:** Before touching the Arc agent, all extensions managed by it must be uninstalled. The agent is the control mechanism for these extensions; if the agent is disconnected or removed first, Azure loses its ability to manage the extensions, effectively orphaning them on the server. This is a critical first step.23  
   Bash  
   \# List all extensions  
   az connectedmachine extension list \-g YourResourceGroup \--machine-name YourArcServerName \-o table  
   \# Delete each extension by name  
   az connectedmachine extension delete \-g YourResourceGroup \--machine-name YourArcServerName \--name \<ExtensionName\> \--yes

2. **Disconnect the Agent:** On the server, run the azcmagent disconnect command. This performs two actions: it deletes the corresponding Azure Arc-enabled server resource in Azure Resource Manager and it clears the local agent's configuration and state.25  
   Bash  
   sudo azcmagent disconnect

   In the event that the resource has already been deleted from the Azure portal, this command will fail. In such cases, the \--force-local-only flag must be used to instruct the agent to clean up its local state without attempting to contact Azure.25  
   Bash  
   sudo azcmagent disconnect \--force-local-only

3. **Uninstall the Agent Software:** Once disconnected, the agent software package can be removed from the system. The azcmagent uninstall command handles the complete removal of the agent's binaries and service files.17  
   Bash  
   sudo azcmagent uninstall

4. **Reinstall and Reconnect:** After a full removal, the server can be re-onboarded to Azure Arc by running the standard installation script generated from the Azure portal. This ensures a fresh installation with a new identity and clean configuration.

### **3.3. Cleansing SQL Server Entra ID Principals**

To prevent conflicts with the new, granular RBAC model, any existing Entra ID-based logins and users within the SQL Server 2025 instance must be removed. This process involves dependencies that must be handled correctly; a login cannot be dropped if it has associated database users, and a user cannot be dropped if it owns objects within a database.28

The following T-SQL script automates this cleansing process. It dynamically generates and executes the necessary commands to iterate through all user databases, transfer ownership of any owned schemas or roles to dbo, drop the Entra ID database users, and finally, drop the server-level Entra ID logins.29

SQL

\-- \=================================================================================  
\-- Script: Cleanse-EntraIDPrincipals.sql  
\-- Description: Idempotently drops all Microsoft Entra ID database users and server logins.  
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

    \-- Transfer schema ownership for Entra principals  
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

    \-- Drop the Entra ID users  
    SET @sql\_command \= 'USE ' \+ QUOTENAME(@db\_name) \+ ';  
    DECLARE @local\_user\_name NVARCHAR(128), @drop\_sql NVARCHAR(MAX);  
    DECLARE user\_cursor CURSOR FOR  
    SELECT name FROM sys.database\_principals WHERE type IN (''E'', ''X''); \-- E=External User, X=External Group

    OPEN user\_cursor;  
    FETCH NEXT FROM user\_cursor INTO @local\_user\_name;  
    WHILE @@FETCH\_STATUS \= 0  
    BEGIN  
        PRINT ''  \- Dropping user: '' \+ QUOTENAME(@local\_user\_name) \+ '' from database '' \+ QUOTENAME(@db\_name);  
        SET @drop\_sql \= ''USE '' \+ QUOTENAME(@db\_name) \+ ''; DROP USER '' \+ QUOTENAME(@local\_user\_name) \+ '';'';  
        BEGIN TRY  
            EXEC sp\_executesql @drop\_sql;  
        END TRY  
        BEGIN CATCH  
            PRINT ''    \- FAILED to drop user '' \+ QUOTENAME(@local\_user\_name) \+ ''. It may still own objects. Please check manually.'';  
            PRINT ''    \- Error: '' \+ ERROR\_MESSAGE();  
        END CATCH  
        FETCH NEXT FROM user\_cursor INTO @local\_user\_name;  
    END;  
    CLOSE user\_cursor;  
    DEALLOCATE user\_cursor;';  
    EXEC sp\_executesql @sql\_command;

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

The AADSSHLoginForLinux extension is the Microsoft-supported mechanism for enabling Entra ID logins on Linux machines, including those managed by Azure Arc. Its deployment is managed through the Azure control plane. The following script automates the prerequisites and the idempotent installation of the extension.

Bash

\#\!/bin/bash  
\#  
\# setup\_ssh\_auth.sh \- Idempotently installs the AADSSHLoginForLinux extension and configures RBAC.  
\#  
set \-e \# Exit immediately if a command exits with a non-zero status.

\# \--- Configuration \---  
\# SET THESE VARIABLES to match your environment  
AZURE\_RESOURCE\_GROUP="YourResourceGroup"  
AZURE\_MACHINE\_NAME="YourArcServerName"  
ADMIN\_ENTRA\_GROUP\_NAME="SEC-Ubuntu-Server-Admins" \# Entra ID group for sudo access  
USER\_ENTRA\_GROUP\_NAME="SEC-Ubuntu-Server-Users"   \# Entra ID group for standard user access

\# \--- Execution \---  
echo "--- Starting Entra ID SSH Authentication Setup \---"

\# 1\. Register HybridConnectivity resource provider (idempotent)  
echo "Registering Microsoft.HybridConnectivity resource provider..."  
az provider register \--namespace Microsoft.HybridConnectivity  
\# Wait for registration to complete  
while\]; do  
    echo "Waiting for registration to complete..."  
    sleep 10  
done  
echo "Provider registered."

\# 2\. Install AADSSHLoginForLinux extension (idempotently)  
EXTENSION\_NAME="AADSSHLoginForLinux"  
echo "Checking for existing '$EXTENSION\_NAME' extension..."  
if az connectedmachine extension show \--name "$EXTENSION\_NAME" \--machine-name "$AZURE\_MACHINE\_NAME" \--resource-group "$AZURE\_RESOURCE\_GROUP" &\>/dev/null; then  
    echo "Extension '$EXTENSION\_NAME' already exists. Skipping installation."  
else  
    echo "Installing '$EXTENSION\_NAME' extension..."  
    az connectedmachine extension create \\  
      \--machine-name "$AZURE\_MACHINE\_NAME" \\  
      \--name "$EXTENSION\_NAME" \\  
      \--publisher "Microsoft.Azure.ActiveDirectory" \\  
      \--type "AADSSHLoginForLinux" \\  
      \--resource-group "$AZURE\_RESOURCE\_GROUP"  
    echo "Extension installed successfully."  
fi

\# 3\. Configure Azure RBAC for SSH Access (idempotently)  
echo "--- Configuring Azure RBAC for SSH Access \---"  
ARC\_SERVER\_ID=$(az connectedmachine show \--name "$AZURE\_MACHINE\_NAME" \-g "$AZURE\_RESOURCE\_GROUP" \--query "id" \-o tsv)

\# Admin Group Role Assignment  
echo "Processing Admin Group: '$ADMIN\_ENTRA\_GROUP\_NAME'"  
ADMIN\_GROUP\_ID=$(az ad group show \--group "$ADMIN\_ENTRA\_GROUP\_NAME" \--query "id" \-o tsv)  
if az role assignment list \--assignee "$ADMIN\_GROUP\_ID" \--role "Virtual Machine Administrator Login" \--scope "$ARC\_SERVER\_ID" \--query "" | grep \-q "id"; then  
    echo "Admin role assignment already exists for group '$ADMIN\_ENTRA\_GROUP\_NAME'."  
else  
    echo "Creating Admin role assignment for group '$ADMIN\_ENTRA\_GROUP\_NAME'..."  
    az role assignment create \\  
      \--assignee-object-id "$ADMIN\_GROUP\_ID" \\  
      \--assignee-principal-type Group \\  
      \--role "Virtual Machine Administrator Login" \\  
      \--scope "$ARC\_SERVER\_ID"  
    echo "Admin role assignment created."  
fi

\# User Group Role Assignment  
echo "Processing User Group: '$USER\_ENTRA\_GROUP\_NAME'"  
USER\_GROUP\_ID=$(az ad group show \--group "$USER\_ENTRA\_GROUP\_NAME" \--query "id" \-o tsv)  
if az role assignment list \--assignee "$USER\_GROUP\_ID" \--role "Virtual Machine User Login" \--scope "$ARC\_SERVER\_ID" \--query "" | grep \-q "id"; then  
    echo "User role assignment already exists for group '$USER\_ENTRA\_GROUP\_NAME'."  
else  
    echo "Creating User role assignment for group '$USER\_ENTRA\_GROUP\_NAME'..."  
    az role assignment create \\  
      \--assignee-object-id "$USER\_GROUP\_ID" \\  
      \--assignee-principal-type Group \\  
      \--role "Virtual Machine User Login" \\  
      \--scope "$ARC\_SERVER\_ID"  
    echo "User role assignment created."  
fi

echo "--- Entra ID SSH Authentication Setup Complete \---"

### **4.2. Hardening SSH Configuration**

To ensure that Entra ID is the exclusive authentication method for human users, the SSH daemon configuration must be hardened to disable weaker or alternative authentication mechanisms.

#### **Configuration in /etc/ssh/sshd\_config**

The following changes should be made to /etc/ssh/sshd\_config and the SSH service restarted.

* PasswordAuthentication no: This is the most critical hardening step. It completely disables the ability to log in with a password, mitigating brute-force attacks and credential theft risks. All interactive logins will be forced through the Entra ID certificate flow.13  
* ChallengeResponseAuthentication no: This disables keyboard-interactive authentication methods, which are often used as a fallback for password authentication.  
* PubkeyAuthentication yes: This directive **must** be set to yes. The authentication mechanism used by the AADSSHLoginForLinux extension is based on short-lived OpenSSH certificates, which are a form of public key cryptography. Disabling this would break the Entra ID login functionality.

After making these changes, restart the SSH service to apply them:

Bash

sudo systemctl restart sshd

### **4.3. Client-Side Connection and Validation**

The end-user experience for connecting to the server is streamlined through the Azure CLI.

#### **Client-Side Procedure**

1. **Install Prerequisites:** Users must have the Azure CLI installed on their client machine, along with the ssh extension.11  
   Bash  
   az extension add \--name ssh

2. **Authenticate to Azure:** Users must first authenticate their CLI session with their Entra ID credentials. This will typically open a web browser for interactive login.11  
   Bash  
   az login

3. **Connect to the Server:** The connection is initiated using the az ssh vm command. This command transparently handles the entire authentication flow: it requests a short-lived certificate from Entra ID based on the user's logged-in identity, and then uses that certificate to authenticate to the server.31  
   Bash  
   az ssh vm \--name "YourArcServerName" \--resource-group "YourResourceGroup"

A successful login prompt and subsequent shell access confirm that the entire authentication chain—from the user's az login to the Entra ID token issuance, the Azure RBAC check, and the on-server VM extension processing—is functioning correctly.

## **Phase 4: Integrating Application-Layer Authentication with Entra ID**

With the host operating system secured, the focus shifts to integrating the data services running on the server. Each application—SQL Server 2025, Neo4j, and Milvus—has a distinct authentication architecture, requiring a tailored approach to align with the central identity strategy.

### **5.1. SQL Server 2025 via Azure Arc**

For SQL Server instances enabled by Azure Arc, Microsoft provides a native, tightly-coupled integration with Entra ID. The Azure Arc agent acts as a secure bridge, allowing the Azure control plane to configure and manage Entra ID authentication directly on the on-premises instance.15

#### **Azure-Side Configuration**

The setup process involves creating several resources in Azure that will facilitate the trust relationship between the on-premises SQL Server and Entra ID. This process is complex and best performed using the Azure CLI script provided by Microsoft, which automates the creation of the App Registration and Key Vault certificate.15

#### **SQL Server-Side Configuration (T-SQL)**

Once the Azure-side configuration is complete, the designated Entra ID administrator can connect to the SQL instance. The next step is to create server-level logins for the Entra ID security groups that will be granted access.

SQL

\-- Connect to the 'master' database as the Entra ID admin  
USE master;  
GO

\-- Create logins for the Entra ID security groups if they don't already exist  
IF NOT EXISTS (SELECT 1 FROM sys.server\_principals WHERE name \= 'Your-Entra-Group-For-SQL-Admins')  
BEGIN  
    PRINT 'Creating login for Your-Entra-Group-For-SQL-Admins...';  
    CREATE LOGIN FROM EXTERNAL PROVIDER;  
END  
GO

IF NOT EXISTS (SELECT 1 FROM sys.server\_principals WHERE name \= 'Your-Entra-Group-For-SQL-Users')  
BEGIN  
    PRINT 'Creating login for Your-Entra-Group-For-SQL-Users...';  
    CREATE LOGIN FROM EXTERNAL PROVIDER;  
END  
GO

These commands create server principals that are linked to the corresponding Entra ID objects, enabling members of those groups to authenticate to the SQL Server instance.15

#### **Validation**

To validate the configuration, use a client tool like SQL Server Management Studio (SSMS) or Azure Data Studio. Attempt to connect to the on-premises SQL Server instance, but instead of using SQL Authentication, select one of the "Azure Active Directory" authentication methods, such as "Azure Active Directory \- Universal with MFA". Log in with the credentials of a user who is a member of one of the configured security groups. A successful connection validates the end-to-end authentication flow.33

### **5.2. Neo4j via OpenID Connect (OIDC)**

Neo4j supports modern authentication standards, including OpenID Connect (OIDC), which allows it to delegate authentication to an external identity provider like Microsoft Entra ID.36

#### **Azure-Side Configuration**

1. **Create Entra App Registration:** In the Entra admin center, register a new application for Neo4j.38  
2. **Configure Redirect URI:** Configure a "Single-page application (SPA)" type URI pointing to your Neo4j Browser interface, for example: http://\<your-server-ip\>:7474/browser/.36  
3. **Configure API Permissions:** Grant the openid, profile, and email delegated permissions under the Microsoft Graph API.38  
4. **Configure Token Claims for Group-Based Authorization:** In the "Token configuration" section of the app registration, add a "groups claim" and configure it to include "Security groups" in the "ID" token. This will add a groups claim containing an array of the user's group Object IDs to the token that Neo4j receives.36  
5. **Create a Client Secret:** Create a client secret and copy its value immediately for use in the Neo4j configuration.38

#### **Neo4j-Side Configuration (neo4j.conf)**

The following script, configure\_neo4j.sh, uses the yq command-line tool to idempotently set the required OIDC parameters in the neo4j.conf file. It assumes yq is installed.

Bash

\#\!/bin/bash  
\#  
\# configure\_neo4j.sh \- Idempotently configures neo4j.conf for Entra ID OIDC authentication.  
\# Requires 'yq' (https://github.com/mikefarah/yq) to be installed.  
\#  
set \-e

\# \--- Configuration \---  
\# SET THESE VARIABLES based on your Entra ID App Registration  
NEO4J\_CONF\_PATH="/etc/neo4j/neo4j.conf"  
ENTRA\_TENANT\_ID="your-tenant-id-from-azure"  
ENTRA\_CLIENT\_ID="your-client-id-from-azure"  
ENTRA\_CLIENT\_SECRET="your-client-secret-from-azure"  
\# Mapping format: "'\<Group-Object-ID-1\>' \= neo4j\_role1; '\<Group-Object-ID-2\>' \= neo4j\_role2"  
GROUP\_TO\_ROLE\_MAPPING="'e8b6ddfa-688d-4ace-987d-6cc5516af188' \= admin; '9e2a31e1-bdd1-47fe-844d-767502bd138d' \= reader"

\# \--- Pre-flight Check \---  
if\! command \-v yq &\> /dev/null; then  
    echo "Error: yq is not installed. Please install it to continue."  
    echo "See: https://github.com/mikefarah/yq"  
    exit 1  
fi  
if; then  
    echo "Error: Neo4j config file not found at $NEO4J\_CONF\_PATH"  
    exit 1  
fi

\# \--- Execution \---  
echo "--- Configuring Neo4j for Entra ID OIDC \---"

\# Use yq to set values. The '-i' flag modifies the file in-place.  
\# This is idempotent; running it again with the same values results in no change.  
yq \-i '.dbms.security.authentication\_providers=\["oidc-azure"\]' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.authorization\_providers=\["oidc-azure"\]' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.oidc.azure.display\_name="Sign in with Microsoft"' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.oidc.azure.auth\_flow="pkce"' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.oidc.azure.well\_known\_discovery\_uri="https://login.microsoftonline.com/'$ENTRA\_TENANT\_ID'/v2.0/.well-known/openid-configuration"' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.oidc.azure.params="client\_id='$ENTRA\_CLIENT\_ID';response\_type=code;scope=openid profile email"' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.oidc.azure.client\_secret="'$ENTRA\_CLIENT\_SECRET'"' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.oidc.azure.claims.username="sub"' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.oidc.azure.claims.groups="groups"' "$NEO4J\_CONF\_PATH"  
yq \-i '.dbms.security.oidc.azure.authorization.group\_to\_role\_mapping="'"$GROUP\_TO\_ROLE\_MAPPING"'"' "$NEO4J\_CONF\_PATH"

echo "Neo4j configuration updated successfully."  
echo "Please restart the Neo4j service for changes to take effect: sudo systemctl restart neo4j"

### **5.3. Strategy for Milvus Authentication**

Milvus primarily uses an internal username and password system and does not currently offer native integration with external identity providers via standards like OIDC or SAML.39 Therefore, a "loosely-coupled" architecture is required.

1. **Enable Milvus Internal Authentication:** The following script, configure\_milvus.sh, uses yq to idempotently enable authentication in the milvus.yaml file.40  
   Bash  
   \#\!/bin/bash  
   \#  
   \# configure\_milvus.sh \- Idempotently enables authentication in milvus.yaml.  
   \# Requires 'yq' (https://github.com/mikefarah/yq) to be installed.  
   \#  
   set \-e  
   MILVUS\_CONF\_PATH="/etc/milvus/milvus.yaml" \# Adjust if your path is different

   if\! command \-v yq &\> /dev/null; then  
       echo "Error: yq is not installed."  
       exit 1  
   fi  
   if; then  
       echo "Error: Milvus config file not found at $MILVUS\_CONF\_PATH"  
       exit 1  
   fi

   echo "--- Enabling Milvus Authentication \---"  
   yq \-i '.common.security.authorizationEnabled \= true' "$MILVUS\_CONF\_PATH"  
   echo "Milvus authentication enabled in $MILVUS\_CONF\_PATH."  
   echo "Please restart the Milvus service for changes to take effect."

2. **Create Service Principals in Entra ID:** For each application that needs to connect to Milvus, create a dedicated Service Principal in Microsoft Entra ID.  
3. **Secure Credential Storage in Azure Key Vault:** Create dedicated user accounts within Milvus (detailed in Phase 5). Store the generated username and password for these accounts as secrets within an Azure Key Vault.  
4. **Grant Service Principal Access to Key Vault:** Configure an access policy on the Key Vault to grant the application's Service Principal get permission for the specific secrets containing the Milvus credentials.  
5. **Application-Managed Connection Flow:** The application authenticates to Entra ID using its Service Principal, retrieves the Milvus credentials from Key Vault, and then uses those credentials to connect to the Milvus server.39

## **Phase 5: A Multi-Layered, Least-Privilege RBAC Architecture**

A robust security posture requires a granular, multi-layered Role-Based Access Control (RBAC) model. This phase details the design and implementation of such a model, applying the principle of least privilege at every level.

### **6.1. Azure Control Plane: Custom Role for Arc Server Operators**

To allow operators to manage the Azure Arc-enabled server without granting broad permissions, a custom Azure role is necessary.

#### **JSON Role Definition (ArcServerOperatorRole.json)**

JSON

{  
  "Name": "Arc Server Operator",  
  "IsCustom": true,  
  "Description": "Allows management of Azure Arc-enabled servers and their extensions, including SSH access configuration.",  
  "Actions": \[  
    "Microsoft.HybridCompute/machines/read",  
    "Microsoft.HybridCompute/machines/write",  
    "Microsoft.HybridCompute/machines/extensions/read",  
    "Microsoft.HybridCompute/machines/extensions/write",  
    "Microsoft.HybridCompute/machines/extensions/delete",  
    "Microsoft.HybridConnectivity/endpoints/read",  
    "Microsoft.HybridConnectivity/endpoints/write",  
    "Microsoft.GuestConfiguration/guestConfigurationAssignments/read"  
  \],  
  "NotActions":,  
  "DataActions":,  
  "NotDataActions":,  
  "AssignableScopes":  
}

#### **Idempotent Deployment Script**

This script dynamically sets the assignable scope and creates or updates the custom role, making it fully self-sufficient.41

Bash

\#\!/bin/bash  
\#  
\# deploy\_custom\_role.sh \- Idempotently creates or updates the 'Arc Server Operator' custom role.  
\#  
set \-e

\# \--- Configuration \---  
AZURE\_RESOURCE\_GROUP="YourResourceGroup"  
ROLE\_DEFINITION\_FILE="ArcServerOperatorRole.json"

\# \--- Execution \---  
echo "--- Deploying Custom Azure Role \---"

\# 1\. Get Subscription and Resource Group IDs  
SUBSCRIPTION\_ID=$(az account show \--query id \-o tsv)  
SCOPE="/subscriptions/$SUBSCRIPTION\_ID/resourceGroups/$AZURE\_RESOURCE\_GROUP"

\# 2\. Dynamically update the JSON file with the correct scope  
\# Create a temporary file for the updated role definition  
TEMP\_ROLE\_FILE=$(mktemp)  
jq \--arg scope "$SCOPE" '.AssignableScopes \= \[$scope\]' "$ROLE\_DEFINITION\_FILE" \> "$TEMP\_ROLE\_FILE"  
echo "Custom role will be assignable to scope: $SCOPE"

\# 3\. Check if the custom role already exists and create or update  
ROLE\_NAME=$(jq \-r.Name "$TEMP\_ROLE\_FILE")  
if az role definition list \--name "$ROLE\_NAME" \--custom-role-only true \--scope "$SCOPE" | grep \-q "$ROLE\_NAME"; then  
    echo "Role '$ROLE\_NAME' already exists. Updating definition..."  
    az role definition update \--role-definition "$TEMP\_ROLE\_FILE"  
else  
    echo "Role '$ROLE\_NAME' does not exist. Creating..."  
    az role definition create \--role-definition "$TEMP\_ROLE\_FILE"  
fi

\# 4\. Clean up temporary file  
rm "$TEMP\_ROLE\_FILE"

if \[ $? \-eq 0 \]; then  
    echo "Custom role deployment completed successfully."  
else  
    echo "Custom role deployment FAILED."  
fi

### **6.2. SQL Server Data Plane: Application-Centric Database Roles**

Within SQL Server, access should be granted through user-defined database roles tailored to specific application functions.3

#### **T-SQL Script for Idempotent Role Creation and Mapping**

This script idempotently creates roles, users, and permissions.45

SQL

\-- setup\_sql\_server\_rbac.sql  
\-- This script must be run in the context of your application database.  
\-- It idempotently creates roles, users from Entra ID logins, and assigns permissions.

\-- \--- Configuration \---  
\-- Set these variables before execution  
DECLARE @AppReadOnlyGroup NVARCHAR(128) \= 'Your-Entra-Group-For-SQL-Users';  
DECLARE @AppReadWriteGroup NVARCHAR(128) \= 'Your-Entra-Group-For-App-Backend';  
DECLARE @AppExecutorGroup NVARCHAR(128) \= 'Your-Entra-Group-For-App-Backend';  
\-- \--- End Configuration \---

USE YourApplicationDatabase;  
GO

\-- Create a dedicated schema for the application if it doesn't exist  
IF NOT EXISTS (SELECT \* FROM sys.schemas WHERE name \= 'app')  
BEGIN  
    EXEC('CREATE SCHEMA app');  
    PRINT 'Schema \[app\] created.';  
END  
GO

\-- Create application-specific database roles if they don't exist  
IF NOT EXISTS (SELECT 1 FROM sys.database\_principals WHERE name \= 'app\_read\_only' AND type \= 'R')  
BEGIN  
    CREATE ROLE app\_read\_only;  
    PRINT 'Database role \[app\_read\_only\] created.';  
END  
IF NOT EXISTS (SELECT 1 FROM sys.database\_principals WHERE name \= 'app\_read\_write' AND type \= 'R')  
BEGIN  
    CREATE ROLE app\_read\_write;  
    PRINT 'Database role \[app\_read\_write\] created.';  
END  
IF NOT EXISTS (SELECT 1 FROM sys.database\_principals WHERE name \= 'app\_executor' AND type \= 'R')  
BEGIN  
    CREATE ROLE app\_executor;  
    PRINT 'Database role \[app\_executor\] created.';  
END  
GO

\-- Grant least-privilege permissions to the roles on the application schema  
GRANT SELECT ON SCHEMA::app TO app\_read\_only;  
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::app TO app\_read\_write;  
GRANT EXECUTE ON SCHEMA::app TO app\_executor;  
PRINT 'Permissions granted to roles.';  
GO

\-- Create database users mapped to the Entra ID group logins if they don't exist  
IF NOT EXISTS (SELECT 1 FROM sys.database\_principals WHERE name \= @AppReadOnlyGroup)  
BEGIN  
    CREATE USER FROM LOGIN;  
    PRINT 'Database user created from Entra login.';  
END  
IF NOT EXISTS (SELECT 1 FROM sys.database\_principals WHERE name \= @AppReadWriteGroup)  
BEGIN  
    CREATE USER FROM LOGIN;  
    PRINT 'Database user created from Entra login.';  
END  
GO

\-- Add the database users to the appropriate roles if they are not already members  
IF NOT EXISTS (SELECT 1 FROM sys.database\_role\_members rm JOIN sys.database\_principals role ON rm.role\_principal\_id \= role.principal\_id JOIN sys.database\_principals member ON rm.member\_principal\_id \= member.principal\_id WHERE role.name \= 'app\_read\_only' AND member.name \= @AppReadOnlyGroup)  
BEGIN  
    ALTER ROLE app\_read\_only ADD MEMBER;  
    PRINT 'User added to role \[app\_read\_only\].';  
END  
IF NOT EXISTS (SELECT 1 FROM sys.database\_role\_members rm JOIN sys.database\_principals role ON rm.role\_principal\_id \= role.principal\_id JOIN sys.database\_principals member ON rm.member\_principal\_id \= member.principal\_id WHERE role.name \= 'app\_read\_write' AND member.name \= @AppReadWriteGroup)  
BEGIN  
    ALTER ROLE app\_read\_write ADD MEMBER;  
    PRINT 'User added to role \[app\_read\_write\].';  
END  
IF NOT EXISTS (SELECT 1 FROM sys.database\_role\_members rm JOIN sys.database\_principals role ON rm.role\_principal\_id \= role.principal\_id JOIN sys.database\_principals member ON rm.member\_principal\_id \= member.principal\_id WHERE role.name \= 'app\_executor' AND member.name \= @AppExecutorGroup)  
BEGIN  
    ALTER ROLE app\_executor ADD MEMBER;  
    PRINT 'User \[' \+ @AppExecutorGroup \+ '\] added to role \[app\_executor\].';  
END  
GO

PRINT 'SQL Server RBAC setup complete.';  
GO

### **6.3. Neo4j Data Plane: Graph-Based Access Control**

Neo4j provides a powerful, Cypher-based syntax for defining fine-grained access controls.48

#### **Cypher Script for Idempotent Role and Privilege Management**

This script uses the IF NOT EXISTS clause to create roles idempotently.50

Cypher

// setup\_neo4j\_rbac.cypher  
// Run this script as an admin user in Neo4j.

// Create roles for different user personas if they do not already exist  
CREATE ROLE data\_analyst IF NOT EXISTS;  
CREATE ROLE data\_scientist IF NOT EXISTS;

// Grant privileges to the data\_analyst role (GRANT/DENY are idempotent)  
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

### **6.4. Milvus Data Plane: Internal RBAC Model**

This Python script uses the pymilvus library to configure the internal RBAC model. It now generates secure, random passwords and prints them for storage in Azure Key Vault, making it self-sufficient.52

Python

\# setup\_milvus\_rbac.py  
import os  
import secrets  
import string  
from pymilvus import MilvusClient

\# \--- Configuration \---  
MILVUS\_URI \= os.getenv("MILVUS\_URI", "http://localhost:19530")  
\# Use the root credentials for initial setup, preferably from environment variables  
ROOT\_TOKEN \= os.getenv("MILVUS\_ROOT\_TOKEN", "root:Milvus")

\# \--- Helper Function \---  
def generate\_password(length=16):  
    """Generates a secure, random password."""  
    alphabet \= string.ascii\_letters \+ string.digits \+ string.punctuation  
    while True:  
        password \= ''.join(secrets.choice(alphabet) for i in range(length))  
        if (any(c.islower() for c in password)  
                and any(c.isupper() for c in password)  
                and any(c.isdigit() for c in password)  
                and any(c in string.punctuation for c in password)):  
            break  
    return password

\# \--- Execution \---  
client \= MilvusClient(uri=MILVUS\_URI, token=ROOT\_TOKEN)  
generated\_credentials \= {}

print("--- Setting up Milvus RBAC \---")

\# Define roles and their privileges  
ROLES\_TO\_CREATE \= {  
    "milvus\_reader":,  
    "milvus\_writer":  
}

\# Create roles idempotently  
for role\_name, privileges in ROLES\_TO\_CREATE.items():  
    try:  
        print(f"Creating role: {role\_name}")  
        client.create\_role(role\_name=role\_name)  
        for privilege in privileges:  
            client.grant\_privilege(  
                role\_name=role\_name,  
                object\_type="Collection",  
                object\_name="\*",  
                privilege=privilege  
            )  
        print(f"Granted privileges to {role\_name}")  
    except Exception as e:  
        if "already exist" in str(e):  
            print(f"Role '{role\_name}' already exists. Skipping creation.")  
        else:  
            print(f"Error creating/configuring role {role\_name}: {e}")

\# Define users and their assigned roles  
USERS\_TO\_CREATE \= {  
    "app\_service\_user\_ro": {"roles": \["milvus\_reader"\]},  
    "app\_service\_user\_rw": {"roles": \["milvus\_writer"\]}  
}

\# Create users idempotently and generate passwords  
for user\_name, details in USERS\_TO\_CREATE.items():  
    try:  
        new\_password \= generate\_password()  
        print(f"Creating user: {user\_name}")  
        client.create\_user(user\_name=user\_name, password=new\_password)  
        generated\_credentials\[user\_name\] \= new\_password  
        for role in details\["roles"\]:  
            client.grant\_role(user\_name=user\_name, role\_name=role)  
        print(f"Granted roles to {user\_name}")  
    except Exception as e:  
        if "already exist" in str(e):  
            print(f"User '{user\_name}' already exists. Skipping creation.")  
        else:  
            print(f"Error creating/configuring user {user\_name}: {e}")

print("\\n--- Milvus RBAC setup complete \---")  
if generated\_credentials:  
    print("\\n\!\!\! IMPORTANT: Store these generated passwords securely in Azure Key Vault\!\!\!")  
    for user, password in generated\_credentials.items():  
        print(f"  Username: {user}")  
        print(f"  Password: {password}\\n")

## **Phase 6: End-to-End Validation and Operational Monitoring**

The final phase involves a comprehensive validation of the entire security architecture, followed by the establishment of ongoing monitoring practices.

### **7.1. Comprehensive Validation Checklist**

A structured test plan is required to verify that every component of the authentication and authorization system is working correctly.

| Test Case ID | User Persona (Entra Group) | Action | Expected Outcome |
| :---- | :---- | :---- | :---- |
| **SSH-01** | SEC-Ubuntu-Server-Admins | Connect via az ssh vm. | Successful login to shell. |
| **SSH-02** | SEC-Ubuntu-Server-Admins | Run sudo whoami. | Command succeeds, outputs root. |
| **SSH-03** | SEC-SQL-Data-Scientists | Connect via az ssh vm. | Successful login to shell. |
| **SSH-04** | SEC-SQL-Data-Scientists | Run sudo whoami. | Command fails with permission error. |
| **SQL-01** | SEC-SQL-Data-Scientists | Connect to SQL Server via SSMS with Entra ID auth. | Successful connection. |
| **SQL-02** | SEC-SQL-Data-Scientists | Run SELECT \* FROM app.MyTable; | Query succeeds. |
| **SQL-03** | SEC-SQL-Data-Scientists | Run UPDATE app.MyTable SET...; | Query fails with permission error. |
| **NEO4J-01** | SEC-Neo4j-Analysts | Log in to Neo4j Browser via OIDC. | Successful login after Entra ID redirect. |
| **NEO4J-02** | SEC-Neo4j-Analysts | Run MATCH (p:Person) RETURN p.name; | Query succeeds. |
| **NEO4J-03** | SEC-Neo4j-Analysts | Run MATCH (p:Person) RETURN p.ssn; | Query fails or returns nulls due to permission error. |
| **MILVUS-01** | Application using app\_service\_user\_ro | Connect to Milvus and perform a search. | Connection and search succeed. |
| **MILVUS-02** | Application using app\_service\_user\_ro | Attempt to insert data into a collection. | Operation fails with permission error. |

### **7.2. Auditing and Monitoring**

Effective security requires continuous monitoring to detect anomalies and unauthorized access attempts.

* **Microsoft Entra ID Sign-in Logs:** Provides a centralized audit trail for all authentication events.  
* **Azure Monitor:** The Azure Activity Log captures all management actions against the Azure Arc-enabled server resource.  
* **Ubuntu Server Logs:** The primary log file for authentication events on the server is /var/log/auth.log.  
* **Database Audit Logs:** Both SQL Server and Neo4j have robust, built-in auditing capabilities that should be configured.  
* **Linux Audit Daemon (auditd):** For advanced host-level intrusion detection, configure auditd to monitor critical system files like /etc/ssh/sshd\_config for unauthorized changes.5

## **Conclusion**

The successful execution of this architectural plan will transform the security posture of the on-premises Ubuntu server from a state of ambiguity to one of clarity, control, and compliance. By systematically decommissioning legacy configurations and implementing a modern, centralized identity solution based on Microsoft Entra ID, the server's attack surface is significantly reduced. The integration of the host OS and its diverse application stack—SQL Server 2025, Neo4j, and Milvus—under a single identity provider streamlines administration, enhances security through policies like Conditional Access, and provides a unified audit trail. The cornerstone of this architecture is the multi-layered, least-privilege RBAC model, which is fundamental to a zero-trust security strategy. The final configured state is not only secure but also manageable and scalable, aligning this critical on-premises resource with modern cloud security best practices.

#### **Works cited**

1. Sign in to a Linux virtual machine in Azure by using Microsoft Entra ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/entra/identity/devices/howto-vm-sign-in-azure-ad-linux](https://learn.microsoft.com/en-us/entra/identity/devices/howto-vm-sign-in-azure-ad-linux)  
2. Server-Level Roles \- SQL \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/server-level-roles?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/server-level-roles?view=sql-server-ver17)  
3. SQL Server security best practices \- Learn Microsoft, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/sql-server-security-best-practices?view=sql-server-ver16](https://learn.microsoft.com/en-us/sql/relational-databases/security/sql-server-security-best-practices?view=sql-server-ver16)  
4. The Principle of Least Privilege in SQL Server Security \- IDERA, accessed August 12, 2025, [https://www.idera.com/blogs/understanding-the-principle-of-least-privilege-in-sql-server-security/](https://www.idera.com/blogs/understanding-the-principle-of-least-privilege-in-sql-server-security/)  
5. Configure Linux system auditing with auditd \- Red Hat, accessed August 12, 2025, [https://www.redhat.com/en/blog/configure-linux-auditing-auditd](https://www.redhat.com/en/blog/configure-linux-auditing-auditd)  
6. How to Conduct a Full Security Audit on Your Linux Server | SecOps® Solution, accessed August 12, 2025, [https://www.secopsolution.com/blog/how-to-conduct-a-full-security-audit-on-your-linux-server](https://www.secopsolution.com/blog/how-to-conduct-a-full-security-audit-on-your-linux-server)  
7. Terraform \- Mastering Idempotency Violations \- Handling Resource Conflicts and Failures in Azure \- DEV Community, accessed August 12, 2025, [https://dev.to/pwd9000/terraform-mastering-idempotency-violations-handling-resource-conflicts-and-failures-in-azure-3f3d](https://dev.to/pwd9000/terraform-mastering-idempotency-violations-handling-resource-conflicts-and-failures-in-azure-3f3d)  
8. az role assignment create is not idempotent · Issue \#8568 · Azure/azure-cli \- GitHub, accessed August 12, 2025, [https://github.com/Azure/azure-cli/issues/8568](https://github.com/Azure/azure-cli/issues/8568)  
9. How to back up using shell scripts \- Ubuntu Server documentation, accessed August 12, 2025, [https://documentation.ubuntu.com/server/how-to/backups/back-up-using-shell-scripts/](https://documentation.ubuntu.com/server/how-to/backups/back-up-using-shell-scripts/)  
10. Archive rotation shell script \- Ubuntu Server documentation, accessed August 12, 2025, [https://documentation.ubuntu.com/server/reference/backups/archive-rotation-shell-script/](https://documentation.ubuntu.com/server/reference/backups/archive-rotation-shell-script/)  
11. Logging into an Azure Linux VM using an Azure AD account \- Jorge Bernhardt, accessed August 12, 2025, [https://www.jorgebernhardt.com/vm-sign-in-azure-ad-linux/](https://www.jorgebernhardt.com/vm-sign-in-azure-ad-linux/)  
12. VM Extension Management with Azure Arc-Enabled Servers \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-vm-extensions](https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-vm-extensions)  
13. How to prevent public key login when using AADSSHLoginForLinux ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/answers/questions/1188639/how-to-prevent-public-key-login-when-using-aadsshl](https://learn.microsoft.com/en-us/answers/questions/1188639/how-to-prevent-public-key-login-when-using-aadsshl)  
14. Join an Ubuntu VM to Microsoft Entra Domain Services | Azure Docs, accessed August 12, 2025, [https://docs.azure.cn/en-us/entra/identity/domain-services/join-ubuntu-linux-vm](https://docs.azure.cn/en-us/entra/identity/domain-services/join-ubuntu-linux-vm)  
15. Tutorial: Set up Microsoft Entra authentication for SQL Server, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/azure-ad-authentication-sql-server-setup-tutorial?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/azure-ad-authentication-sql-server-setup-tutorial?view=sql-server-ver17)  
16. Azure Arc and Defender for Servers: Connectivity and Monitoring Script, accessed August 12, 2025, [https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/azure-arc-and-defender-for-servers-connectivity-and-monitoring-script/4428271](https://techcommunity.microsoft.com/blog/coreinfrastructureandsecurityblog/azure-arc-and-defender-for-servers-connectivity-and-monitoring-script/4428271)  
17. Resolving Azure Linux Agent Stuck at "Creating" Status for Azure Arc-enabled servers, accessed August 12, 2025, [https://learn.microsoft.com/en-us/answers/questions/1851238/resolving-azure-linux-agent-stuck-at-creating-stat](https://learn.microsoft.com/en-us/answers/questions/1851238/resolving-azure-linux-agent-stuck-at-creating-stat)  
18. azcmagent check CLI reference \- Azure Arc | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-check](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-check)  
19. Better Azure Arc Agent Onboarding Script \- GitHub Gist, accessed August 12, 2025, [https://gist.github.com/JustinGrote/b7fac2b239420b4befd753d21952c3ec](https://gist.github.com/JustinGrote/b7fac2b239420b4befd753d21952c3ec)  
20. Deleting Lines with Sed: Master Advanced Techniques \- BitDoze, accessed August 12, 2025, [https://www.bitdoze.com/sed-delete-lines/](https://www.bitdoze.com/sed-delete-lines/)  
21. How to Use sed Command to Delete a Line {with Examples} \- phoenixNAP, accessed August 12, 2025, [https://phoenixnap.com/kb/sed-delete-line](https://phoenixnap.com/kb/sed-delete-line)  
22. How to delete from a text file, all lines that contain a specific string? \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/5410757/how-to-delete-from-a-text-file-all-lines-that-contain-a-specific-string](https://stackoverflow.com/questions/5410757/how-to-delete-from-a-text-file-all-lines-that-contain-a-specific-string)  
23. Manage and maintain the Azure Connected Machine agent \- Azure ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-agent](https://learn.microsoft.com/en-us/azure/azure-arc/servers/manage-agent)  
24. Install and Manage the Azure Monitor Agent \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-monitor/agents/azure-monitor-agent-manage](https://learn.microsoft.com/en-us/azure/azure-monitor/agents/azure-monitor-agent-manage)  
25. azcmagent disconnect CLI reference \- Azure Arc \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-disconnect](https://learn.microsoft.com/en-us/azure/azure-arc/servers/azcmagent-disconnect)  
26. Troubleshoot Azure Connected Machine agent connection issues \- Azure Arc | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/azure-arc/servers/troubleshoot-agent-onboard](https://learn.microsoft.com/en-us/azure/azure-arc/servers/troubleshoot-agent-onboard)  
27. Resolving Azure Linux Agent Stuck at "Creating" Status for Azure Arc-enabled servers, accessed August 12, 2025, [https://learn.microsoft.com/en-us/answers/questions/2086615/resolving-azure-linux-agent-stuck-at-creating-stat](https://learn.microsoft.com/en-us/answers/questions/2086615/resolving-azure-linux-agent-stuck-at-creating-stat)  
28. How to drop a SQL Server Login and all its dependencies, accessed August 12, 2025, [https://www.sqlshack.com/drop-sql-server-login-dependencies/](https://www.sqlshack.com/drop-sql-server-login-dependencies/)  
29. SQL Server – Drop An User In All Databases & Drop The Login Too\! \- SQL Jana, accessed August 12, 2025, [https://sqljana.wordpress.com/2017/01/12/sql-server-drop-an-user-in-all-databases-drop-the-login-too/](https://sqljana.wordpress.com/2017/01/12/sql-server-drop-an-user-in-all-databases-drop-the-login-too/)  
30. How to delete user logins and user defined schema's at once? \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/48466611/how-to-delete-user-logins-and-user-defined-schemas-at-once](https://stackoverflow.com/questions/48466611/how-to-delete-user-logins-and-user-defined-schemas-at-once)  
31. Use \`az ssh vm\` (AADSSHLoginForLinux) with Ansible \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/77897648/use-az-ssh-vm-aadsshloginforlinux-with-ansible](https://stackoverflow.com/questions/77897648/use-az-ssh-vm-aadsshloginforlinux-with-ansible)  
32. Enable Microsoft Entra authentication \- SQL Server on Azure VMs, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-sql/virtual-machines/windows/configure-azure-ad-authentication-for-sql-vm](https://docs.azure.cn/en-us/azure-sql/virtual-machines/windows/configure-azure-ad-authentication-for-sql-vm)  
33. Entra ID SQL authentication \- Nerdio Manager for Enterprise, accessed August 12, 2025, [https://nmehelp.getnerdio.com/hc/en-us/articles/26124311294733-Entra-ID-SQL-authentication](https://nmehelp.getnerdio.com/hc/en-us/articles/26124311294733-Entra-ID-SQL-authentication)  
34. Connecting to Azure SQL with Microsoft Entra ID in dbForge SQL Complete \- Documentation, accessed August 12, 2025, [https://docs.devart.com/sqlcomplete/getting-started/connecting-microsoft-entra-id-authentication-for-azure-sql-databases.html](https://docs.devart.com/sqlcomplete/getting-started/connecting-microsoft-entra-id-authentication-for-azure-sql-databases.html)  
35. Connect to Azure SQL Database with Microsoft Entra multifactor authentication, accessed August 12, 2025, [https://docs.azure.cn/en-us/azure-sql/database/active-directory-interactive-connect-azure-sql-db](https://docs.azure.cn/en-us/azure-sql/database/active-directory-interactive-connect-azure-sql-db)  
36. Configuring Neo4j Single Sign-On (SSO) \- Operations Manual, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/tutorial/tutorial-sso-configuration/](https://neo4j.com/docs/operations-manual/current/tutorial/tutorial-sso-configuration/)  
37. Neo4j Single Sign-on (SSO) Integration • SAML \- AuthDigital, accessed August 12, 2025, [https://authdigital.com/neo4j-single-sign-on](https://authdigital.com/neo4j-single-sign-on)  
38. Configure OIDC SSO for gallery and custom applications \- Microsoft Entra ID, accessed August 12, 2025, [https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/add-application-portal-setup-oidc-sso](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/add-application-portal-setup-oidc-sso)  
39. Authenticate User Access | milvus-operator, accessed August 12, 2025, [https://milvus-io.github.io/milvus-operator/docs/administration/security/enable-authentication.html](https://milvus-io.github.io/milvus-operator/docs/administration/security/enable-authentication.html)  
40. Authenticate User Access | Milvus Documentation, accessed August 12, 2025, [https://milvus.io/docs/authenticate.md](https://milvus.io/docs/authenticate.md)  
41. Tutorial: Create an Azure custom role with Azure CLI \- Azure RBAC ..., accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/role-based-access-control/tutorial-custom-role-cli](https://learn.microsoft.com/en-us/azure/role-based-access-control/tutorial-custom-role-cli)  
42. Azure custom roles \- Azure RBAC | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/azure/role-based-access-control/custom-roles](https://learn.microsoft.com/en-us/azure/role-based-access-control/custom-roles)  
43. az role definition | Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/cli/azure/role/definition?view=azure-cli-latest](https://learn.microsoft.com/en-us/cli/azure/role/definition?view=azure-cli-latest)  
44. Database-level roles \- SQL Server \- Microsoft Learn, accessed August 12, 2025, [https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/database-level-roles?view=sql-server-ver17](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/database-level-roles?view=sql-server-ver17)  
45. Check if a user exists in a SQL Server database \- DBA Stack Exchange, accessed August 12, 2025, [https://dba.stackexchange.com/questions/125886/check-if-a-user-exists-in-a-sql-server-database](https://dba.stackexchange.com/questions/125886/check-if-a-user-exists-in-a-sql-server-database)  
46. How can you create a login and a user but only if it doesn't exist? \- Stack Overflow, accessed August 12, 2025, [https://stackoverflow.com/questions/67808123/how-can-you-create-a-login-and-a-user-but-only-if-it-doesnt-exist](https://stackoverflow.com/questions/67808123/how-can-you-create-a-login-and-a-user-but-only-if-it-doesnt-exist)  
47. CREATE USER should have IF NOT EXISTS \- Support, accessed August 12, 2025, [https://productsupport.red-gate.com/hc/en-us/community/posts/24964559096989-CREATE-USER-should-have-IF-NOT-EXISTS](https://productsupport.red-gate.com/hc/en-us/community/posts/24964559096989-CREATE-USER-should-have-IF-NOT-EXISTS)  
48. Property-based access control \- Operations Manual \- Neo4j, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/authentication-authorization/property-based-access-control/](https://neo4j.com/docs/operations-manual/current/authentication-authorization/property-based-access-control/)  
49. Role-based access control \- Operations Manual \- Neo4j, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/authentication-authorization/manage-privileges/](https://neo4j.com/docs/operations-manual/current/authentication-authorization/manage-privileges/)  
50. Manage roles \- Operations Manual \- Neo4j, accessed August 12, 2025, [https://neo4j.com/docs/operations-manual/current/authentication-authorization/manage-roles/](https://neo4j.com/docs/operations-manual/current/authentication-authorization/manage-roles/)  
51. User and role management \- Cypher Manual \- Neo4j, accessed August 12, 2025, [https://neo4j.com/docs/cypher-manual/4.1/administration/security/users-and-roles/](https://neo4j.com/docs/cypher-manual/4.1/administration/security/users-and-roles/)  
52. RBAC Explained | Milvus Documentation, accessed August 12, 2025, [https://milvus.io/docs/rbac.md](https://milvus.io/docs/rbac.md)  
53. SLES 12 SP5 | Security and Hardening Guide | Setting Up the Linux Audit Framework, accessed August 12, 2025, [https://documentation.suse.com/en-us/sles/12-SP5/html/SLES-all/cha-audit-setup.html](https://documentation.suse.com/en-us/sles/12-SP5/html/SLES-all/cha-audit-setup.html)