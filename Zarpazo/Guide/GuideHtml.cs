using System.Net;
using System.Text;

namespace Zarpazo.Guide;

/// <summary>The guide as one HTML file with the pictures inside it, so it can be sent by mail or chat and opened anywhere.</summary>
internal static class GuideHtml
{
    public static string Build(string title, IEnumerable<GuideStep> steps)
    {
        string heading = WebUtility.HtmlEncode(title.Trim().Length > 0 ? title.Trim() : "Guía paso a paso");
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n<html lang=\"es\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append($"<title>{heading}</title>\n<style>\n");
        html.Append("body{font-family:'Segoe UI',Arial,sans-serif;color:#202124;max-width:900px;margin:0 auto;padding:32px 20px;line-height:1.5}\n");
        html.Append("h1{font-size:28px;margin:0 0 28px}\n");
        html.Append("section{margin:0 0 36px;break-inside:avoid}\n");
        html.Append("h2{font-size:16px;margin:0 0 6px;color:#d94441}\n");
        html.Append("p{margin:0 0 12px;white-space:pre-wrap}\n");
        html.Append("img{max-width:100%;border:1px solid #dadce0;border-radius:6px}\n");
        html.Append("</style>\n</head>\n<body>\n");
        html.Append($"<h1>{heading}</h1>\n");

        foreach (GuideStep step in steps)
        {
            html.Append($"<section>\n<h2>Paso {step.Number}</h2>\n");
            if (step.Text.Trim().Length > 0)
            {
                html.Append($"<p>{WebUtility.HtmlEncode(step.Text.Trim())}</p>\n");
            }
            html.Append($"<img alt=\"Paso {step.Number}\" src=\"data:image/png;base64,{Convert.ToBase64String(step.ReadPng())}\">\n</section>\n");
        }

        html.Append("</body>\n</html>\n");
        return html.ToString();
    }
}
