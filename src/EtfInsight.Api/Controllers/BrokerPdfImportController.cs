using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace EtfInsight.Api.Controllers
{
    [ApiController]
    [Route("api")]
    [Produces("application/json")]
    public class BrokerPdfImportController(
        IBrokerPdfImportService importService,
        ILogger<BrokerPdfImportController> logger) : ControllerBase
    {

        private const int MaxFilesPerImport = 100;
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB per file

        [HttpPost("portfolios/{portfolioId:guid}/import/broker-import")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StartImport(
            Guid portfolioId,
            [FromForm] IFormCollection files,
            CancellationToken ct = default)
        {

            var userId = HttpContext.GetGuestId();

            if (files == null || files.Files.Count == 0)
            {
                return BadRequest(new { Error = "At least one PDF file is required." });
            }

            if (files.Count > MaxFilesPerImport)
            {
                return BadRequest(new { Error = $"A maximum of {MaxFilesPerImport} files can be uploaded per import." });
            }

            var invalids = files
                .Where(f => f.Length == 0 || f.Length > MaxFileSizeBytes || !f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .Select(f => new { FileName = f.FileName, Size = f.Length })
                .ToList();

            if (invalids.Any())
            {
                return BadRequest(new { Error = "Invalid files (empty, >10 MB, or not .pdf).", Files = invalids });
            }

            var result = await importService.StartImportAsync(portfolioId, userId, files.Files, ct);

            return result.Status == "not_found"
                ? NotFound(new { Error = $"Portfolio {portfolioId} not found or not owned by you." })
                : AcceptedAtAction(result);
        }


        [HttpGet("import-jobs/{jobId:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJobStatus(Guid jobId, CancellationToken ct = default)
        {
            var userId = HttpContext.GetGuestId();
            var result = await importService.GetJobStatusAsync(jobId, userId, ct);

            return result == null
                ? NotFound(new { Error = $"Import job {jobId} not found or not owned by you." })
                : Ok(result);
        }

    }
}