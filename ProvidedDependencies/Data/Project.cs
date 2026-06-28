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
        get => comment.HasText ? new XmlDocument().CreateComment(' ' + comment.Trim() + ' ') : null;
        set => comment = value?.Data;
    }

    [XmlElement("PropertyGroup"), EditorBrowsable(EditorBrowsableState.Never)]
    public XmlElement[] propertyGroupEls {
        get {
            XmlDocument tempDoc = new();
            var         els     = new XmlElement[propertyGroup.Count];
            int         i       = 0;
            foreach (KeyValuePair<string, string> property in propertyGroup) {
                XmlElement propertyEl = tempDoc.CreateElement(property.Key);
                propertyEl.AppendChild(tempDoc.CreateTextNode(property.Value));
                els[i++] = propertyEl;
            }
            return els;
        }
        set {
            propertyGroup.Clear();
            propertyGroup.AddAll(value.Select(prop => new KeyValuePair<string, string>(prop.LocalName, prop.InnerText)));
        }
    }

    [XmlIgnore]
    public IDictionary<string, string> propertyGroup { get; } = new Dictionary<string, string>();

    [XmlElement("ItemGroup")]
    public List<ItemGroup> itemGroups { get; } = [];

}

public record ItemGroup {

    [XmlAttribute("Label")]
    public string? label { get; set; }

    [XmlAttribute("Condition")]
    public string? condition { get; set; }

    [XmlElement("PackageReference")]
    public List<PackageReference> packageReferences { get; } = [];

    [XmlElement("PackageVersion")]
    public List<PackageReference> packageVersions { get; } = [];

}

public record PackageReference {

    [XmlAttribute("ExcludeAssets")]
    public string? excludeAssets { get; set; }

    [XmlAttribute("IncludeAssets")]
    public string? includeAssets { get; set; }

    [XmlAttribute("PrivateAssets")]
    public string? privateAssets { get; set; }

    [XmlAttribute("Condition")]
    public string? condition { get; set; }

    [XmlAttribute("NoWarn")]
    public string? noWarn { get; set; }

    [XmlAttribute("Version")]
    public string? version { get; set; }

    [XmlAttribute("Include")]
    public string? include { get; set; }

    [XmlAttribute("Update")]
    public string? update { get; set; }

}