var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent);
var db = sql.AddDatabase("Default", "ClanHub");

builder.AddContainer("mailpit", "axllent/mailpit")
    .WithHttpEndpoint(targetPort: 8025, name: "ui")
    .WithEndpoint(targetPort: 1025, name: "smtp");

builder.AddProject<Projects.ClanHub_Server>("server")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("Email__Host", "localhost")
    .WithEnvironment("Email__Port", "1025")
    .WithExternalHttpEndpoints();

builder.Build().Run();
