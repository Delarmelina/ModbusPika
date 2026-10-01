using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace ModbusTcpTroubleshooter.App;

public sealed class ManualTopic
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Section { get; init; }
    public required string Keywords { get; init; }
    public required XElement Content { get; init; }
    public string SearchText => $"{Section} {Title} {Keywords} {Content.Value}";
    public string Preview
    {
        get
        {
            var first = Content.Element("p")?.Value.Trim() ?? "";
            return first.Length > 110 ? first[..110] + "…" : first;
        }
    }
    public override string ToString() => Title;
}

public sealed class ManualSection
{
    public required string Title { get; init; }
    public required List<ManualTopic> Topics { get; init; }
}

public sealed class ManualCatalog
{
    private readonly Dictionary<string, ManualTopic> _byId;
    public IReadOnlyList<ManualSection> Sections { get; }
    public IReadOnlyList<ManualTopic> Topics { get; }

    private ManualCatalog(List<ManualSection> sections)
    {
        Sections = sections;
        Topics = sections.SelectMany(section => section.Topics).ToArray();
        _byId = Topics.ToDictionary(topic => topic.Id, StringComparer.OrdinalIgnoreCase);
        if (!_byId.ContainsKey("inicio")) throw new InvalidDataException("O manual precisa do tópico 'inicio'.");
        foreach (var topic in Topics)
        {
            foreach (var link in topic.Content.Elements("link"))
                if (!_byId.ContainsKey(Required(link, "topic")))
                    throw new InvalidDataException($"Link desconhecido no manual: {topic.Id}.");
            foreach (var table in topic.Content.Elements("table"))
            {
                var count = table.Element("head")?.Elements("cell").Count() ?? 0;
                if (count == 0 || table.Elements("row").Any(row => row.Elements("cell").Count() != count))
                    throw new InvalidDataException($"Colunas inconsistentes no tópico {topic.Id}.");
            }
        }
    }

    public static ManualCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ModbusTcpTroubleshooter.App.ManualContent.xml")
            ?? throw new FileNotFoundException("O conteúdo do manual não foi incluído no aplicativo.");
        var document = XDocument.Load(stream);
        var sections = document.Root?.Elements("section").Select(section => new ManualSection
        {
            Title = Required(section, "title"),
            Topics = section.Elements("topic").Select(topic => new ManualTopic
            {
                Id = Required(topic, "id"),
                Title = Required(topic, "title"),
                Section = Required(section, "title"),
                Keywords = (string?)topic.Attribute("keywords") ?? "",
                Content = topic
            }).ToList()
        }).ToList() ?? throw new InvalidDataException("Estrutura do manual inválida.");
        return new ManualCatalog(sections);
    }

    private static string Required(XElement element, string attribute) =>
        (string?)element.Attribute(attribute) is { Length: > 0 } value ? value :
        throw new InvalidDataException($"Atributo obrigatório ausente: {attribute}.");

    public ManualTopic Get(string? id) =>
        id is not null && _byId.TryGetValue(id, out var topic) ? topic : _byId["inicio"];

    public IReadOnlyList<ManualTopic> Search(string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return Topics;
        var compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
        return Topics.Where(topic => terms.All(term =>
            compare.IndexOf(topic.SearchText, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)).ToArray();
    }
}
