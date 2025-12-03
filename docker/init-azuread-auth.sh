#!/bin/bash
set -e

# Configure PostgreSQL for Azure AD authentication
# Supports managed identity and service principal tokens

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    -- Create Azure AD authentication function
    CREATE OR REPLACE FUNCTION verify_azure_ad_token(token text)
    RETURNS TABLE(sub text, oid text, tid text, app_id text, roles text[])
    LANGUAGE plpython3u
    AS \$\$
        import json
        import base64

        # Simple JWT parser (production should validate signature)
        parts = token.split('.')
        if len(parts) != 3:
            return []

        # Decode payload (add padding if needed)
        payload = parts[1]
        payload += '=' * (4 - len(payload) % 4)

        try:
            decoded = base64.urlsafe_b64decode(payload)
            claims = json.loads(decoded)

            return [(
                claims.get('sub', ''),
                claims.get('oid', ''),
                claims.get('tid', ''),
                claims.get('appid', ''),
                claims.get('roles', [])
            )]
        except Exception as e:
            plpy.warning(f"Token parse error: {e}")
            return []
    \$\$;

    -- Create roles for different access levels
    DO \$\$
    BEGIN
        IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'hartonomous_reader') THEN
            CREATE ROLE hartonomous_reader;
        END IF;

        IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'hartonomous_writer') THEN
            CREATE ROLE hartonomous_writer;
        END IF;

        IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'hartonomous_admin') THEN
            CREATE ROLE hartonomous_admin;
        END IF;
    END
    \$\$;

    -- Grant read-only permissions
    GRANT CONNECT ON DATABASE $POSTGRES_DB TO hartonomous_reader;
    GRANT USAGE ON SCHEMA public TO hartonomous_reader;
    GRANT SELECT ON ALL TABLES IN SCHEMA public TO hartonomous_reader;
    ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO hartonomous_reader;

    -- Grant read-write permissions
    GRANT hartonomous_reader TO hartonomous_writer;
    GRANT INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hartonomous_writer;
    ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT INSERT, UPDATE, DELETE ON TABLES TO hartonomous_writer;

    -- Grant admin permissions
    GRANT hartonomous_writer TO hartonomous_admin;
    GRANT CREATE ON SCHEMA public TO hartonomous_admin;

    -- Enable pgaudit for security logging
    CREATE EXTENSION IF NOT EXISTS pgaudit;
    ALTER SYSTEM SET pgaudit.log = 'all';
    ALTER SYSTEM SET pgaudit.log_catalog = 'off';

EOSQL

echo "Azure AD authentication configured for $POSTGRES_DB"
