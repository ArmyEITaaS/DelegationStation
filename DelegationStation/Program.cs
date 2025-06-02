using DelegationStation.Authorization;
using DelegationStation.Interfaces;
using DelegationStation.Services;
using DelegationStationShared;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// Setup Ilogger for startup errors
var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.SetMinimumLevel(LogLevel.Debug);
    loggingBuilder.AddConsole();
});
ILogger logger = loggerFactory.CreateLogger<Program>();


// Check for required config values
string defaultAdminGroup = builder.Configuration["DefaultAdminGroupObjectId"] ?? "";
if (string.IsNullOrEmpty(defaultAdminGroup))
{
    logger.LogError("Exiting. DefaultAdminGroupObjectId is not set. Please set it to the object ID of the group that should have admin access.");
    Environment.Exit(1);
}
else
{
    var match = Regex.Match(defaultAdminGroup, DSConstants.GUID_REGEX, RegexOptions.IgnoreCase);
    if (!match.Success)
    {
        logger.LogError("DefaultAdminGroupObjectId is not a valid GUID. Please set it to the object ID of the group that should have admin access.");
        Environment.Exit(1);
    }
}


//Add services to the container.
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();
builder.Services.AddControllers(config =>
{
    var policy = new AuthorizationPolicyBuilder()
                     .RequireAuthenticatedUser()
                     .Build();
    config.Filters.Add(new AuthorizeFilter(policy));
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TagView", policy =>
        policy.Requirements.Add(DeviceTagOperations.Read));
    options.AddPolicy("TagUpdate", policy =>
        policy.Requirements.Add(DeviceTagOperations.Update));
    options.AddPolicy("TagUpdateActions", policy =>
        policy.Requirements.Add(DeviceTagOperations.UpdateActions));
    options.AddPolicy("TagUpdateActionSecurityGroups", policy =>
        policy.Requirements.Add(DeviceTagOperations.UpdateSecurityGroups));
    options.AddPolicy("TagUpdateActionAttributes", policy =>
        policy.Requirements.Add(DeviceTagOperations.UpdateAttributes));
    options.AddPolicy("TagUpdateActionAdministrativeUnits", policy =>
        policy.Requirements.Add(DeviceTagOperations.UpdateAdministrativeUnits));
    options.AddPolicy("DelegationStationAdmin", policy =>
        policy.RequireRole(defaultAdminGroup));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddMicrosoftIdentityConsentHandler();

builder.Services.AddSingleton<IAuthorizationHandler, DeviceTagAuthorizationHandler>();
builder.Services.AddSingleton<IDeviceTagDBService, DeviceTagDBService>();
builder.Services.AddSingleton<IDeviceDBService, DeviceDBService>();
builder.Services.AddSingleton<IGraphService, GraphService>();
builder.Services.AddSingleton<IRoleDBService, RoleDBService>();

builder.Services.AddApplicationInsightsTelemetry(opt => opt.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
