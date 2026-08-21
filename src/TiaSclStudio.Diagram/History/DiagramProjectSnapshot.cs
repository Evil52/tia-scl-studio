using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.Diagram.History
{
    /// <summary>
    /// An immutable, in-memory representation of a diagram project.
    /// </summary>
    public sealed class DiagramProjectSnapshot
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(DiagramProject));
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private readonly byte[] _serializedProject;

        private DiagramProjectSnapshot(byte[] serializedProject)
        {
            _serializedProject = serializedProject;
        }

        /// <summary>
        /// Captures the complete project state independently of the source object graph.
        /// </summary>
        public static DiagramProjectSnapshot Capture(DiagramProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            return new DiagramProjectSnapshot(Serialize(project));
        }

        /// <summary>
        /// Creates a deep clone without retaining references to the supplied project.
        /// </summary>
        public static DiagramProject Clone(DiagramProject project)
        {
            return Capture(project).Restore();
        }

        /// <summary>
        /// Restores a new project object graph from this snapshot.
        /// </summary>
        public DiagramProject Restore()
        {
            using (var stream = new MemoryStream(_serializedProject, false))
            using (var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    CloseInput = false,
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                }))
            {
                var project = Serializer.Deserialize(reader) as DiagramProject;
                if (project == null)
                {
                    throw new InvalidDataException("The diagram snapshot does not contain a project.");
                }

                return project;
            }
        }

        internal bool HasSameContent(DiagramProjectSnapshot other)
        {
            if (other == null || _serializedProject.Length != other._serializedProject.Length)
            {
                return false;
            }

            for (var index = 0; index < _serializedProject.Length; index++)
            {
                if (_serializedProject[index] != other._serializedProject[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] Serialize(DiagramProject project)
        {
            var settings = new XmlWriterSettings
            {
                CloseOutput = false,
                Encoding = Utf8WithoutBom,
                Indent = false,
                NewLineHandling = NewLineHandling.None,
                OmitXmlDeclaration = false
            };

            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, settings))
                {
                    Serializer.Serialize(writer, project);
                    writer.Flush();
                }

                return stream.ToArray();
            }
        }
    }
}
