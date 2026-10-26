namespace KnowledgeBaseAPI.Services;

public class TextChunkingService
{
    public List<string> SplitText(
    string text,
    int chunkSize = 1000,
    int overlap = 150)
    {
        var chunks = new List<string>();

    if (string.IsNullOrWhiteSpace(text))
            return chunks;

        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));

        if (overlap < 0 || overlap >= chunkSize)
            throw new ArgumentOutOfRangeException(nameof(overlap));

        text = text.Trim();

        int start = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + chunkSize, text.Length);

            if (end < text.Length)
            {
                // Try to end the chunk at a paragraph or sentence boundary.
                int minimumEnd = start + (chunkSize / 2);

                int boundary = -1;

                for (int i = end - 1; i >= minimumEnd; i--)
                {
                    if (text[i] == '\n' ||
                        text[i] == '.' ||
                        text[i] == '!' ||
                        text[i] == '?' ||
                        text[i] == '؟' ||
                        text[i] == '؛')
                    {
                        boundary = i + 1;
                        break;
                    }
                }

                if (boundary > start)
                    end = boundary;
            }

            string chunk = text[start..end].Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);

            if (end >= text.Length)
                break;

            // Repeat part of the previous chunk to preserve context.
            start = Math.Max(start + 1, end - overlap);
        }

        return chunks;
    }

}
