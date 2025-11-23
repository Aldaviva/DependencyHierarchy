using System.Xml.Serialization;

namespace ProvidedDependencies.Data;

[XmlRoot("FileList")]
public record FrameworkFileList {

    [XmlElement("File")]
    public List<FrameworkFile> files { get; } = [];

}

public record FrameworkFile {

    [XmlAttribute("Type")]
    public FrameworkFileType type { get; init; }

    [XmlAttribute("Path")]
    public required string path { get; init; }

    [XmlAttribute("AssemblyName")]
    public required string assemblyName { get; init; }

}

public enum FrameworkFileType {

    Managed,
    Analyzer

}