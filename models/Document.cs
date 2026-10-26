namespace KnowledgeBaseAPI.Models
{
    public class Document
    {
        public int Id { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;
        public string? FileHash { get; set; }

        public string Content { get; set; } = string.Empty;

        public DateTime UploadDate { get; set; }

        public string Status { get; set; } = "Uploaded";

        public string? ErrorMessage { get; set; }

        public ICollection<DocumentChunk> Chunks { get; set; }
            = new List<DocumentChunk>();
    }
}