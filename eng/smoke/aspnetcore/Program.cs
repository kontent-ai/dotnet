// A real host, not just a compile: builds the app, registers the package's DI extension, wires its
// middleware, then starts and stops. Catches a package that compiles but fails to boot.
using Kontent.Ai.AspNetCore.RichText;
using Kontent.Ai.AspNetCore.Webhooks;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddKontentRichText();

var app = builder.Build();
app.UseWebhookSignatureValidator(_ => false, new WebhookOptions { Secret = "smoke" });
app.MapGet("/", () => "ok");

await app.StartAsync();
Console.WriteLine("aspnetcore smoke: host started with rich text + webhook validator");
await app.StopAsync();
