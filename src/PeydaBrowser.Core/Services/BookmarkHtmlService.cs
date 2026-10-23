using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Security;

namespace PeydaBrowser.Core.Services;

public sealed record ImportedBookmark(string Title, string Url, IReadOnlyList<string> FolderPath, DateTimeOffset? AddedUtc);
public sealed record BookmarkImportResult(IReadOnlyList<ImportedBookmark> Bookmarks, int SkippedCount);

/// <summary>
/// Import/export for the de-facto standard NETSCAPE-Bookmark-file-1 HTML format
/// used by Chrome, Edge, Firefox and Brave. Only http/https links are imported;
/// anything else (javascript:, data:, ...) is skipped and counted.
/// </summary>
public static class BookmarkHtmlService
{
    public static string Export(IReadOnlyList<Bookmark> bookmarks, IReadOnlyList<BookmarkFolder> folders)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
        sb.AppendLine("<!-- Exported from Peyda Browser. -->");
        sb.AppendLine("""<META HTTP-EQUIV="Content-Type" CONTENT="text/html; charset=UTF-8">""");
        sb.AppendLine("<TITLE>Bookmarks</TITLE>");
        sb.AppendLine("<H1>Bookmarks</H1>");
        sb.AppendLine("<DL><p>");
        WriteLevel(sb, null, bookmarks, folders, indent: 1);
        sb.AppendLine("</DL><p>");
        return sb.ToString();
    }

    private static void WriteLevel(StringBuilder sb, string? folderId,
        IReadOnlyList<Bookmark> bookmarks, IReadOnlyList<BookmarkFolder> folders, int indent)
    {
        var pad = new string(' ', indent * 4);
        foreach (var folder in folders.Where(f => f.ParentId == folderId).OrderBy(f => f.SortOrder).ThenBy(f => f.Name))
        {
            sb.Append(pad).Append("<DT><H3 ADD_DATE=\"").Append(folder.CreatedUtc.ToUnixTimeSeconds()).Append("\">")
              .Append(WebUtility.HtmlEncode(folder.Name)).AppendLine("</H3>");
            sb.Append(pad).AppendLine("<DL><p>");
            WriteLevel(sb, folder.Id, bookmarks, folders, indent + 1);
            sb.Append(pad).AppendLine("</DL><p>");
        }
        foreach (var bookmark in bookmarks.Where(b => b.FolderId == folderId).OrderBy(b => b.SortOrder).ThenBy(b => b.Title))
        {
            sb.Append(pad).Append("<DT><A HREF=\"").Append(WebUtility.HtmlEncode(bookmark.Url))
              .Append("\" ADD_DATE=\"").Append(bookmark.CreatedUtc.ToUnixTimeSeconds()).Append("\">")
              .Append(WebUtility.HtmlEncode(bookmark.Title)).AppendLine("</A>");
        }
    }

    private static readonly Regex TagRegex = new(
        @"<DT>\s*<H3[^>]*>(?<folder>.*?)</H3>|<DT>\s*<A\s+[^>]*HREF=""(?<url>[^""]*)""[^>]*>(?<title>.*?)</A>|<DL[^>]*>|</DL\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex AddDateRegex = new(
        @"<DT>\s*<A\s+[^>]*ADD_DATE=""(?<date>\d+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static BookmarkImportResult Import(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var results = new List<ImportedBookmark>();
        var skipped = 0;
        var folderStack = new List<string>();
        string? pendingFolder = null;

        foreach (Match match in TagRegex.Matches(html))
        {
            var token = match.Value;
            if (token.StartsWith("<DL", StringComparison.OrdinalIgnoreCase))
            {
                if (pendingFolder is not null)
                {
                    folderStack.Add(pendingFolder);
                    pendingFolder = null;
                }
                else if (folderStack.Count > 0)
                {
                    folderStack.Add(folderStack[^1]); // anonymous nested DL keeps current path
                }
            }
            else if (token.StartsWith("</DL", StringComparison.OrdinalIgnoreCase))
            {
                if (folderStack.Count > 0) folderStack.RemoveAt(folderStack.Count - 1);
                pendingFolder = null;
            }
            else if (match.Groups["folder"].Success)
            {
                pendingFolder = Clean(match.Groups["folder"].Value);
            }
            else if (match.Groups["url"].Success)
            {
                var url = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim());
                var title = Clean(match.Groups["title"].Value);
                if (!UrlValidator.IsValidHttpUrl(url, out _))
                {
                    skipped++;
                    continue;
                }
                DateTimeOffset? added = null;
                var dateMatch = AddDateRegex.Match(html, Math.Max(0, match.Index - 4));
                if (dateMatch.Success && long.TryParse(dateMatch.Groups["date"].Value, out var seconds))
                    added = DateTimeOffset.FromUnixTimeSeconds(seconds);
                results.Add(new ImportedBookmark(
                    string.IsNullOrWhiteSpace(title) ? url : title,
                    url,
                    folderStack.ToList(),
                    added));
            }
        }

        return new BookmarkImportResult(results, skipped);
    }

    private static string Clean(string raw)
    {
        var withoutTags = Regex.Replace(raw, "<[^>]*>", string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }
}
