using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace ModbusTcpTroubleshooter.App;

public static class ReportExporter
{
    public static async Task ExportAsync(string path, string markdown, IReadOnlyList<TopologyLink>? topology = null,
        IReadOnlyList<NetworkDiscoveryRow>? devices = null, IReadOnlyList<NeighborAdvertisement>? neighbors = null, bool serverMode = false)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Selecione um arquivo .md ou .pdf.", nameof(path));
        if (string.IsNullOrWhiteSpace(markdown)) throw new ArgumentException("Relatorio vazio.", nameof(markdown));

        var images = new Dictionary<string, byte[]>();
        var relevantTopology = topology?.Where(x => x.Evidence is "Modbus confirmado" or "Captura Modbus TCP"
            || x.Evidence.EndsWith(" anunciado", StringComparison.Ordinal)).ToArray() ?? [];
        var overview = ModbusTopologyOverview.Build(devices ?? [], relevantTopology, neighbors ?? [], serverMode);
        if (relevantTopology.Length > 0 || overview.Peers.Count > 0)
        {
            var graphText = new StringBuilder("## Diagrama visual de topologia Modbus\n\nParticipantes Modbus observados/validados. Infraestrutura anunciada nao comprova o caminho fisico ate cada dispositivo.\n\n");
            var fileName = Path.GetFileNameWithoutExtension(path) + ".topologia-01.png";
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            var png = dispatcher is not null && !dispatcher.CheckAccess()
                ? dispatcher.Invoke(() => RenderTopologyImage(relevantTopology, devices ?? [], neighbors ?? [], serverMode))
                : RenderTopologyImage(relevantTopology, devices ?? [], neighbors ?? [], serverMode);
            images.Add(fileName, png);
            graphText.AppendLine($"![Topologia observada](<{fileName}>)\n");
            // The visual graph replaces its text equivalent in exports; the in-app report keeps readable links.
            var logicalStart = markdown.IndexOf("### Diagrama logico (enlaces de evidencia)", StringComparison.Ordinal);
            if (logicalStart >= 0)
            {
                var fenceStart = markdown.IndexOf("```text", logicalStart, StringComparison.Ordinal);
                var fenceEnd = fenceStart < 0 ? -1 : markdown.IndexOf("```", fenceStart + 7, StringComparison.Ordinal);
                if (fenceEnd >= 0) markdown = markdown.Remove(logicalStart, fenceEnd + 3 - logicalStart);
            }
            var index = markdown.IndexOf("## Topologia observada e rotas", StringComparison.Ordinal);
            markdown = index >= 0 ? markdown.Insert(index, graphText.ToString()) : markdown + "\n" + graphText;
        }

        // Render completely before replacing the destination; a failed export preserves an existing report.
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                await Task.Run(() => RenderPdf(temporary, markdown, images));
            else
            {
                foreach (var item in images)
                    await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, item.Key), item.Value);
                await File.WriteAllTextAsync(temporary, markdown, new UTF8Encoding(false));
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] RenderTopologyImage(TopologyLink[] links, IReadOnlyList<NetworkDiscoveryRow> devices,
        IReadOnlyList<NeighborAdvertisement> neighbors, bool serverMode)
    {
        var diagram = new TopologyDiagram { Links = links, Devices = devices, Neighbors = neighbors, ServerMode = serverMode };
        var peerCount = ModbusTopologyOverview.Build(devices, links, neighbors, serverMode).Peers.Count;
        var size = new System.Windows.Size(1100, Math.Max(350, 165 + Math.Max(1, peerCount) * 100));
        diagram.Measure(size);
        diagram.Arrange(new System.Windows.Rect(size));
        diagram.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(diagram);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void RenderPdf(string path, string markdown, IReadOnlyDictionary<string, byte[]> images)
    {
        var document = new Document();
        document.Info.Title = "Diagnostico Modbus TCP";
        document.Info.Author = "Modbus TCP Troubleshooter";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Segoe UI";
        normal.Font.Size = 9;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        foreach (var (name, size) in new[] { (StyleNames.Heading1, 18), (StyleNames.Heading2, 13), (StyleNames.Heading3, 10) })
        {
            var style = document.Styles[name]!;
            style.Font.Name = "Segoe UI";
            style.Font.Size = size;
            style.Font.Bold = true;
            style.Font.Color = Color.FromRgb(32, 69, 96);
            style.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
            style.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);
            style.ParagraphFormat.KeepWithNext = true;
        }
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.6);
        var header = section.Headers.Primary.AddParagraph("Diagnostico Modbus TCP | Relatorio tecnico");
        header.Format.Font.Size = 8;
        header.Format.Font.Color = Colors.Gray;
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.Format.Font.Size = 8;
        footer.AddText("Pagina ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var code = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("```", StringComparison.Ordinal)) { code = !code; continue; }
            if (!code && string.IsNullOrWhiteSpace(line)) continue;
            if (!code && line.StartsWith("![Topologia observada](<", StringComparison.Ordinal))
            {
                var name = line["![Topologia observada](<".Length..].TrimEnd(')', '>');
                if (images.TryGetValue(name, out var bytes))
                {
                    var imageParagraph = section.AddParagraph();
                    imageParagraph.Format.Alignment = ParagraphAlignment.Center;
                    imageParagraph.Format.KeepTogether = true;
                    var image = imageParagraph.AddImage("base64:" + Convert.ToBase64String(bytes));
                    image.Width = Unit.FromCentimeter(23.5);
                    image.LockAspectRatio = true;
                }
                continue;
            }
            if (!code && line.TrimStart().StartsWith('|') && i + 1 < lines.Length && IsSeparator(lines[i + 1]))
            {
                var rows = new List<string[]> { Cells(line) };
                i += 2;
                while (i < lines.Length && lines[i].TrimStart().StartsWith('|')) rows.Add(Cells(lines[i++]));
                i--;
                AddTable(section, rows);
                continue;
            }
            var paragraph = section.AddParagraph();
            if (code)
            {
                paragraph.Format.Font.Name = "Consolas";
                paragraph.Format.Font.Size = 8;
                paragraph.Format.SpaceAfter = 0;
                paragraph.Format.Shading.Color = Color.FromRgb(243, 245, 247);
                // Bound raw monospace lines to the printable width, preserving all source characters.
                for (var offset = 0; offset < line.Length; offset += 130)
                {
                    if (offset > 0) paragraph.AddLineBreak();
                    paragraph.AddText(line.Substring(offset, Math.Min(130, line.Length - offset)));
                }
            }
            else
            {
                var level = line.TakeWhile(c => c == '#').Count();
                if (level is >= 1 and <= 6 && line.Length > level && line[level] == ' ')
                {
                    paragraph.Style = level == 1 ? StyleNames.Heading1 : level == 2 ? StyleNames.Heading2 : StyleNames.Heading3;
                    line = line[(level + 1)..];
                }
                if (line.StartsWith("- ", StringComparison.Ordinal)) line = "\u2022 " + line[2..];
                AddInline(paragraph, line);
            }
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(path);
    }

    private static void AddTable(Section section, List<string[]> rows)
    {
        var count = rows[0].Length;
        var table = section.AddTable();
        table.Borders.Width = 0.4;
        table.Borders.Color = Color.FromRgb(190, 201, 210);
        table.Format.Font.Size = count > 6 ? 7 : 8;
        table.Format.SpaceAfter = 3;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(4);
        var checklist = count == 3 && rows[0][0] == "Etapa";
        for (var column = 0; column < count; column++)
            table.AddColumn(Unit.FromCentimeter(checklist ? 26.7 * new[] { .72, .17, .11 }[column] : 26.7 / count));
        for (var index = 0; index < rows.Count; index++)
        {
            var row = table.AddRow();
            row.TopPadding = row.BottomPadding = Unit.FromPoint(checklist ? 2 : 4);
            if (index == 0)
            {
                row.HeadingFormat = true;
                row.Format.Font.Bold = true;
                row.Shading.Color = Color.FromRgb(228, 237, 244);
            }
            for (var column = 0; column < count; column++)
                AddInline(row.Cells[column].AddParagraph(), column < rows[index].Length ? rows[index][column] : "");
        }
        section.AddParagraph().Format.SpaceAfter = 4;
    }

    private static string[] Cells(string line) => line.Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray();
    private static bool IsSeparator(string line) => Cells(line).All(x => Regex.IsMatch(x, @"^:?-{3,}:?$"));
    private static string WrapLongTokens(string text) => Regex.Replace(text, @"\S{32,}", match =>
        string.Join("\u200B", Enumerable.Range(0, (match.Length + 23) / 24)
            .Select(i => match.Value.Substring(i * 24, Math.Min(24, match.Length - i * 24)))));

    private static void AddInline(Paragraph paragraph, string text)
    {
        foreach (var part in Regex.Split(text, @"(\*\*[^*]+\*\*|`[^`]+`)"))
        {
            if (part.StartsWith("**", StringComparison.Ordinal) && part.EndsWith("**", StringComparison.Ordinal) && part.Length > 4)
                paragraph.AddFormattedText(WrapLongTokens(part[2..^2]), TextFormat.Bold);
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                paragraph.AddFormattedText(WrapLongTokens(part[1..^1])).Font.Name = "Consolas";
            else paragraph.AddText(WrapLongTokens(part));
        }
    }
}
