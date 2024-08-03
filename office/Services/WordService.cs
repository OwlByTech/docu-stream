using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text.RegularExpressions;
using Grpc.Core;
using MimeDetective;

namespace office.Services;

public class WordService : Word.WordBase
{
    private readonly ILogger<WordService> _logger;
    public WordService(ILogger<WordService> logger)
    {
        _logger = logger;
    }

    private void replaceStringValues(IEnumerable<Paragraph> elements, List<DocuValues> stringValues)
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

    public override async Task Apply(IAsyncStreamReader<WordApplyReq> reqStream, IServerStreamWriter<WordApplyRes> resStream, ServerCallContext ctx)
    {
        var bodyValues = new List<DocuValues>();
        var headerValues = new List<DocuValues>();
        MemoryStream chunks = new MemoryStream();

        await foreach (var req in reqStream.ReadAllAsync())
        {
            switch (req.RequestCase)
            {
                case WordApplyReq.RequestOneofCase.Word:
                    bodyValues.AddRange(req.Word.Body);
                    headerValues.AddRange(req.Word.Header);
                    break;

                case WordApplyReq.RequestOneofCase.Docu:
                    var chunk = req.Docu.Chunk;
                    if (chunk.Length <= 0)
                    {
                        continue;
                    }

                    await chunks.WriteAsync(chunk.ToByteArray(), 0, chunk.Length);
                    break;

                default:
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid request type"));
            }
        }

        long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var Inspector = new ContentInspectorBuilder()
        {
            Definitions = MimeDetective.Definitions.Default.All()
        }.Build();

        var Results = Inspector.Inspect(chunks.ToArray());
        var extension = Results.ByFileExtension().First().Extension;

        if (extension != "docx")
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid file type: {extension}. Only '.docx' files are allowed."));
        }

        string docuPath = $"/tmp/{unixTime}.{extension}";
        using (var fileStream = new FileStream(docuPath, FileMode.Create, FileAccess.Write))
        {
            chunks.WriteTo(fileStream);
        }
        chunks.SetLength(0);
        chunks.Position = 0;

        using (WordprocessingDocument wordDocument = WordprocessingDocument.Open(docuPath, true))
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
                if (header == null) { continue; }
                replaceStringValues(header.Elements<Paragraph>(), bodyValues);
            }

            Body? body = document.Body;
            if (body == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "body required."));
            }

            replaceStringValues(body.Elements<Paragraph>(), headerValues);

            document.Save();
        }

        using (var stream = new FileStream(docuPath, FileMode.Open, FileAccess.Read))
        {
            stream.CopyTo(chunks);
        }

        if (!File.Exists(docuPath))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "File was not created successfully."));
        }

        File.Delete(docuPath);

        await resStream.WriteAsync(new WordApplyRes
        {
            Docu = new DocuChunk
            {
                Chunk = Google.Protobuf.ByteString.CopyFrom(chunks.ToArray())
            }
        });
    }
}

