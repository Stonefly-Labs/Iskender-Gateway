// Composition host shell. ISK-0000 deliberately maps no endpoints and registers no gateway services.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
