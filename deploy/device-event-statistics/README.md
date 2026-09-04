# Device Event Statistics SQL deployment

Apply the SQL migrations with a deployment identity before enabling the
Statistics Worker.

    .\Apply-SqlMigrations.ps1 -ConnectionString $env:DEVICE_EVENT_STATISTICS_SQL_CONNECTION_STRING -DatabaseName device_event_statistics -SchemaName device_stats

The script validates the resolved database, applies migrations in filename
order, stores a SHA-256 checksum in SchemaMigration, and refuses to continue
when an already-applied migration was changed. It does not print the
connection string or credentials.

The runtime identity only verifies the schema and latest migration; it does
not create or alter SQL objects.
