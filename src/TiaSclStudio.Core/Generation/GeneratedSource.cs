using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Core.Generation
{
    public sealed class GeneratedSource
    {
        public GeneratedSource(string fileName, string content, GeneratedSourceKind kind, int order)
        {
            FileName = fileName;
            Content = content;
            Kind = kind;
            Order = order;
        }

        public string FileName { get; private set; }

        public string Content { get; private set; }

        public GeneratedSourceKind Kind { get; private set; }

        public int Order { get; private set; }
    }
}
