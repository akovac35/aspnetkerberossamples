#!/bin/bash

# Set environment variable to disable path conversion on Windows Git Bash
export MSYS_NO_PATHCONV=1

# Generate a private key
openssl genrsa -out linux-server.key 2048

# Create a configuration file for the certificate with DNS name
cat > linux-server.conf << EOF
[req]
distinguished_name = req_distinguished_name
req_extensions = v3_req
prompt = no

[req_distinguished_name]
CN = linux-server.example.local

[v3_req]
subjectAltName = @alt_names

[alt_names]
DNS.1 = linux-server.example.local
EOF

# Create a self-signed certificate with proper DNS name
openssl req -new -x509 -key linux-server.key -out linux-server.crt -days 365 -config linux-server.conf -extensions v3_req

# Convert to PFX format with password "test"
openssl pkcs12 -export -out ./linux-server.pfx -inkey linux-server.key -in linux-server.crt -password pass:test

# Clean up temporary files
rm linux-server.key linux-server.crt linux-server.conf