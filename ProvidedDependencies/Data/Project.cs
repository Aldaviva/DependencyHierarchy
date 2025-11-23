using System.ComponentModel;
using System.Xml;
using System.Xml.Serialization;

namespace ProvidedDependencies.Data;

[XmlRoot("Project")]
public record Project {

    [XmlIgnore]
    public string? comment { get; set; }

    [XmlAnyElement("comment"), EditorBrowsable(EditorBrowsableState.Never)]
    public XmlComment? commentEl {
        get => comment.HasText() ? new XmlDocument().CreateComment(' ' + comment.Trim() + ' ') : null;
        set => comment = value?.Data;
    }

    [XmlElement("ItemGroup")]
    public List<ItemGroup> itemGroup { get; } = [];

}

public record ItemGroup {

    [XmlAttribute("Label")]
    public string? label { get; set; }

    [XmlAttribute("Condition")]
    public string? condition { get; set; }

    [XmlElement("PackageReference")]
    public List<PackageReference> packageReferences { get; } = [];

}

public record PackageReference {

    [XmlAttribute("ExcludeAssets")]
    public string? excludeAssets { get; set; }

    [XmlAttribute("PrivateAssets")]
    public string? privateAssets { get; set; }

    [XmlAttribute("Condition")]
    public string? condition { get; set; }

    [XmlAttribute("Version")]
    public required string version { get; set; }

    [XmlAttribute("Include")]
    public required string include { get; set; }

}