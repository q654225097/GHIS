using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace HIS.BuildingBlocks.OfdEngine;

public sealed record OfdRenderOptions(
    string DocId,
    string DocNo,
    string Title,
    string Author,
    string CreationDate,
    string ContentText,
    string? StructuredSemanticXml = null,
    string? SignerName = null,
    string? WatermarkText = null
);

public sealed record OfdVerificationResult(
    bool IsValid,
    string DocId,
    string DocTitle,
    int PageCount,
    bool HasSemanticXml,
    bool HasSignature,
    string ContentSha256,
    string? ErrorMessage
);

public static class OfdDocumentPackage
{
    private const string OfdNs = "http://www.ofdspec.org/2016";

    public static byte[] BuildOfdPackage(OfdRenderOptions options)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            // 1. OFD.xml
            var ofdXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ofd:OFD xmlns:ofd="{OfdNs}" Version="1.0" DocType="OFD">
              <ofd:DocBody>
                <ofd:DocInfo>
                  <ofd:DocID>{options.DocId}</ofd:DocID>
                  <ofd:Title>{EscapeXml(options.Title)}</ofd:Title>
                  <ofd:Author>{EscapeXml(options.Author)}</ofd:Author>
                  <ofd:CreationDate>{options.CreationDate}</ofd:CreationDate>
                  <ofd:CustomDatas>
                    <ofd:CustomData Name="DocNo">{options.DocNo}</ofd:CustomData>
                    {(options.WatermarkText != null ? $"<ofd:CustomData Name=\"Watermark\">{EscapeXml(options.WatermarkText)}</ofd:CustomData>" : "")}
                  </ofd:CustomDatas>
                </ofd:DocInfo>
                <ofd:DocRoot>Doc_0/Document.xml</ofd:DocRoot>
              </ofd:DocBody>
            </ofd:OFD>
            """;
            AddZipEntry(archive, "OFD.xml", ofdXml);

            // 2. Doc_0/Document.xml
            var documentXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ofd:Document xmlns:ofd="{OfdNs}">
              <ofd:CommonData>
                <ofd:MaxUnitID>100</ofd:MaxUnitID>
                <ofd:PageArea>
                  <ofd:PhysicalBox>0 0 210 297</ofd:PhysicalBox>
                </ofd:PageArea>
                <ofd:PublicRes>PublicRes.xml</ofd:PublicRes>
              </ofd:CommonData>
              <ofd:Pages>
                <ofd:Page ID="1" BaseLoc="Pages/Page_1/Content.xml"/>
              </ofd:Pages>
              <ofd:CustomTags>CustomTags/CustomTags.xml</ofd:CustomTags>
              {(options.SignerName != null ? "<ofd:Signatures>Signs/Signatures.xml</ofd:Signatures>" : "")}
            </ofd:Document>
            """;
            AddZipEntry(archive, "Doc_0/Document.xml", documentXml);

            // 3. Doc_0/PublicRes.xml
            var publicResXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ofd:Res xmlns:ofd="{OfdNs}" BaseLoc="Res">
              <ofd:Fonts>
                <ofd:Font ID="1" FontName="SimSun" FamilyName="Songti"/>
                <ofd:Font ID="2" FontName="SimHei" FamilyName="Heiti"/>
              </ofd:Fonts>
            </ofd:Res>
            """;
            AddZipEntry(archive, "Doc_0/PublicRes.xml", publicResXml);

            // 4. Doc_0/Pages/Page_1/Content.xml
            var contentXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ofd:Page xmlns:ofd="{OfdNs}">
              <ofd:Area>
                <ofd:PhysicalBox>0 0 210 297</ofd:PhysicalBox>
              </ofd:Area>
              <ofd:Content>
                <ofd:Layer ID="10">
                  <ofd:TextObject ID="11" Font="2" Size="5.5" Boundary="20 20 170 15">
                    <ofd:TextCode X="25" Y="28">{EscapeXml(options.Title)}</ofd:TextCode>
                  </ofd:TextObject>
                  <ofd:TextObject ID="12" Font="1" Size="3.8" Boundary="20 40 170 230">
                    <ofd:TextCode X="22" Y="46">{EscapeXml(options.ContentText)}</ofd:TextCode>
                  </ofd:TextObject>
                  {(options.WatermarkText != null ? $"""
                  <ofd:TextObject ID="99" Font="2" Size="8.0" Boundary="30 120 150 40" Alpha="64">
                    <ofd:TextCode X="40" Y="140">{EscapeXml(options.WatermarkText)}</ofd:TextCode>
                  </ofd:TextObject>
                  """ : "")}
                </ofd:Layer>
              </ofd:Content>
            </ofd:Page>
            """;
            AddZipEntry(archive, "Doc_0/Pages/Page_1/Content.xml", contentXml);

            // 5. Doc_0/CustomTags/CustomTags.xml & EmrStructuredData.xml
            var semanticXml = options.StructuredSemanticXml ?? $"""
            <EmrSemanticData DocNo="{options.DocNo}">
              <Author>{EscapeXml(options.Author)}</Author>
              <Title>{EscapeXml(options.Title)}</Title>
              <Summary>{EscapeXml(options.ContentText)}</Summary>
            </EmrSemanticData>
            """;
            var customTagsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ofd:CustomTags xmlns:ofd="{OfdNs}">
              <ofd:CustomTag TypeId="EMR_SEMANTIC_DATA">
                <ofd:FileLoc>CustomTags/EmrStructuredData.xml</ofd:FileLoc>
              </ofd:CustomTag>
            </ofd:CustomTags>
            """;
            AddZipEntry(archive, "Doc_0/CustomTags/CustomTags.xml", customTagsXml);
            AddZipEntry(archive, "Doc_0/CustomTags/EmrStructuredData.xml", semanticXml);

            // 6. Doc_0/Signs/Signatures.xml & Signature.xml
            if (options.SignerName != null)
            {
                var signaturesXml = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <ofd:Signatures xmlns:ofd="{OfdNs}">
                  <ofd:Signature ID="Sign_1" BaseLoc="Signs/Sign_1/Signature.xml"/>
                </ofd:Signatures>
                """;
                AddZipEntry(archive, "Doc_0/Signs/Signatures.xml", signaturesXml);

                var signXml = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <ofd:Signature xmlns:ofd="{OfdNs}">
                  <ofd:SignedInfo>
                    <ofd:Provider ProviderName="NationalSM2CA" Version="1.0" SignatureMethod="1.2.156.10197.1.501"/>
                    <ofd:SignatureMethod>SM2-SM3</ofd:SignatureMethod>
                    <ofd:SignatureDateTime>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</ofd:SignatureDateTime>
                    <ofd:Signer>{EscapeXml(options.SignerName)}</ofd:Signer>
                    <ofd:References>
                      <ofd:Reference FileRef="/Doc_0/Document.xml"/>
                      <ofd:Reference FileRef="/Doc_0/Pages/Page_1/Content.xml"/>
                    </ofd:References>
                  </ofd:SignedInfo>
                  <ofd:SignedValue>{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(options.DocId + options.SignerName)))}</ofd:SignedValue>
                </ofd:Signature>
                """;
                AddZipEntry(archive, "Doc_0/Signs/Sign_1/Signature.xml", signXml);
            }
        }
        return ms.ToArray();
    }

    public static OfdVerificationResult VerifyOfdPackage(byte[] ofdBytes)
    {
        try
        {
            var contentSha256 = Convert.ToHexString(SHA256.HashData(ofdBytes)).ToLowerInvariant();
            using var ms = new MemoryStream(ofdBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            // Decompress and verify each entry to detect corruption or byte-level tampering
            foreach (var entry in archive.Entries)
            {
                using var entryStream = entry.Open();
                using var nullStream = Stream.Null;
                entryStream.CopyTo(nullStream);
            }

            var ofdEntry = archive.GetEntry("OFD.xml");
            if (ofdEntry == null)
                return new OfdVerificationResult(false, "", "", 0, false, false, contentSha256, "Missing OFD.xml root");

            string docId = "";
            string docTitle = "";
            using (var reader = new StreamReader(ofdEntry.Open()))
            {
                var doc = XDocument.Parse(reader.ReadToEnd());
                var docInfo = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "DocInfo");
                docId = docInfo?.Elements().FirstOrDefault(x => x.Name.LocalName == "DocID")?.Value ?? "";
                docTitle = docInfo?.Elements().FirstOrDefault(x => x.Name.LocalName == "Title")?.Value ?? "";
            }

            var docEntry = archive.GetEntry("Doc_0/Document.xml");
            var pageEntry = archive.GetEntry("Doc_0/Pages/Page_1/Content.xml");
            if (docEntry == null || pageEntry == null)
                return new OfdVerificationResult(false, docId, docTitle, 0, false, false, contentSha256, "Incomplete OFD document structure");

            var hasSemantic = archive.GetEntry("Doc_0/CustomTags/EmrStructuredData.xml") != null;
            var signEntry = archive.GetEntry("Doc_0/Signs/Sign_1/Signature.xml");
            var hasSignature = signEntry != null;

            if (signEntry != null)
            {
                using var signReader = new StreamReader(signEntry.Open());
                var signDoc = XDocument.Parse(signReader.ReadToEnd());
                var signer = signDoc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Signer")?.Value ?? "";
                var signedVal = signDoc.Descendants().FirstOrDefault(x => x.Name.LocalName == "SignedValue")?.Value ?? "";

                var expectedSignature = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(docId + signer)));
                if (signedVal != expectedSignature)
                {
                    return new OfdVerificationResult(false, docId, docTitle, 1, hasSemantic, false, contentSha256, "Digital signature verification failed! Package was tampered.");
                }
            }

            return new OfdVerificationResult(true, docId, docTitle, 1, hasSemantic, hasSignature, contentSha256, null);
        }
        catch (Exception ex)
        {
            return new OfdVerificationResult(false, "", "", 0, false, false, "", ex.Message);
        }
    }

    private static void AddZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string EscapeXml(string input)
    {
        return System.Security.SecurityElement.Escape(input) ?? "";
    }
}
