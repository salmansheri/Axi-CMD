# Restrict exported AxiApi telemetry data

AxiApi handles tenant-specific command and user data, and existing logs include user identifiers and cache keys. Exported telemetry will exclude request and response bodies, SQL values, credentials, usernames, and raw command text by default, favoring route templates and operation names over unrestricted diagnostic payloads. The first milestone also omits tenant identifiers, including `appname`; this sacrifices tenant filtering and some debugging detail to avoid retaining sensitive application data in an additional system.
