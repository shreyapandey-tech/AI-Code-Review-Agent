using AICodeReviewAgent.Configuration;
using AICodeReviewAgent.Services;
using Microsoft.Extensions.FileProviders;

// ─────────────────────────────────────────────────────────────
//  Locate wwwroot (handles solution root, project dir, or bin dir)
// ─────────────────────────────────────────────────────────────
var currentDir = Directory.GetCurrentDirectory();
var webRoot = Directory.Exists(Path.Combine(currentDir, "wwwroot"))
    ? Path.Combine(currentDir, "wwwroot")
    : Directory.Exists(Path.Combine(currentDir, "AICodeReviewAgent", "wwwroot"))
        ? Path.Combine(currentDir, "AICodeReviewAgent", "wwwroot")
        : Path.Combine(AppContext.BaseDirectory, "wwwroot");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Directory.Exists(webRoot) ? webRoot : null
});

// Bind the "NvidiaAI" section from appsettings.json / User Secrets / env-vars.
builder.Services.Configure<NvidiaAiSettings>(
    builder.Configuration.GetSection("NvidiaAI"));

// Register HttpClient for the NVIDIA NIM API.
// AddHttpClient<T> creates a named, typed client and handles lifetime properly –
// avoids the "socket exhaustion" problem you get if you new up HttpClient yourself.
builder.Services.AddHttpClient<LlmService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
});

// The agent service uses the LLM service and is scoped to each HTTP request.
builder.Services.AddScoped<AgentService>();

// Add controllers (they scan this assembly automatically).
builder.Services.AddControllers();

// ─────────────────────────────────────────────────────────────
//  Middleware pipeline
// ─────────────────────────────────────────────────────────────

var app = builder.Build();

// Always serve static files (index.html, CSS, JS) from wwwroot.
if (Directory.Exists(webRoot))
{
    var fileProvider = new PhysicalFileProvider(webRoot);
    var defaultFilesOptions = new DefaultFilesOptions { FileProvider = fileProvider };
    defaultFilesOptions.DefaultFileNames.Clear();
    defaultFilesOptions.DefaultFileNames.Add("index.html");
    app.UseDefaultFiles(defaultFilesOptions);

    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}
else
{
    app.UseDefaultFiles();   // rewrites "/" → "/index.html"
    app.UseStaticFiles();    // serves everything under wwwroot/
}

// In development show friendly error pages; in production keep error messages terse.
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

app.MapControllers();

app.Run();
