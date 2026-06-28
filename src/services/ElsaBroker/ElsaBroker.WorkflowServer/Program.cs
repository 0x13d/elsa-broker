using Elsa.Extensions;
using ElsaBroker.WorkflowServer;

var builder = WebApplication.CreateBuilder(args);

// Elsa 3 server — standard modules, intentionally WITHOUT MassTransit so the
// in-process dispatcher resumes DispatchWorkflow(WaitForCompletion) correctly
// (the bundled demo image's MassTransit dispatch breaks that resume).
builder.Services.AddElsa(elsa =>
{
    elsa.UseIdentity(identity =>
    {
        identity.TokenOptions = options =>
            options.SigningKey = builder.Configuration["Elsa:Identity:SigningKey"]
                ?? "dev-signing-key-change-me-please-0123456789abcdef";
        identity.UseAdminUserProvider();
    });
    elsa.UseDefaultAuthentication();

    elsa.UseWorkflowManagement();
    elsa.UseWorkflowRuntime();
    elsa.UseJavaScript();
    elsa.UseLiquid();
    elsa.UseHttp();
    elsa.UseWorkflowsApi();
});

// Elsa Studio runs on a different origin, so allow CORS to the API.
builder.Services.AddCors(cors => cors.AddDefaultPolicy(p => p
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("*")));

// Seed published workflow definitions from the shared Workflows/ folder.
builder.Services.AddHostedService<WorkflowFolderImporter>();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.UseWorkflowsApi();   // Elsa management/runtime REST API (Studio talks to this)
app.UseWorkflows();      // HTTP workflow endpoints (HttpEndpoint activities)

app.Run();
