using file_upload_api.Data;
using file_upload_api.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;

[Route("api/[controller]")]
[ApiController]
public class UploadController : ControllerBase
{
    private readonly string _tempRoot = Path.Combine(Directory.GetCurrentDirectory(), "TempChunks");
    private readonly string _uploadRoot = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");

    private readonly AppDbContext _context;

    public UploadController(AppDbContext context)
    {
        _context = context;
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_uploadRoot);
    }


    [HttpPost("/upload-chunk")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadChunk(
        [FromForm] IFormFile chunk,
        [FromForm] string fileName,
        [FromForm] string fileId,
        [FromForm] int chunkIndex,
        [FromForm] int totalChunks)
    {
        if (chunk == null || chunk.Length == 0)
            return BadRequest("Empty chunk.");

        var tempFolder = Path.Combine(_tempRoot, fileId);
        Directory.CreateDirectory(tempFolder);

        var chunkPath = Path.Combine(tempFolder, $"{chunkIndex}.part");

        // Save the chunk to temporary location
        using (var stream = new FileStream(chunkPath, FileMode.Create))
        {
            await chunk.CopyToAsync(stream);
        }

        // Check if all chunks have arrived
        var uploadedChunks = Directory.GetFiles(tempFolder, "*.part").Length;
        if (uploadedChunks == totalChunks)
        {
            var finalFilePath = Path.Combine(_uploadRoot, fileName);

            using (var finalStream = new FileStream(finalFilePath, FileMode.Create))
            {
                for (int i = 0; i < totalChunks; i++)
                {
                    var partPath = Path.Combine(tempFolder, $"{i}.part");
                    using var partStream = new FileStream(partPath, FileMode.Open);
                    await partStream.CopyToAsync(finalStream);
                }
            }

            // Clean up temp files
            Directory.Delete(tempFolder, true);

            // Optionally: Save metadata to DB here
            // Save file metadata to DB
            var uploadedFile = new UploadedFile
            {
                FileName = fileName,
                FilePath = finalFilePath,
                UploadedAt = DateTime.UtcNow
            };

            _context.UploadedFiles.Add(uploadedFile);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Upload complete", filePath = finalFilePath });
        }

        return Ok(new { message = $"Chunk {chunkIndex + 1} received." });
    }
}
