FROM postgis/postgis:16-3.4

# Install Azure AD authentication extension dependencies
RUN apt-get update && apt-get install -y \
    postgresql-16-pgaudit \
    && rm -rf /var/lib/apt/lists/*

# Copy Azure AD authentication setup script
COPY docker/init-azuread-auth.sh /docker-entrypoint-initdb.d/03-azuread-auth.sh
RUN chmod +x /docker-entrypoint-initdb.d/03-azuread-auth.sh
