// Data-plane host shell. It adds no endpoints and registers no gateway services yet. ISK-0007 adds its behavior.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
