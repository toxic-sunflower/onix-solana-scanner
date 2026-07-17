var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres")
    .WithImageTag("16-alpine")
    .WithEnvironment("POSTGRES_DB", "onix_scanner");

var database = postgres.AddDatabase("Default", "onix_scanner");

builder.AddProject<Projects.Onix_Scanner_Api>("api")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
