# ASP.NET Kerberos Samples

This project contains samples for configuring Windows SSO with Linux ASP.NET 8+ application using managed `Kerberos.NET` implementation.

Windows Single Sign-On (SSO) enables users to authenticate once and access multiple services without re-entering credentials. This functionality relies on various authentication protocols—NTLM, Kerberos, MS-KILE, and SPNEGO—and their relationships.

Managed Kerberos implementation facilitates integration without requiring the application to rely on the host OS configuration.

## Windows Authentication Protocols

This chapter provides an overview of the key authentication protocols used in Windows environments, particularly in Active Directory (AD) domains, and their relationships.

### SPNEGO (Simple and Protected GSSAPI Negotiation Mechanism)
SPNEGO is a protocol that enables clients and servers to negotiate the authentication mechanism to use, typically choosing between Kerberos and NTLM. In Windows domains, SPNEGO prioritizes Kerberos for its security and SSO capabilities but falls back to NTLM when Kerberos is not feasible. SPNEGO operates through the Generic Security Service Application Program Interface (GSSAPI), providing a standardized way to handle authentication across different protocols. In the context of HTTP authentication, as specified in RFC 4559, SPNEGO is used with the 'Negotiate' auth-scheme.

### NTLM (NT LAN Manager)
NTLM is a proprietary Microsoft security protocol suite that provides authentication, integrity, and confidentiality. It is used in Windows environments, particularly as a fallback when Kerberos is unavailable.

### Kerberos
Kerberos is a standardized network authentication protocol that uses secret-key cryptography to provide strong authentication for client-server applications. In Windows domains, Kerberos is the default authentication mechanism, enabling SSO by issuing a Ticket Granting Ticket (TGT) upon user login. The TGT is used to obtain service tickets for accessing resources without re-authentication, making it efficient and secure for enterprise environments.

### MS-KILE (Microsoft Kerberos Protocol Extensions)
MS-KILE is Microsoft’s implementation of the Kerberos protocol, adhering to the standard but incorporating extensions to enhance functionality within Windows environments. A key extension is the Privilege Attribute Certificate (PAC), which embeds authorization data, such as user group memberships and security identifiers (SIDs), directly into Kerberos tickets. This allows services to perform authorization without querying the Active Directory separately, improving efficiency.

### Relationships Between Protocols
In Windows Active Directory environments, Kerberos (via MS-KILE) is the primary authentication protocol due to its robust security and SSO support. NTLM serves as a fallback for scenarios where Kerberos cannot be used, such as when a client is not domain-joined or cannot reach the KDC. SPNEGO facilitates seamless authentication by negotiating the protocol, ensuring that Kerberos is used whenever possible. MS-KILE extends standard Kerberos with features like the PAC, which integrates authorization data into the authentication process, making it a cornerstone of Windows domain security.

The following table summarizes the roles and relationships of these protocols:

| **Protocol** | **Role** | **Relationship** |
|--------------|----------|------------------|
| NTLM         | Authentication, SSO | Used by SPNEGO as a fallback when Kerberos is unavailable |
| Kerberos     | Authentication, SSO | Primary protocol negotiated by SPNEGO |
| MS-KILE      | Microsoft’s Kerberos implementation with extensions | Extends standard Kerberos, used in Windows domains |
| SPNEGO       | Authentication protocol negotiation | Negotiates between NTLM and Kerberos/MS-KILE |

## Project Structure

- `Program.cs` - Main application with Kerberos authentication setup
- `KerberosAuthHandler.cs` - Custom authentication handler for Kerberos
- `appsettings.json` - Configuration including Kerberos settings and HTTPS certificate
- `create-cert.sh` - Script to generate self-signed SSL certificates
- `copy-to-server.sh` - Deployment script for building and copying application files to a Linux host

## Sample Infrastructure Setup

A Windows infrastructure is required to test the samples. One can be configured by following the instructions in this chapter.

The setup configures an Active Directory Domain Services (AD DS) domain on a Windows Server 2019 virtual machine (VM), joining a Windows 11 VM to that domain, and setting up a Linux VM to host a Kerberos-authenticated application with Single Sign-On (SSO) using Microsoft Edge.

### **Prerequisites**
1. **VM Setup**:
   - **Windows Server 2019 VM**: Installed with a static IP (e.g., `192.168.1.10`), hostname (e.g., `adfs-server`), and fully updated.
   - **Windows 11 VM**: Installed with a static IP (e.g., `192.168.1.20`), hostname (e.g., `win-client`), and fully updated.
   - **Linux VM**: Installed (e.g., Oracle Linux 9.5 or later) with a static IP (e.g., `192.168.1.11`), hostname (e.g., `linux-server`). The app will be hosted here.
   - All VMs are on the same network and can communicate (e.g., via a virtual LAN in your hypervisor).
2. **DNS**: The Windows Server 2019 VM will act as the DNS server for the network domain.
3. **Domain Name**: Choose a domain name, e.g., `example.local`.
4. **Development Tools**: .NET 8 SDK, bash, OpenSSL (for certificate generation), and SCP client for deployment.
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
   - Start secpol.msc and add `EXAMPLE\Domain Users` under `Security Settings\Local Policies\User Rights Assignment\Allow log on through Remote Desktop Services`.
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
   - Publish the app to the Linux VM using script `copy-to-server.sh`.
   - Start the app: `./kerberos_sample`
   - On the Windows 11 VM, log in as AD domain user `user1@example.local`.
   - Open Microsoft Edge and navigate to `https://linux-server.example.local:5001/login`.
   - If configured correctly, you should be authenticated via Kerberos SSO.
   - If authentication works, but not SSO, then confirm the site is registered under **Local intranet** in the AD domain computers.

2. **Test Endpoints**:
   - `/public` - Public endpoint, no authentication is required
   - `/login` - Login endpoint on which Kerberos SSO should be applied
   - `/secure` - Protected endpoint requiring authenticated status
   - `/auth-status` - Shows current authentication status and user claims
   - `/logout` - Logout endpoint

Windows Kerberos implementation is called `MS-KILE` and contains some extensions to the standard Kerberos protocol, among which is the inclusion of user claims (including group memberships) in the Kerberos ticket. The following is an example of claims that can be expected in a Windows Kerberos ticket:

```json
[
	{
		"Type": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/sid",
		"Value": "S-1-5-21-1508193582-2086494461-801676821-1107"
	},
	{
		"Type": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname",
		"Value": "Test user 1"
	},
	{
		"Type": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
		"Value": "user1@example.local"
	},
	{
		"Type": "http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid",
		"Value": "S-1-5-21-1508193582-2086494461-801676821-513"
	},
	{
		"Type": "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
		"Value": "Domain Users"
	},
	{
		"Type": "http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid",
		"Value": "S-1-18-1"
	}
]
```

## Troubleshooting

### **Common Issues**

1. **"Failed to load keytab file"**:
   - Ensure the keytab file exists and has correct permissions
   - Verify the file path in `appsettings.json`

2. **"Authentication failed"**:
   - Check system clocks are synchronized between all servers
   - Verify SPN is correctly registered: `setspn -L svc-app`

3. **SSL Certificate Issues**:
   - Run `./create-cert.sh` to regenerate certificates
   - Import the certificate to Windows client's trusted store if needed

4. **SSO Not Working**:
   - Verify the site is added to Local Intranet zone in Internet Options
   - Check that Edge is configured for automatic authentication
   - Ensure user is logged in with domain credentials

5. **Connection Refused**:
   - Check firewall settings on Linux VM (ports 5000/5001)
   - Verify application is running: `netstat -tlnp | grep 5001`
   - Ensure DNS resolution works from all machines

## References

- [Kerberos.NET Documentation](https://github.com/dotnet/Kerberos.NET)
- [ASP.NET Core Authentication](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/)
- [Windows Server Active Directory Domain Services](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/get-started/virtual-dc/active-directory-domain-services-overview)
- [SPNEGO-based Kerberos and NTLM HTTP Authentication in Microsoft Windows](https://www.rfc-editor.org/info/rfc4559)
- [MS-SPNG](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-spng)
- [MS-KILE Protocol](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-kile/)
