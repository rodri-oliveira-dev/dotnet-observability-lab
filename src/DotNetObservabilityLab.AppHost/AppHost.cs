var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Ingestion_Api>("ingestion-api")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Ingestion_Outbox_Worker>("ingestion-outbox-worker");

builder.AddProject<Projects.Consolidation_Api>("consolidation-api")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Consolidation_Worker>("consolidation-worker");

builder.Build().Run();
