using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true)
    .WithDescription("Administrator password persisted with the local PostgreSQL data volume.");

var ingestionDatabasePassword = builder.AddParameter("ingestion-db-password", secret: true)
    .WithDescription("Password for the ingestion boundary PostgreSQL role.");

var consolidationDatabasePassword = builder.AddParameter("consolidation-db-password", secret: true)
    .WithDescription("Password for the consolidation boundary PostgreSQL role.");

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithEnvironment("INGESTION_DB_PASSWORD", ingestionDatabasePassword)
    .WithEnvironment("CONSOLIDATION_DB_PASSWORD", consolidationDatabasePassword)
    .WithInitFiles("postgres-init")
    .WithDataVolume();

// The PostgreSQL bootstrap script creates these databases with their boundary-specific owners.
// Aspire registers them here for readiness checks and resource discovery.
var ingestionDatabase = postgres.AddDatabase("ingestion-db", "ingestion_db")
    .WithCreationScript("SELECT 1;");

var consolidationDatabase = postgres.AddDatabase("consolidation-db", "consolidation_db")
    .WithCreationScript("SELECT 1;");

var postgresEndpoint = postgres.Resource.PrimaryEndpoint;

var ingestionConnectionString = ReferenceExpression.Create(
    $"Host={postgresEndpoint.Property(EndpointProperty.Host)};Port={postgresEndpoint.Property(EndpointProperty.Port)};Database=ingestion_db;Username=ingestion_app;Password={ingestionDatabasePassword}");

var consolidationConnectionString = ReferenceExpression.Create(
    $"Host={postgresEndpoint.Property(EndpointProperty.Host)};Port={postgresEndpoint.Property(EndpointProperty.Port)};Database=consolidation_db;Username=consolidation_app;Password={consolidationDatabasePassword}");

var redis = builder.AddRedis("redis");

builder.AddProject<Projects.Ingestion_Api>("ingestion-api")
    .WithEnvironment("ConnectionStrings__ingestion-db", ingestionConnectionString)
    .WithReference(redis)
    .WaitFor(ingestionDatabase)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Ingestion_Outbox_Worker>("ingestion-outbox-worker")
    .WithEnvironment("ConnectionStrings__ingestion-db", ingestionConnectionString)
    .WaitFor(ingestionDatabase);

builder.AddProject<Projects.Consolidation_Api>("consolidation-api")
    .WithEnvironment("ConnectionStrings__consolidation-db", consolidationConnectionString)
    .WaitFor(consolidationDatabase)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Consolidation_Worker>("consolidation-worker")
    .WithEnvironment("ConnectionStrings__consolidation-db", consolidationConnectionString)
    .WithReference(redis)
    .WaitFor(consolidationDatabase);

builder.Build().Run();