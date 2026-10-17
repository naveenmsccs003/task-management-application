using System.IO.Compression;
using System.Text;

namespace TaskManagement.Application.Attachments;

public enum FileCategory
{
    Image,
    Document,
    Video,
}

/// <summary>A file type the system can recognize. <see cref="Previewable"/> types may be shown inline once scanned.</summary>
public sealed record FileTypeInfo(string Code, FileCategory Category, string ContentType, IReadOnlyList<string> Extensions, bool Previewable);

/// <summary>
/// The supported file types (FR-ATT-02) and detection by content signature (FR-ATT-03). The browser's content type is
/// never trusted, and the extension must agree with the content. SVG and HTML are not in the catalog at all, so they
/// can never be enabled (script risk); text that looks like markup is refused.
/// </summary>
public static class FileTypes
{
    private const int HeaderLength = 8_192;

    public static IReadOnlyList<FileTypeInfo> All { get; } =
    [
        new("png", FileCategory.Image, "image/png", [".png"], true),
        new("jpeg", FileCategory.Image, "image/jpeg", [".jpg", ".jpeg"], true),
        new("webp", FileCategory.Image, "image/webp", [".webp"], true),
        new("gif", FileCategory.Image, "image/gif", [".gif"], true),
        new("pdf", FileCategory.Document, "application/pdf", [".pdf"], false),
        new("docx", FileCategory.Document, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", [".docx"], false),
        new("xlsx", FileCategory.Document, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [".xlsx"], false),
        new("pptx", FileCategory.Document, "application/vnd.openxmlformats-officedocument.presentationml.presentation", [".pptx"], false),
        new("txt", FileCategory.Document, "text/plain; charset=utf-8", [".txt", ".log"], false),
        new("md", FileCategory.Document, "text/markdown; charset=utf-8", [".md", ".markdown"], false),
        new("csv", FileCategory.Document, "text/csv; charset=utf-8", [".csv"], false),
        new("zip", FileCategory.Document, "application/zip", [".zip"], false),
        new("mp4", FileCategory.Video, "video/mp4", [".mp4", ".m4v"], true),
        new("webm", FileCategory.Video, "video/webm", [".webm"], true),
    ];

    public static FileTypeInfo? Find(string code) => All.FirstOrDefault(t => t.Code == code);

    /// <summary>
    /// Detects the type from <paramref name="content"/> (seekable; read from the start) and checks that
    /// <paramref name="fileName"/>'s extension belongs to it. Returns null when the content is not a supported type or
    /// the extension does not match, e.g. an executable renamed to <c>.pdf</c> (AC-ATT-2).
    /// </summary>
    public static FileTypeInfo? Detect(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        var header = new byte[HeaderLength];
        content.Position = 0;
        var length = content.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false);
        var bytes = header.AsSpan(0, length);
        content.Position = 0;

        var code = DetectBinary(bytes);
        if (code == "zip")
        {
            code = DetectZipContainer(content);
            content.Position = 0;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (code is null)
        {
            // No binary signature: only plain UTF-8 text qualifies, and the extension says which text type it is.
            if (!IsPlainText(bytes, complete: length < HeaderLength))
            {
                return null;
            }

            return All.FirstOrDefault(t => t.Category == FileCategory.Document && t.ContentType.StartsWith("text/", StringComparison.Ordinal)
                && t.Extensions.Contains(extension));
        }

        var type = Find(code);
        return type is not null && type.Extensions.Contains(extension) ? type : null;
    }

    private static string? DetectBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "png";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "jpeg";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return "gif";
        }

        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "webp";
        }

        if (bytes.StartsWith("%PDF-"u8))
        {
            return "pdf";
        }

        if (bytes.StartsWith("PK\x03\x04"u8) || bytes.StartsWith("PK\x05\x06"u8))
        {
            return "zip";
        }

        // ISO base media: a box size, then "ftyp". QuickTime ("qt  ") is a different format.
        if (bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8) && !bytes[8..12].SequenceEqual("qt  "u8))
        {
            return "mp4";
        }

        // EBML header whose DocType is "webm" (Matroska files say "matroska").
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]) && bytes[..Math.Min(bytes.Length, 64)].IndexOf("webm"u8) >= 0)
        {
            return "webm";
        }

        return null;
    }

    /// <summary>Office Open XML files are ZIP packages; their parts tell Word, Excel and PowerPoint apart.</summary>
    private static string? DetectZipContainer(Stream content)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            if (names.Contains("[Content_Types].xml"))
            {
                if (names.Exists(n => n.StartsWith("word/", StringComparison.Ordinal)))
                {
                    return "docx";
                }

                if (names.Exists(n => n.StartsWith("xl/", StringComparison.Ordinal)))
                {
                    return "xlsx";
                }

                if (names.Exists(n => n.StartsWith("ppt/", StringComparison.Ordinal)))
                {
                    return "pptx";
                }
            }

            return "zip";
        }
        catch (InvalidDataException)
        {
            return null; // starts like a ZIP but is not a readable one
        }
    }

    /// <summary>Valid UTF-8 without NUL or other control characters (except tab, CR, LF, FF), and not HTML/SVG/XML markup.</summary>
    private static bool IsPlainText(ReadOnlySpan<byte> bytes, bool complete)
    {
        if (bytes.IsEmpty)
        {
            return false;
        }

        var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetDecoder();
        var chars = new char[bytes.Length];
        int count;
        try
        {
            // The header may end in the middle of a character unless it is the whole file.
            count = decoder.GetChars(bytes, chars, flush: complete);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var text = chars.AsSpan(0, count);
        foreach (var c in text)
        {
            if (char.IsControl(c) && c is not ('\t' or '\r' or '\n' or '\f'))
            {
                return false;
            }
        }

        var start = text.TrimStart().TrimStart('﻿').TrimStart();
        string[] markup = ["<!doctype", "<html", "<svg", "<?xml", "<script", "<head", "<body", "<iframe"];
        foreach (var marker in markup)
        {
            if (start.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The original name is metadata only (FR-ATT-04): path parts, control and reserved characters are removed and the
    /// length is capped, keeping the extension.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (!char.IsControl(c) && c is not ('<' or '>' or ':' or '"' or '|' or '?' or '*'))
            {
                builder.Append(c);
            }
        }

        name = builder.ToString().Trim().Trim('.').Trim();
        if (name.Length == 0)
        {
            return "file";
        }

        const int max = Domain.Files.Attachment.FileNameMaxLength;
        if (name.Length > max)
        {
            var extension = Path.GetExtension(name);
            extension = extension.Length > 20 ? string.Empty : extension;
            var cut = max - extension.Length;
            if (char.IsHighSurrogate(name[cut - 1]))
            {
                cut--; // do not split a character made of two UTF-16 units
            }

            name = name[..cut] + extension;
        }

        return name;
    }
}
