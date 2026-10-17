using System.IO.Compression;
using System.Text;
using TaskManagement.Application.Attachments;

namespace TaskManagement.Application.Tests.Attachments;

public sealed class FileTypesTests
{
    private static string? Detect(byte[] content, string fileName)
    {
        using var stream = new MemoryStream(content);
        return FileTypes.Detect(stream, fileName)?.Code;
    }

    private static byte[] Bytes(params int[] values) => values.Select(v => (byte)v).ToArray();

    private static byte[] Zip(params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
                writer.Write("x");
            }
        }

        return buffer.ToArray();
    }

    public static TheoryData<byte[], string, string?> Cases => new()
    {
        { Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0), "a.png", "png" },
        { Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10), "a.JPG", "jpeg" },
        { Encoding.ASCII.GetBytes("GIF89a.."), "a.gif", "gif" },
        { Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 "), "a.webp", "webp" },
        { Encoding.ASCII.GetBytes("%PDF-1.4\n"), "a.pdf", "pdf" },
        { [0, 0, 0, 0x18, .. Encoding.ASCII.GetBytes("ftypisom"), 0, 0, 0, 0], "a.mp4", "mp4" },
        { [0, 0, 0, 0x14, .. Encoding.ASCII.GetBytes("ftypqt  "), 0, 0, 0, 0], "a.mp4", null }, // QuickTime is not MP4
        { [0x1A, 0x45, 0xDF, 0xA3, 0x42, 0x82, 0x84, .. Encoding.ASCII.GetBytes("webm")], "a.webm", "webm" },
        { [0x1A, 0x45, 0xDF, 0xA3, 0x42, 0x82, 0x88, .. Encoding.ASCII.GetBytes("matroska")], "a.webm", null },
        { Zip("[Content_Types].xml", "word/document.xml"), "a.docx", "docx" },
        { Zip("[Content_Types].xml", "xl/workbook.xml"), "a.xlsx", "xlsx" },
        { Zip("[Content_Types].xml", "ppt/presentation.xml"), "a.pptx", "pptx" },
        { Zip("readme.txt"), "a.zip", "zip" },
        { Zip("readme.txt"), "a.docx", null }, // a plain ZIP renamed to .docx
        { Encoding.ASCII.GetBytes("PK\x03\x04not really a zip"), "a.zip", null },
        { Encoding.UTF8.GetBytes("name,amount\nÄnne,3\n"), "a.csv", "csv" },
        { Encoding.UTF8.GetBytes("\uFEFF# Title\n"), "a.md", "md" },
        { Encoding.UTF8.GetBytes("plain words"), "a.txt", "txt" },
        { Encoding.UTF8.GetBytes("plain words"), "a.exe", null },
        { Encoding.UTF8.GetBytes("  <svg onload=alert(1)>"), "a.txt", null },
        { Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg/>"), "a.md", null },
        { Bytes(0x4D, 0x5A, 0x90, 0x00), "a.pdf", null }, // AC-ATT-2: Windows executable
        { Bytes(0x7F, 0x45, 0x4C, 0x46), "a.txt", null }, // ELF executable
        { Bytes(0xC3, 0x28), "a.txt", null }, // invalid UTF-8
        { Encoding.ASCII.GetBytes("%PDF-1.4"), "a.png", null }, // real PDF, wrong extension
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Types_are_detected_by_content_and_must_match_the_extension(byte[] content, string fileName, string? expected) =>
        Assert.Equal(expected, Detect(content, fileName));

    [Fact]
    public void A_multi_byte_character_cut_at_the_header_boundary_is_still_text()
    {
        var text = new string('a', 8_191) + "é and more text after the first 8 KB";
        Assert.Equal("txt", Detect(Encoding.UTF8.GetBytes(text), "long.txt"));
    }

    [Fact]
    public void SVG_and_HTML_are_not_in_the_catalog_so_they_can_never_be_enabled() =>
        Assert.DoesNotContain(FileTypes.All, t => t.Code is "svg" or "html" || t.Extensions.Any(e => e is ".svg" or ".html" or ".htm"));

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Users\\me\\report.pdf", "report.pdf")]
    [InlineData("a<b>:\"c\"|?*.txt", "abc.txt")]
    [InlineData("line\nbreak.txt", "linebreak.txt")]
    [InlineData("...", "file")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("rapport été 😀.pdf", "rapport été 😀.pdf")]
    public void File_names_are_sanitized(string? input, string expected) => Assert.Equal(expected, FileTypes.SanitizeFileName(input));

    [Fact]
    public void Long_file_names_keep_their_extension()
    {
        var name = FileTypes.SanitizeFileName(new string('x', 300) + ".pdf");
        Assert.Equal(255, name.Length);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
    }
}
