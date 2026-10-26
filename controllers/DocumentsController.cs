
using KnowledgeBaseAPI.Data;
using KnowledgeBaseAPI.Models;
using KnowledgeBaseAPI.Services;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowledgeBaseAPI.controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly PdfService _pdfService;
    private readonly OcrService _ocrService;
    private readonly OllamaVisionService _visionService;
    private readonly TextChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly QdrantVectorService _qdrantService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DocumentsController> _logger;


    public DocumentsController(
        ApplicationDbContext context,
        PdfService pdfService,
        OcrService ocrService,
        OllamaVisionService visionService,
        TextChunkingService chunkingService,
        IEmbeddingService embeddingService,
        QdrantVectorService qdrantService,
        IWebHostEnvironment environment,
        ILogger<DocumentsController> logger)
    {
        _context = context;
        _pdfService = pdfService;
        _ocrService = ocrService;
        _visionService = visionService;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _qdrantService = qdrantService;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetDocuments(
        CancellationToken cancellationToken)
    {
        var documents = await _context.Documents
            .AsNoTracking()
            .OrderByDescending(document => document.UploadDate)
            .Select(document => new
            {
                document.Id,
                document.FileName,
                document.UploadDate,
                document.Status,
                document.ErrorMessage,
                ChunkCount = document.Chunks.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(documents);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new
            {
                message = "Choose a non-empty PDF file."
            });
        }

        var displayName = Path.GetFileName(file.FileName);

        var extension = Path.GetExtension(displayName).ToLowerInvariant();

        var allowedExtensions = new[] { ".pdf", ".png", ".jpg", ".jpeg" };

        if (!allowedExtensions.Contains(extension) ||
            !await HasValidFileSignatureAsync(
                file,
                extension,
                cancellationToken))
        {
            return BadRequest(new
            {
                message = "Only valid PDF, PNG, JPG, and JPEG files are allowed."
            });
        }

        string fileHash;

        await using (var inputStream = file.OpenReadStream())
        {
            var hashBytes = await SHA256.HashDataAsync(
                inputStream,
                cancellationToken);

            fileHash = Convert.ToHexString(hashBytes);
        }

        var duplicateExists = await _context.Documents
            .AsNoTracking()
            .AnyAsync(
                document => document.FileHash == fileHash,
                cancellationToken);

        if (duplicateExists)
        {
            return Conflict(new
            {
                message = "هذا الملف مرفوع مسبقًا، حتى لو كان اسمه مختلفًا."
            });
        }

        var uploadsFolder = Path.Combine(
            _environment.ContentRootPath,
            "Uploads");

        Directory.CreateDirectory(uploadsFolder);

        var filePath = Path.Combine(
    uploadsFolder,
    $"{Guid.NewGuid():N}{extension}");

        await using (var stream = System.IO.File.Create(filePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var document = new Document
        {
            FileName = displayName,
            FilePath = filePath,
            FileHash = fileHash,
            UploadDate = DateTime.UtcNow,
            Status = "Processing"
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        return await ProcessDocumentAsync(
            document,
            cancellationToken);
    }

    [HttpPost("{id:int}/retry")]
    public async Task<IActionResult> Retry(
        int id,
        CancellationToken cancellationToken)
    {
        var document = await _context.Documents
            .Include(item => item.Chunks)
            .FirstOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (document == null)
        {
            return NotFound(new
            {
                message = "Document not found."
            });
        }

        if (document.Status != "Failed")
        {
            return Conflict(new
            {
                message = "Only failed documents can be retried."
            });
        }

        document.Status = "Processing";
        document.ErrorMessage = null;

        await _context.SaveChangesAsync(cancellationToken);

        return await ProcessDocumentAsync(
            document,
            cancellationToken);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var document = await _context.Documents
            .Include(item => item.Chunks)
            .FirstOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (document == null)
        {
            return NotFound(new
            {
                message = "Document not found."
            });
        }

        var filePath = document.FilePath;

        // Remove vectors from Qdrant before deleting SQL data.
        await _qdrantService.DeleteDocumentChunksAsync(
            document.Id,
            cancellationToken);

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);

        var uploadsFolder = Path.GetFullPath(
            Path.Combine(_environment.ContentRootPath, "Uploads"))
            + Path.DirectorySeparatorChar;

        var fullPath = Path.GetFullPath(filePath);

        if (fullPath.StartsWith(
            uploadsFolder,
            StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                System.IO.File.Delete(fullPath);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Could not remove the file for document {DocumentId}.",
                    id);
            }
        }

        return Ok(new
        {
            message = "Document deleted successfully."
        });
    }

    private async Task<IActionResult> ProcessDocumentAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        try
        {
            document.Status = "Processing";
            document.ErrorMessage = null;

            await _context.SaveChangesAsync(cancellationToken);

            // Extract text from the PDF.
            var extension = Path.GetExtension(document.FilePath)
    .ToLowerInvariant();

            string extractedText;


            if (extension is ".png" or ".jpg" or ".jpeg")
            {
                try
                {
                    extractedText =
                        await _visionService.ExtractTextFromImageAsync(
                            document.FilePath,
                            cancellationToken);
                }
                catch (Exception visionException)
                    when (visionException is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        visionException,
                        "Vision extraction failed for document {DocumentId}. Falling back to OCR.",
                        document.Id);

                    extractedText = _ocrService.ExtractTextFromImage(
                        document.FilePath);
                }
            }

            else if (extension == ".pdf")
            {
                try
                {
                    // First try extracting selectable PDF text.
                    extractedText = _pdfService.ExtractText(
                        document.FilePath);
                }
                catch (InvalidOperationException exception)
                    when (exception.Message.Contains(
                        "No selectable text",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // If the PDF is scanned, process its pages as images.
                    _logger.LogInformation(
                        "PDF {DocumentId} appears to be scanned. Starting vision extraction.",
                        document.Id);

                    extractedText = await ExtractScannedPdfTextAsync(
                        document.FilePath,
                        cancellationToken);
                }
            }

            else
            {
                throw new InvalidDataException(
                    $"Unsupported file type: {extension}");
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw new InvalidDataException(
                    "No text could be extracted from this PDF.");
            }

            _logger.LogInformation(
                "PDF extracted text preview for document {DocumentId}: {Preview}",
                document.Id,
                extractedText[..Math.Min(1000, extractedText.Length)]);

            // Delete any old vectors before reprocessing this document.
            await _qdrantService.DeleteDocumentChunksAsync(
                document.Id,
                cancellationToken);

            // Remove old SQL chunks.
            _context.DocumentChunks.RemoveRange(document.Chunks);
            document.Chunks.Clear();

            await _context.SaveChangesAsync(cancellationToken);

            document.Content = extractedText;

            var chunks = _chunkingService.SplitText(extractedText);

            for (var index = 0; index < chunks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunkContent = chunks[index];

                _logger.LogInformation(
                    "Creating embedding for document {DocumentId}, chunk {ChunkIndex}.",
                    document.Id,
                    index);

                var embedding =
                    await _embeddingService.CreateEmbeddingAsync(
                        chunkContent,
                        cancellationToken);

                if (embedding == null || embedding.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Embedding creation failed for chunk {index}.");
                }

                document.Chunks.Add(new DocumentChunk
                {
                    ChunkIndex = index,
                    Content = chunkContent,
                    EmbeddingJson = JsonSerializer.Serialize(embedding),
                    Document = document
                });
            }

            // Save chunks first so SQL Server assigns their IDs.
            await _context.SaveChangesAsync(cancellationToken);

            // Upload each chunk vector and metadata to Qdrant.
            foreach (var chunk in document.Chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _qdrantService.UpsertChunkAsync(
                    chunk,
                    document.FileName,
                    cancellationToken);
            }

            // Mark completed only after Qdrant upload succeeds.
            document.Status = "Completed";
            document.ErrorMessage = null;

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                documentId = document.Id,
                fileName = document.FileName,
                status = document.Status,
                chunkCount = document.Chunks.Count
            });
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Processing failed for document {DocumentId}.",
                document.Id);

            document.Status = "Failed";
            document.ErrorMessage = exception.Message;

            await _context.SaveChangesAsync(cancellationToken);

            return UnprocessableEntity(new
            {
                documentId = document.Id,
                fileName = document.FileName,
                status = document.Status,
                message = document.ErrorMessage
            });
        }
    }

    

    [HttpPost("backfill-file-hashes")]
    public async Task<IActionResult> BackfillFileHashes(
        CancellationToken cancellationToken)
    {
        var documents = await _context.Documents
            .Where(document => document.FileHash == null)
            .ToListAsync(cancellationToken);

        var results = new List<object>();

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!System.IO.File.Exists(document.FilePath))
            {
                results.Add(new
                {
                    document.Id,
                    document.FileName,
                    status = "FileNotFound",
                    path = document.FilePath
                });

                continue;
            }

            await using var stream = System.IO.File.OpenRead(
                document.FilePath);

            var hashBytes = await SHA256.HashDataAsync(
                stream,
                cancellationToken);

            document.FileHash = Convert.ToHexString(hashBytes);

            results.Add(new
            {
                document.Id,
                document.FileName,
                status = "HashCalculated"
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(results);
    }

    [HttpPost("backfill-qdrant")]
    public async Task<IActionResult> BackfillQdrant(
        CancellationToken cancellationToken)
    {
        var imported = await _qdrantService.BackfillExistingChunksAsync(
            cancellationToken);

        return Ok(new
        {
            message = "Backfill completed.",
            importedChunks = imported
        });
    }

    private static async Task<bool> HasValidFileSignatureAsync(
        IFormFile file,
        string extension,
        CancellationToken cancellationToken)
    {
        var header = new byte[8];

        await using var stream = file.OpenReadStream();

        var bytesRead = await stream.ReadAsync(
            header.AsMemory(),
            cancellationToken);

        if (extension == ".pdf")
        {
            return bytesRead >= 5 &&
                header[0] == 0x25 &&
                header[1] == 0x50 &&
                header[2] == 0x44 &&
                header[3] == 0x46 &&
                header[4] == 0x2D;
        }

        if (extension == ".png")
        {
            byte[] signature =
            {
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        };

            return bytesRead >= signature.Length &&
                header.SequenceEqual(signature);
        }

        if (extension is ".jpg" or ".jpeg")
        {
            return bytesRead >= 3 &&
                header[0] == 0xFF &&
                header[1] == 0xD8 &&
                header[2] == 0xFF;
        }

        return false;
    }

    private async Task<string> ExtractScannedPdfTextAsync(
        string pdfPath,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder();

        using var pdfDocument =
            UglyToad.PdfPig.PdfDocument.Open(pdfPath);

        var pageCount = pdfDocument.NumberOfPages;

        if (pageCount == 0)
        {
            throw new InvalidDataException(
                "The PDF contains no pages.");
        }

        var tempFolder = Path.Combine(
            Path.GetTempPath(),
            "KnowledgeBaseVision");

        Directory.CreateDirectory(tempFolder);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imagePath = Path.Combine(
                tempFolder,
                $"{Guid.NewGuid():N}.png");

            try
            {
                // PDFtoImage page indexes start at zero.
                await using (var pdfStream =
                    System.IO.File.OpenRead(pdfPath))
                {
                    PDFtoImage.Conversion.SavePng(
                        imageFilename: imagePath,
                        pdfStream: pdfStream,
                        page: pageIndex);
                }

                string pageText;

                try
                {
                    pageText =
                        await _visionService.ExtractTextFromImageAsync(
                            imagePath,
                            cancellationToken);
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        exception,
                        "Vision extraction failed on PDF page {PageNumber}. Falling back to OCR.",
                        pageIndex + 1);

                    pageText = _ocrService.ExtractTextFromImage(
                        imagePath);
                }

                result.AppendLine(
                    $"--- Page {pageIndex + 1} ---");

                result.AppendLine(pageText);
                result.AppendLine();
            }
            finally
            {
                if (System.IO.File.Exists(imagePath))
                {
                    System.IO.File.Delete(imagePath);
                }
            }
        }

        var extractedText = result.ToString().Trim();

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new InvalidOperationException(
                "No text could be extracted from the scanned PDF.");
        }

        return extractedText;
    }


}
