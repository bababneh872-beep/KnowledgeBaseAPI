
using System;
using System.IO;
using TesseractOCR;
using TesseractOCR.Enums;

namespace KnowledgeBaseAPI.Services;

public class OcrService
{
    private readonly string _tessdataPath;

    public OcrService()
    {
        _tessdataPath = Path.Combine(
            AppContext.BaseDirectory,
            "tessdata");
    }

    public string ExtractTextFromImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException(
                "The image path is required.",
                nameof(imagePath));
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException(
                "The image file was not found.",
                imagePath);
        }

        var arabicData = Path.Combine(
            _tessdataPath,
            "ara.traineddata");

        var englishData = Path.Combine(
            _tessdataPath,
            "eng.traineddata");

        if (!File.Exists(arabicData) ||
            !File.Exists(englishData))
        {
            throw new FileNotFoundException(
                $"OCR language files were not found in {_tessdataPath}. " +
                "Both ara.traineddata and eng.traineddata are required.");
        }

        using var engine = new Engine(
            _tessdataPath,
            "ara+eng",
            EngineMode.Default);

        using var image =
            TesseractOCR.Pix.Image.LoadFromFile(imagePath);

        using var page = engine.Process(image);

        var extractedText = page.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new InvalidOperationException(
                "OCR could not extract readable text from the image.");
        }

        return extractedText;
    }
}
