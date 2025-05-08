using file_upload_api.Data;
using file_upload_api.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

[Route("api/[controller]")]
[ApiController]
public class UploadController : ControllerBase
{
    private readonly string _tempRoot = Path.Combine(Directory.GetCurrentDirectory(), "TempChunks");
    private readonly string _uploadRoot = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
    private readonly AppDbContext _context;
    private readonly ILogger<UploadController> _logger;

    public UploadController(AppDbContext context, ILogger<UploadController> logger)
    {
        _context = context;
        _logger = logger;
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_uploadRoot);
    }

    [HttpPost("upload-chunk")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadChunk([FromForm] UploadChunkRequest request)
    {
        try
        {
            if (request.Chunk == null || request.Chunk.Length == 0)
                return BadRequest("Empty chunk.");

            var tempFolder = Path.Combine(_tempRoot, request.FileId);
            Directory.CreateDirectory(tempFolder);

            var chunkPath = Path.Combine(tempFolder, $"{request.ChunkIndex}.part");

            // Save the chunk to temporary location
            var isWritten = await TryWriteToFileAsync(chunkPath, async () =>
            {
                using (var stream = new FileStream(chunkPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    await request.Chunk.CopyToAsync(stream);
                }
            });

            if (!isWritten)
                return StatusCode(StatusCodes.Status500InternalServerError, "Error writing chunk to temporary file.");

            // Check if all chunks have arrived
            var uploadedChunks = Directory.GetFiles(tempFolder, "*.part").Length;
            if (uploadedChunks == request.TotalChunks)
            {
                var finalFilePath = Path.Combine(_uploadRoot, request.FileName);

                // Combine chunks into final file
                try
                {
                    using (var finalStream = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    {
                        for (int i = 0; i < request.TotalChunks; i++)
                        {
                            var partPath = Path.Combine(tempFolder, $"{i}.part");

                            // Check if part file exists before trying to write it
                            if (!System.IO.File.Exists(partPath))
                            {
                                _logger.LogError($"Chunk {i} does not exist for file {request.FileId}. Aborting combine process.");
                                return StatusCode(StatusCodes.Status500InternalServerError, "Missing chunk during file combination.");
                            }

                            await TryWriteToFileAsync(finalFilePath, async () =>
                            {
                                using var partStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                await partStream.CopyToAsync(finalStream);
                            });
                        }
                    }

                    // Clean up temp files
                    Directory.Delete(tempFolder, true);

                    // Save file metadata to DB
                    var uploadedFile = new UploadedFile
                    {
                        FileName = request.FileName,
                        FilePath = finalFilePath,
                        UploadedAt = DateTime.UtcNow
                    };

                    _context.UploadedFiles.Add(uploadedFile);
                    await _context.SaveChangesAsync();

                    return Ok(new { message = "Upload complete", filePath = finalFilePath });
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "Error while combining file chunks.");
                    return StatusCode(StatusCodes.Status500InternalServerError, "Error while combining file chunks.");
                }
            }

            return Ok(new { message = $"Chunk {request.ChunkIndex + 1} received." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while uploading the file.");
            return StatusCode(StatusCodes.Status500InternalServerError, $"An error occurred: {ex.Message}");
        }
    }

    // Retry mechanism for writing to file
    private static async Task<bool> TryWriteToFileAsync(string path, Func<Task> writeAction)
    {
        int maxAttempts = 3;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                await writeAction();
                return true;
            }
            catch (IOException ex)
            {
                // Log and retry for file access errors
                if (i == maxAttempts - 1)
                {
                    throw new IOException("Error writing to file after multiple attempts.", ex);
                }
                await Task.Delay(100); // Wait before retrying
            }
            catch (Exception ex)
            {
                // Catch any other errors and log
                throw new InvalidOperationException("Unexpected error during file write operation.", ex);
            }
        }
        return false;
    }
}

public class UploadChunkRequest
{
    [Required]
    public IFormFile Chunk { get; set; }

    [Required]
    public string FileName { get; set; }

    [Required]
    public string FileId { get; set; }

    [Required]
    public int ChunkIndex { get; set; }

    [Required]
    public int TotalChunks { get; set; }
}
