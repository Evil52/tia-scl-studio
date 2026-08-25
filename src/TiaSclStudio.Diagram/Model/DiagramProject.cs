using System.Collections.Generic;
using System.Xml.Serialization;
using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Diagram.Model
{
    [XmlRoot("TiaSclStudioProject")]
    public sealed class DiagramProject
    {
        public const int LegacyFormatVersion = 1;
        public const int GroupFormatVersion = 2;
        public const int CpuFormatVersion = 3;
        public const int CurrentFormatVersion = 4;

        public DiagramProject()
        {
            FormatVersion = CurrentFormatVersion;
            Plant = new PlantProject();
            Plant.CpuFamily = PlcCpuFamily.S71200;
            Sheets = new List<CallSheet>();
        }

        [XmlAttribute]
        public int FormatVersion { get; set; }

        public PlantProject Plant { get; set; }

        [XmlArrayItem("Sheet")]
        public List<CallSheet> Sheets { get; set; }
    }
}
