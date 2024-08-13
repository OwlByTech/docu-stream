using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Drawing.Pictures;
using System.Text.RegularExpressions;
using Grpc.Core;
using MimeDetective;
using Google.Protobuf.Collections;
using Google.Protobuf;

namespace office.Services;

public class WordService : Word.WordBase
{
    private readonly ILogger<WordService> _logger;
    public WordService(ILogger<WordService> logger)
    {
        _logger = logger;
    }

    private void replaceTextValues(IEnumerable<Paragraph> elements, IEnumerable<DocuValue> textValues)
    {
        var textElements = elements
            .SelectMany(e => e.Elements<Run>())
            .SelectMany(r => r.Elements<Text>());

        foreach (var text in textElements)
        {
            foreach (var stringValue in textValues)
            {
                var regexPattern = new Regex(@"\{\{\s*\b" + Regex.Escape(stringValue.Key) + @"\b\s*\}\}", RegexOptions.Compiled);
                text.Text = regexPattern.Replace(text.Text, match => stringValue.Value);
            }
        }
    }

    private void replaceImageValues(IEnumerable<Drawing> drawings, OpenXmlPart part, IEnumerable<DocuValue> imageValues, List<MemoryStream> attachFiles)
    {
        foreach (var drawing in drawings)
        {
            var drawParts = drawing.Descendants<NonVisualDrawingProperties>().FirstOrDefault();
            if (drawParts == null)
            {
                continue;
            }

            var imageDescription = drawParts?.Description?.Value;
            if (imageDescription == null)
            {
                continue;
            }

            var imageFound = imageValues.Where(h => "{{" + h.Key + "}}" == imageDescription).ToList();
            if (imageFound.Count() == 0)
            {
                continue;
            }

            var blipFill = drawing.Descendants<BlipFill>().FirstOrDefault();
            var blipEmbedValue = blipFill?.Blip?.Embed?.Value;
            if (blipEmbedValue == null) { continue; }

            var imagePart = part.GetPartById(blipEmbedValue) as ImagePart;
            if (imagePart == null) { continue; }

            var imageIndex = int.Parse(imageFound[0].Value);
            using (var image = attachFiles[imageIndex])
            {
                image.Position = 0;
                imagePart.FeedData(image);
            }
        }

    }

    public override async Task Apply(IAsyncStreamReader<WordApplyReq> reqStream, IServerStreamWriter<WordApplyRes> resStream, ServerCallContext ctx)
    {
        var bodyValues = new List<DocuValue>();
        var headerValues = new List<DocuValue>();
        List<MemoryStream> attachFiles = new List<MemoryStream>();

        await foreach (var req in reqStream.ReadAllAsync())
        {
            switch (req.RequestCase)
            {
                case WordApplyReq.RequestOneofCase.Word:
                    bodyValues.AddRange(req.Word.Body);
                    headerValues.AddRange(req.Word.Header);
                    break;

                case WordApplyReq.RequestOneofCase.Docu:
                    var chunks = req.Docu.Chunks;
                    if (attachFiles.Count != chunks.Count)
                    {
                        for (int i = 0; i < chunks.Count; i++)
                        {
                            attachFiles.Add(new MemoryStream());
                        }
                    }

                    for (int i = 0; i < chunks.Count; i++)
                    {
                        if (chunks[i].Length <= 0)
                        {
                            continue;
                        }

                        await attachFiles[i].WriteAsync(chunks[i].ToByteArray(), 0, chunks[i].Length);
                    }
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

        var Results = Inspector.Inspect(attachFiles[0].ToArray());
        var extension = Results.ByFileExtension().First().Extension;

        if (extension != "docx")
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid file type: {extension}. Only '.docx' files are allowed."));
        }

        string docuPath = $"/tmp/{unixTime}.{extension}";
        using (var fileStream = new FileStream(docuPath, FileMode.Create, FileAccess.Write))
        {
            attachFiles[0].WriteTo(fileStream);
        }
        attachFiles[0].SetLength(0);
        attachFiles[0].Position = 0;

        using (WordprocessingDocument wordDocument = WordprocessingDocument.Open(docuPath, true))
        {
            MainDocumentPart? mainDocumentPart = wordDocument.MainDocumentPart;
            if (mainDocumentPart == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "main document part required."));
            }



            var headerImages = headerValues.Where(h => h.Type == DocuValueType.Image);
            var headerTexts = headerValues.Where(h => h.Type == DocuValueType.Text);

            foreach (var headerPart in mainDocumentPart.HeaderParts)
            {
                var header = headerPart.Header;
                if (header == null) { continue; }

                replaceTextValues(header.Elements<Paragraph>(), headerTexts);
                replaceImageValues(headerPart.Header.Descendants<Drawing>(), headerPart, headerImages, attachFiles);
            }

            Document document = mainDocumentPart.Document;
            if (document == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "document required."));
            }

            Body? body = document.Body;
            if (body == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "body required."));
            }

            var bodyTexts = bodyValues.Where(b => b.Type == DocuValueType.Text);
            var bodyImages = bodyValues.Where(b => b.Type == DocuValueType.Image);
            replaceTextValues(body.Elements<Paragraph>(), bodyTexts);
            replaceImageValues(body.Descendants<Drawing>(), mainDocumentPart, bodyImages, attachFiles);

            document.Save();
        }

        using (var stream = new FileStream(docuPath, FileMode.Open, FileAccess.Read))
        {
            stream.CopyTo(attachFiles[0]);
        }

        if (!File.Exists(docuPath))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "File was not created successfully."));
        }

        File.Delete(docuPath);

        RepeatedField<ByteString> chunksRes = new RepeatedField<ByteString>();
        var docu = new DocuChunk();
        docu.Chunks.Add(ByteString.CopyFrom(attachFiles[0].ToArray()));

        await resStream.WriteAsync(new WordApplyRes
        {
            Docu = docu
        });
    }
}

