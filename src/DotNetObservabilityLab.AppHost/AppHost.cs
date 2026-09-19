using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true)
    .WithDescription("Administrator password persisted with the local PostgreSQL data volume.");

var ingestionDatabasePassword = builder.AddParameter("ingestion-db-password", secret: true)
    .WithDescription("Password for the ingestion boundary PostgreSQL role.");

var consolidationDatabasePassword = builder.AddParameter("consolidation-db-password", secret: true)
    .WithDescription("Password for the consolidation boundary PostgreSQL role.");

var postgresInitDirectory = Path.Combine(builder.AppHostDirectory, "postgres-init");

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithEnvironment("INGESTION_DB_PASSWORD", ingestionDatabasePassword)
    .WithEnvironment("CONSOLIDATION_DB_PASSWORD", consolidationDatabasePassword)
    .WithInitFiles(postgresInitDirectory)
    .WithDataVolume();

var ingestionDatabase = postgres
    .AddDatabase("ingestion-db", "ingestion_db")
    .WithCreationScript("""
        CREATE DATABASE "ingestion_db" OWNER "ingestion_app";
        REVOKE CONNECT ON DATABASE "ingestion_db" FROM PUBLIC;
        GRANT CONNECT ON DATABASE "ingestion_db" TO "ingestion_app";
        """);

var consolidationDatabase = postgres
    .AddDatabase("consolidation-db", "consolidation_db")
    .WithCreationScript("""
        CREATE DATABASE "consolidation_db" OWNER "consolidation_app";
        REVOKE CONNECT ON DATABASE "consolidation_db" FROM PUBLIC;
        GRANT CONNECT ON DATABASE "consolidation_db" TO "consolidation_app";
        """);

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