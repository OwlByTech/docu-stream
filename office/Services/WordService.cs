using DocumentFormat.OpenXml.Drawing.Pictures;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text.RegularExpressions;
using Grpc.Core;

namespace office.Services;

public class WordService : Word.WordBase
{
    private readonly ILogger<WordService> _logger;
    public WordService(ILogger<WordService> logger)
    {
        _logger = logger;
    }

    private void replaceStringValues(IEnumerable<Paragraph> elements, List<DocuStringValues> stringValues)
    {

        var textElements = elements
            .SelectMany(e => e.Elements<Run>())
            .SelectMany(r => r.Elements<Text>());

        foreach (var text in textElements)
        {
            foreach (var stringValue in stringValues)
            {
                var regexPattern = new Regex(@"\{\{\s*" + Regex.Escape(stringValue.Key) + @"\s*\}\}", RegexOptions.Compiled);
                text.Text = regexPattern.Replace(text.Text, match => stringValue.Value);
            }
        }

    }

    public override Task<WordApplyRes> Apply(WordApplyReq req, ServerCallContext context)
    {
        // TODO: Change test file for streaming by grpc
        using (WordprocessingDocument wordDocument = WordprocessingDocument.Open("./test/Template.docx", true))
        {

            MainDocumentPart? mainDocumentPart = wordDocument.MainDocumentPart;
            if (mainDocumentPart == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "main document part required."));
            }

            Document document = mainDocumentPart.Document;
            if (document == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "document required."));
            }

            foreach (var headerPart in mainDocumentPart.HeaderParts)
            {
                var header = headerPart.Header;
                if (header == null)
                {
                    continue;
                }

                replaceStringValues(header.Elements<Paragraph>(), req.Header.ToList());
            }

            Body? body = document.Body;
            if (body == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "body required."));
            }

            replaceStringValues(body.Elements<Paragraph>(), req.Body.ToList());

            document.Save();
        }
        return Task.FromResult(new WordApplyRes { });
    }
}

