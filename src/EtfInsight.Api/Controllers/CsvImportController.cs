using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Dapper;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using EtfInsight.Core.Interfaces;


namespace EtfInsight.Api.Controllers
{
    // ── Controller ─────────────────────────────────────────────────────────────────

    [ApiController]
    [Route("api/portfolios/{portfolioId:guid}/transactions")]
    [Produces("application/json")]
    public class CsvImportController : ControllerBase
    {

        private readonly IDbConnection _db;
        private readonly IPortfolioRepository _portfolioRepo;
        private readonly IIngestionService _ingestionService;
        private readonly ICsvImportService _csvImportService;
        private readonly ILogger<CsvImportController> _logger;


        public CsvImportController(
            IDbConnection db,
            IPortfolioRepository portfolioRepo,
            IIngestionService ingestionService,
            ICsvImportService csvImportService,
            ILogger<CsvImportController> logger)
        {
            _db = db;
            _portfolioRepo = portfolioRepo;
            _ingestionService = ingestionService;
            _csvImportService = csvImportService;
            _logger = logger;
        }

        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Import(
            Guid portfolioId,
            IFormFile file,
            CancellationToken cancellationToken = default)
        {
            var exists = await _db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)", new { Id = portfolioId });

            if (!exists)
            {
                return NotFound(new { Error = $"Portfolio {portfolioId} not found." });
            }

            if (file is null || file.Length == 0)
            {
                return BadRequest(new { Error = "No file uploaded." });
            }

            using var reader = new StreamReader(file.OpenReadStream());
            var result = await _csvImportService.ImportAsync(portfolioId, reader, cancellationToken: cancellationToken);

            if (result.Imported == 0)
                return BadRequest(new { Error = "No valid rows found.", invalidRows = result.InvalidRows });


            return result.AnyIngesting
                ? Accepted(result)
                : Ok(result);
        }
    }
}