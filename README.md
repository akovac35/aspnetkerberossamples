# ASP.NET Kerberos Samples

This project contains samples for configuring Windows SSO with Linux ASP.NET 8+ application using managed Kerberos implementation based on `Kerberos.NET`.

## Infrastructure Setup

The setup configures an Active Directory Domain Services (AD DS) domain on a Windows Server 2019 virtual machine (VM), joining a Windows 11 VM to that domain, and setting up a Linux VM to host a Kerberos-authenticated application with Single Sign-On (SSO) using Microsoft Edge.

### **Prerequisites**
1. **VM Setup**:
   - **Windows Server 2019 VM**: Installed with a static IP (e.g., `192.168.1.10`), hostname (e.g., `adfs-server`), and fully updated.
   - **Windows 11 VM**: Installed with a static IP (e.g., `192.168.1.20`), hostname (e.g., `win-client`), and fully updated.
   - **Linux VM**: Installed (e.g., Oracle Linux 9.5 or later) with a static IP (e.g., `192.168.1.11`), hostname (e.g., `linux-server`). The app will be hosted here.
   - All VMs are on the same network and can communicate (e.g., via a virtual LAN in your hypervisor).
2. **DNS**: The Windows Server 2019 VM will act as the DNS server for the network domain.
3. **Domain Name**: Choose a domain name, e.g., `example.local`.
5. **App**: The Linux VM will host a .NET app configured for Kerberos authentication.

### **Step 1: Configure Active Directory Domain Services (AD DS) on Windows Server 2019**
1. **Install AD DS Role**:
   - Open **Server Manager** → **Add roles and features**.
   - Select **Role-based or feature-based installation**, then choose the server.
   - Check **Active Directory Domain Services** → Install.
   - After installation, click the notification flag in Server Manager and select **Promote this server to a domain controller**.

2. **Promote to Domain Controller**:
   - Choose **Add a new forest**.
   - Enter the domain name (e.g., `example.local`).
   - Set the forest and domain functional levels to **Windows Server 2016** or higher.
   - Enable **DNS server** (checked by default).
   - Set a Directory Services Restore Mode (DSRM) password.
   - Complete the wizard and reboot the server.

3. **Verify DNS**:
   - Open **DNS Manager** (`dnsmgmt.msc`).
   - Ensure the domain `example.local` has an **A record** for `adfs-server.example.local` pointing to `192.168.1.10`.
   - Add an **A record** for `linux-server.example.local` pointing to `192.168.1.11`.
   - Add a **CNAME record** for the AD FS service, e.g., `adfs.example.local` → `adfs-server.example.local`.

4. **Create Service Account for App**:
   - Open **Active Directory Users and Computers** (`dsa.msc`).
   - Create a service account, e.g., `svc-app` under `Users`.
   - Set a strong password and enable **Password never expires**.

### **Step 2: Join Windows 11 VM to the Domain**
1. **Configure DNS on Windows 11**:
   - Set the primary DNS server to `192.168.1.10` (the Windows Server 2019 IP).
   - Verify connectivity: `ping adfs-server.example.local`.

2. **Join the Domain**:
   - Open **System Properties** (`sysdm.cpl`).
   - Under **Computer Name**, click **Change** → Select **Domain** → Enter `example.local`.
   - Authenticate with a domain admin account (e.g., `Administrator@example.local`).
   - Reboot the Windows 11 VM.

3. **Log in to Windows 11**:
   - Log in with a domain user account (e.g., `user1@example.local`).
   - Create this user in Windows Server **Active Directory Users and Computers** if needed.

4. **Configure Edge for Kerberos**:
   - Open Microsoft Edge on the Windows 11 VM.
   - Go to **Settings** → **System and performance** → Ensure **Use single sign-on** is enabled.
   - **Configure Internet Options for automatic authentication**:
     - Press `Windows + R`, type `inetcpl.cpl`, press Enter.
     - Go to **Security** tab → Select **Local intranet** → Click **Sites**.
     - Click **Advanced** → Add `https://linux-server.example.local`.
     - Click **OK** to close all dialogs.
   - Restart Edge.

### **Step 3: Configure App for Kerberos Authentication**
1. **Create Service Principal and Keytab**:
   - On the Windows Server 2019, open a PowerShell prompt as Administrator.
   - Create an SPN for the app:
     ```powershell
     setspn -S HTTP/linux-server.example.local@EXAMPLE.LOCAL svc-app
     ```
   - Generate a keytab file:
     ```powershell
     ktpass -princ HTTP/linux-server.example.local@EXAMPLE.LOCAL -mapuser svc-app@EXAMPLE.LOCAL -crypto ALL -ptype KRB5_NT_PRINCIPAL -pass <svc-app-password> -out c:\tmp\linux-server.keytab
     ```
   - Copy `linux-server.keytab` to the Linux VM.

2. **Update DNS on Linux VM**:
   - Manually edit connection config and use a static IP `192.168.1.11` and DNS `192.168.1.10` (Windows Server).
   - Verify DNS resolution: `ping adfs-server.example.local`.

### **Step 4: Test SSO from Windows 11**
1. **Access the App**:
   - On the Windows 11 VM, log in as `user1@example.local`.
   - Open Microsoft Edge and navigate to `https://linux-server.example.local:5001/secure`.
   - If configured correctly, you should be authenticated via Kerberos.
   - If authentication works, but not SSO, then confirm the site is registered under **Local intranet** in the AD domain computers.
   - Validate system clocks for involved servers in case of authentication problems.
