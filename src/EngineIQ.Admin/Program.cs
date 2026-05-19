using System.Text;
using EngineIQ.Admin;
using EngineIQ.Admin.Middleware;
using EngineIQ.Admin.Options;
using EngineIQ.Admin.Services;
using EngineIQ.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AdminAuthOptions>(builder.Configuration.GetSection(AdminAuthOptions.SectionName));
builder.Services.AddEngineIQPersistence(builder.Configuration);
builder.Services.AddRabbitMqJobPublisher(builder.Configuration);

builder.Services.AddSingleton<AdminPortalService>();
builder.Services.AddSingleton<DlqRetryService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(
                """{"error":"internal_error","detail":"See server logs."}""",
                Encoding.UTF8);
        });
    });
}

app.UseMiddleware<BasicAuthMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

app.MapAdminApi();
app.MapGet("/", () => Results.Redirect("/admin/"));
app.MapGet("/admin", () => Results.Redirect("/admin/"));
app.MapFallbackToFile("/admin/{**slug}", "admin/index.html");

app.Run();
