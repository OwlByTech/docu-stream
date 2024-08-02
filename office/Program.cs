using office.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(3000);
});

var app = builder.Build();

app.MapGrpcService<WordService>();
app.Run();


