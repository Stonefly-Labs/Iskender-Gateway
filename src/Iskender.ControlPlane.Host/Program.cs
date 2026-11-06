// Control-plane host shell. ISK-0001 adds no endpoints and registers no gateway services. ISK-0007 adds its behavior.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
