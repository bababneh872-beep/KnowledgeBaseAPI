
using System;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;

namespace KnowledgeBaseAPI.Services;

public class PdfService
{
    public string ExtractText(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "The PDF file path is required.",
                nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "The PDF file was not found.",
                filePath);
        }

        var result = new StringBuilder();

        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            var pageText = page.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                result.AppendLine(pageText);
                result.AppendLine();
            }
        }

        var extractedText = result.ToString().Trim();

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new InvalidOperationException(
                "No selectable text was found in this PDF. " +
                "The document may contain scanned pages and require OCR.");
        }

        return extractedText;
    }
}
