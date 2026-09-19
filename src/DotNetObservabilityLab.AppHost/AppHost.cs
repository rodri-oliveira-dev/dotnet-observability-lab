var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var ingestionDatabase = postgres.AddDatabase("ingestion-db", "ingestion_db");
var consolidationDatabase = postgres.AddDatabase("consolidation-db", "consolidation_db");

var redis = builder.AddRedis("redis");

builder.AddProject<Projects.Ingestion_Api>("ingestion-api")
    .WithReference(ingestionDatabase)
    .WithReference(redis)
    .WaitFor(ingestionDatabase)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Ingestion_Outbox_Worker>("ingestion-outbox-worker")
    .WithReference(ingestionDatabase)
    .WaitFor(ingestionDatabase);

builder.AddProject<Projects.Consolidation_Api>("consolidation-api")
    .WithReference(consolidationDatabase)
    .WaitFor(consolidationDatabase)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Consolidation_Worker>("consolidation-worker")
    .WithReference(consolidationDatabase)
    .WithReference(redis)
    .WaitFor(consolidationDatabase);

builder.Build().Run();
