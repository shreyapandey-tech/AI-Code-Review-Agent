using AICodeReviewAgent.Models;
using AICodeReviewAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace AICodeReviewAgent.Controllers;

/// <summary>
/// Handles HTTP requests for the code-review workflow.
///
/// Routes:
///   POST /api/review         – JSON body { "diffContent": "...", "repositoryContext": "..." }
///   POST /api/review/upload  – multipart form upload of a .patch / .diff file
///   GET  /api/review/sample  – returns a sample Git diff for testing
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReviewController : ControllerBase
{
    private readonly AgentService _agent;
    private readonly ILogger<ReviewController> _logger;

    // Maximum diff size we accept (1 MB).
    // Protects against accidental or malicious giant payloads.
    private const int MaxDiffSizeBytes = 1_048_576;

    public ReviewController(AgentService agent, ILogger<ReviewController> logger)
    {
        _agent  = agent;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    //  POST /api/review  (JSON body)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts a JSON body with the diff text and runs an AI code review.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Review(
        [FromBody] ReviewRequest request,
        CancellationToken cancellationToken)
    {
        // Basic validation.
        if (string.IsNullOrWhiteSpace(request.DiffContent))
            return BadRequest(new { error = "DiffContent must not be empty." });

        if (System.Text.Encoding.UTF8.GetByteCount(request.DiffContent) > MaxDiffSizeBytes)
            return BadRequest(new { error = "Diff is too large (max 1 MB)." });

        try
        {
            _logger.LogInformation("Starting code review for a diff with {Chars} characters.",
                request.DiffContent.Length);

            var result = await _agent.RunReviewAsync(request, cancellationToken);

            _logger.LogInformation(
                "Review completed. {FindingCount} finding(s), {ToolCalls} tool call(s).",
                result.Findings.Count, result.ToolCallsExecuted);

            return Ok(result);
        }
        catch (HttpRequestException ex)
        {
            // The LLM API returned an error (e.g. invalid key, rate limit).
            _logger.LogError(ex, "LLM API error during review.");
            return StatusCode(502, new { error = "AI API error: " + ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during review.");
            return StatusCode(500, new { error = "An unexpected error occurred: " + ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  POST /api/review/upload  (file upload)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts a .patch or .diff file upload and runs an AI code review.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxDiffSizeBytes)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string? repositoryContext,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file was uploaded." });

        // Only accept plain-text patch / diff files.
        var allowedExtensions = new[] { ".patch", ".diff", ".txt" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest(new { error = $"Unsupported file type '{ext}'. Upload a .patch, .diff, or .txt file." });

        // Read the file into a string.
        string diffContent;
        using (var reader = new StreamReader(file.OpenReadStream()))
            diffContent = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(diffContent))
            return BadRequest(new { error = "The uploaded file is empty." });

        var request = new ReviewRequest
        {
            DiffContent        = diffContent,
            RepositoryContext  = repositoryContext
        };

        try
        {
            _logger.LogInformation("Starting review for uploaded file '{FileName}'.", file.FileName);
            var result = await _agent.RunReviewAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "LLM API error during file-upload review.");
            return StatusCode(502, new { error = "AI API error: " + ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during file-upload review.");
            return StatusCode(500, new { error = "An unexpected error occurred: " + ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET /api/review/sample
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a sample Git diff that the frontend "Load Sample" button uses.
    /// </summary>
    [HttpGet("sample")]
    public IActionResult GetSampleDiff()
    {
        return Ok(new { diffContent = SampleDiff.Content });
    }
}

/// <summary>
/// A realistic sample Git diff that exercises multiple review categories:
/// security (hard-coded password, SQL injection), missing tests,
/// code style (Console.WriteLine, magic number, TODO), and a potential bug.
/// </summary>
internal static class SampleDiff
{
    public const string Content = """
        diff --git a/src/UserService.cs b/src/UserService.cs
        index a1b2c3d..e4f5g6h 100644
        --- a/src/UserService.cs
        +++ b/src/UserService.cs
        @@ -1,15 +1,45 @@
         using System;
         using System.Data.SqlClient;
        +using System.Collections.Generic;
        +using System.Linq;
         
         namespace MyApp.Services
         {
             public class UserService
             {
        -        private string _connectionString = "";
        +        // TODO: move this to config later
        +        private string _connectionString = "Server=prod-db;Database=AppDb;User Id=admin;Password=SuperSecret123!;";
        +
        +        private const string AdminPassword = "admin123";
        +
        +        public List<User> SearchUsers(string searchTerm)
        +        {
        +            Console.WriteLine("Searching for: " + searchTerm);
        +
        +            // Build query by string concatenation - fast approach
        +            var query = "SELECT * FROM Users WHERE Name = '" + searchTerm + "'";
        +
        +            var results = new List<User>();
        +            using var conn = new SqlConnection(_connectionString);
        +            conn.Open();
        +            using var cmd = new SqlCommand(query, conn);
        +            using var reader = cmd.ExecuteReader();
        +
        +            while (reader.Read())
        +            {
        +                results.Add(new User
        +                {
        +                    Id   = reader.GetInt32(0),
        +                    Name = reader.GetString(1),
        +                    Age  = reader.GetInt32(2)
        +                });
        +            }
        +
        +            return results;
        +        }
        +
        +        public bool ValidateUser(string username, string password)
        +        {
        +            if (password == AdminPassword) return true;
        +
        +            try { return CheckDatabase(username, password); }
        +            catch (Exception) { }
        +
        +            return false;
        +        }
        +
        +        private bool CheckDatabase(string u, string p) => false;
             }
         }
        diff --git a/src/OrderProcessor.cs b/src/OrderProcessor.cs
        index 0000000..1234567 100644
        --- /dev/null
        +++ b/src/OrderProcessor.cs
        @@ -0,0 +1,30 @@
        +using System;
        +
        +namespace MyApp.Services
        +{
        +    public class OrderProcessor
        +    {
        +        public decimal CalculateDiscount(decimal price, int customerType)
        +        {
        +            // Apply discount based on customer type
        +            if (customerType == 1)
        +                return price * 0.10m;
        +            else if (customerType == 2)
        +                return price * 0.20m;
        +            else if (customerType == 3)
        +                return price * 0.30m;
        +
        +            return 0;
        +        }
        +
        +        public void ProcessOrder(Order order)
        +        {
        +            var discount = CalculateDiscount(order.Price, order.CustomerType);
        +            var finalPrice = order.Price - discount;
        +
        +            // FIXME: should send confirmation email here
        +            Console.WriteLine("Order processed. Final price: " + finalPrice);
        +        }
        +    }
        +}
        """;
}

