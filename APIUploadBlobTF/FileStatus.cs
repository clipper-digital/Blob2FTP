
namespace APIUploadBlobTF
{
    public class FileStatus
    {
        public string? Filename { get; set; }

        public string? DestinationName { get; set; }

        public string? DestinationPath { get; set; }

        public DateTime DestinationDateTime { get; set; }

        public bool Success { get; set; }
    }
}
