using MuniClaw.Core.Contracts.Integrations;
using MuniClaw.Core.Data;
using MuniClaw.Core.Integrations;
using MuniClaw.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Register Core Store & Services
builder.Services.AddSingleton<IMuniClawStore, InMemoryMuniClawStore>();
builder.Services.AddScoped<IOrganizationAuthorizationService, OrganizationAuthorizationService>();
builder.Services.AddScoped<ITaskLifecycleService, TaskLifecycleService>();
builder.Services.AddScoped<IWorkerDispatchService, WorkerDispatchService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();
builder.Services.AddScoped<IDeliveryService, DeliveryService>();
builder.Services.AddScoped<IProviderCredentialService, ProviderCredentialService>();
builder.Services.AddScoped<IWorkspaceProvisioningService, WorkspaceProvisioningService>();
builder.Services.AddScoped<IMinicloudInfrastructureClient, MockMinicloudInfrastructureClient>();
builder.Services.AddScoped<IGitProviderClient, MockGitProviderClient>();

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin();
    });
});

var app = builder.Build();

app.UseCors();
app.UseRouting();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "MuniClaw.Api" }));

app.Run();

public partial class Program { }
