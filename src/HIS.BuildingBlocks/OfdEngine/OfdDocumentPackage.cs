using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace HIS.BuildingBlocks.OfdEngine;

public static class OfdDocumentPackage
{
	private const string OfdNs = "http://www.ofdspec.org/2016";
	private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	public static byte[] BuildOfdPackage(OfdRenderOptions options)
	{
		string value = options.PageWidthMm.ToString("0.##", CultureInfo.InvariantCulture);
		string value2 = options.PageHeightMm.ToString("0.##", CultureInfo.InvariantCulture);
		string value3 = options.MarginLeftMm.ToString("0.##", CultureInfo.InvariantCulture);
		string value4 = options.MarginTopMm.ToString("0.##", CultureInfo.InvariantCulture);
		string value5 = Math.Max(60m, options.PageWidthMm - options.MarginLeftMm - options.MarginRightMm).ToString("0.##", CultureInfo.InvariantCulture);
		string value6 = Math.Max(80m, options.PageHeightMm - options.MarginTopMm - options.MarginBottomMm - 35m).ToString("0.##", CultureInfo.InvariantCulture);
		string value7 = (options.MarginTopMm + 22m).ToString("0.##", CultureInfo.InvariantCulture);
		string value8 = Math.Max(40m, options.PageHeightMm - options.MarginBottomMm - 12m).ToString("0.##", CultureInfo.InvariantCulture);
		using MemoryStream memoryStream = new MemoryStream();
		using (ZipArchive archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
		{
			string content = BuildRootXml(options);
			AddZipEntry(archive, "OFD.xml", content);
			string content2 = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Document xmlns:ofd=\"{"http://www.ofdspec.org/2016"}\">\n  <ofd:CommonData>\n    <ofd:MaxUnitID>120</ofd:MaxUnitID>\n    <ofd:PageArea>\n      <ofd:PhysicalBox>0 0 {value} {value2}</ofd:PhysicalBox>\n    </ofd:PageArea>\n    <ofd:PublicRes>PublicRes.xml</ofd:PublicRes>\n  </ofd:CommonData>\n  <ofd:Pages>\n    <ofd:Page ID=\"1\" BaseLoc=\"Pages/Page_1/Content.xml\"/>\n  </ofd:Pages>\n  <ofd:CustomTags>CustomTags/CustomTags.xml</ofd:CustomTags>\n  {((options.SignerName != null) ? "<ofd:Signatures>Signs/Signatures.xml</ofd:Signatures>" : "")}\n</ofd:Document>";
			byte[] documentBytes = Utf8WithoutBom.GetBytes(content2);
			AddZipEntry(archive, "Doc_0/Document.xml", content2, documentBytes);
			string content3 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Res xmlns:ofd=\"http://www.ofdspec.org/2016\" BaseLoc=\"Res\">\n  <ofd:Fonts>\n    <ofd:Font ID=\"1\" FontName=\"SimSun\" FamilyName=\"Songti\"/>\n    <ofd:Font ID=\"2\" FontName=\"SimHei\" FamilyName=\"Heiti\"/>\n  </ofd:Fonts>\n</ofd:Res>";
			AddZipEntry(archive, "Doc_0/PublicRes.xml", content3);
			string input = (string.IsNullOrWhiteSpace(options.HospitalHeaderText) ? options.Title : (options.HospitalHeaderText + " · " + options.Title));
			string input2 = (string.IsNullOrWhiteSpace(options.PatientBannerText) ? options.ContentText : (options.PatientBannerText + "\n" + options.ContentText));
			string content4 = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Page xmlns:ofd=\"{"http://www.ofdspec.org/2016"}\">\n  <ofd:Area>\n    <ofd:PhysicalBox>0 0 {value} {value2}</ofd:PhysicalBox>\n  </ofd:Area>\n  <ofd:Content>\n    <ofd:Layer ID=\"10\">\n      <ofd:TextObject ID=\"11\" Font=\"2\" Size=\"5.5\" Boundary=\"{value3} {value4} {value5} 16\">\n        <ofd:TextCode X=\"2\" Y=\"8\">{EscapeXml(input)}</ofd:TextCode>\n      </ofd:TextObject>\n      <ofd:TextObject ID=\"12\" Font=\"1\" Size=\"3.8\" Boundary=\"{value3} {value7} {value5} {value6}\">\n        <ofd:TextCode X=\"2\" Y=\"6\">{EscapeXml(input2)}</ofd:TextCode>\n      </ofd:TextObject>\n      {((options.SignSlotsSummary != null) ? $"<ofd:TextObject ID=\"15\" Font=\"1\" Size=\"3.5\" Boundary=\"{value3} {value8} {value5} 10\">\n  <ofd:TextCode X=\"2\" Y=\"5\">{EscapeXml(options.SignSlotsSummary)}</ofd:TextCode>\n</ofd:TextObject>" : "")}\n      {((options.FooterText != null) ? $"<ofd:TextObject ID=\"16\" Font=\"1\" Size=\"3.0\" Boundary=\"{value3} {value8} {value5} 8\">\n  <ofd:TextCode X=\"2\" Y=\"7\">{EscapeXml(options.FooterText)}</ofd:TextCode>\n</ofd:TextObject>" : "")}\n      {((options.WatermarkText != null) ? ("<ofd:TextObject ID=\"99\" Font=\"2\" Size=\"8.0\" Boundary=\"30 120 150 40\" Alpha=\"64\">\n  <ofd:TextCode X=\"10\" Y=\"20\">" + EscapeXml(options.WatermarkText) + "</ofd:TextCode>\n</ofd:TextObject>") : "")}\n    </ofd:Layer>\n  </ofd:Content>\n</ofd:Page>";
			byte[] contentBytes = Utf8WithoutBom.GetBytes(content4);
			AddZipEntry(archive, "Doc_0/Pages/Page_1/Content.xml", content4, contentBytes);
			string content5 = options.StructuredSemanticXml ?? $"<EmrSemanticData DocNo=\"{EscapeXml(options.DocNo)}\">\n  <Author>{EscapeXml(options.Author)}</Author>\n  <Title>{EscapeXml(options.Title)}</Title>\n  <PageSize>{EscapeXml(options.PageSizeCode)}</PageSize>\n  <Summary>{EscapeXml(options.ContentText)}</Summary>\n</EmrSemanticData>";
			string content6 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:CustomTags xmlns:ofd=\"http://www.ofdspec.org/2016\">\n  <ofd:CustomTag TypeId=\"EMR_SEMANTIC_DATA\">\n    <ofd:FileLoc>CustomTags/EmrStructuredData.xml</ofd:FileLoc>\n  </ofd:CustomTag>\n</ofd:CustomTags>";
			AddZipEntry(archive, "Doc_0/CustomTags/CustomTags.xml", content6);
			byte[] semanticBytes = Utf8WithoutBom.GetBytes(content5);
			AddZipEntry(archive, "Doc_0/CustomTags/EmrStructuredData.xml", content5, semanticBytes);
			if (options.SignerName != null)
			{
				string documentHash = ComputeEntrySha256(documentBytes);
				string contentHash = ComputeEntrySha256(contentBytes);
				string semanticHash = ComputeEntrySha256(semanticBytes);
				byte[] signedValue = ComputeSignedValue(options.DocId, documentBytes, contentBytes, semanticBytes);
				string content7 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Signatures xmlns:ofd=\"http://www.ofdspec.org/2016\">\n  <ofd:Signature ID=\"Sign_1\" BaseLoc=\"Signs/Sign_1/Signature.xml\"/>\n</ofd:Signatures>";
				AddZipEntry(archive, "Doc_0/Signs/Signatures.xml", content7);
				string content8 = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Signature xmlns:ofd=\"{"http://www.ofdspec.org/2016"}\">\n  <ofd:SignedInfo>\n    <ofd:Provider ProviderName=\"NationalSM2CA\" Version=\"1.0\" SignatureMethod=\"1.2.156.10197.1.501\"/>\n    <ofd:SignatureMethod>SM2-SM3</ofd:SignatureMethod>\n    <ofd:SignatureDateTime>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</ofd:SignatureDateTime>\n    <ofd:Signer>{EscapeXml(options.SignerName)}</ofd:Signer>\n    <ofd:DocId>{EscapeXml(options.DocId)}</ofd:DocId>\n    <ofd:References>\n      <ofd:Reference FileRef=\"/Doc_0/Document.xml\" DigestMethod=\"SHA256\" DigestValue=\"{documentHash}\"/>\n      <ofd:Reference FileRef=\"/Doc_0/Pages/Page_1/Content.xml\" DigestMethod=\"SHA256\" DigestValue=\"{contentHash}\"/>\n      <ofd:Reference FileRef=\"/Doc_0/CustomTags/EmrStructuredData.xml\" DigestMethod=\"SHA256\" DigestValue=\"{semanticHash}\"/>\n    </ofd:References>\n  </ofd:SignedInfo>\n  <ofd:SignedValue>{Convert.ToBase64String(signedValue)}</ofd:SignedValue>\n</ofd:Signature>";
				AddZipEntry(archive, "Doc_0/Signs/Sign_1/Signature.xml", content8);
			}
		}
		return memoryStream.ToArray();
	}

	public static OfdVerificationResult VerifyOfdPackage(byte[] ofdBytes)
	{
		try
		{
			string contentSha = Convert.ToHexString(SHA256.HashData(ofdBytes)).ToLowerInvariant();
			using MemoryStream stream = new MemoryStream(ofdBytes);
			using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
			foreach (ZipArchiveEntry entry5 in zipArchive.Entries)
			{
				using Stream stream2 = entry5.Open();
				using Stream destination = Stream.Null;
				stream2.CopyTo(destination);
			}
			ZipArchiveEntry entry = zipArchive.GetEntry("OFD.xml");
			if (entry == null)
			{
				return new OfdVerificationResult(IsValid: false, "", "", 0, HasSemanticXml: false, HasSignature: false, contentSha, "Missing OFD.xml root");
			}
			string text = "";
			string docTitle = "";
			using (StreamReader streamReader = new StreamReader(entry.Open()))
			{
				XDocument xDocument = XDocument.Parse(streamReader.ReadToEnd());
				XElement xElement = xDocument.Descendants().FirstOrDefault((XElement x) => x.Name.LocalName == "DocInfo");
				text = xElement?.Elements().FirstOrDefault((XElement x) => x.Name.LocalName == "DocID")?.Value ?? "";
				docTitle = xElement?.Elements().FirstOrDefault((XElement x) => x.Name.LocalName == "Title")?.Value ?? "";
			}
			ZipArchiveEntry entry2 = zipArchive.GetEntry("Doc_0/Document.xml");
			ZipArchiveEntry entry3 = zipArchive.GetEntry("Doc_0/Pages/Page_1/Content.xml");
			if (entry2 == null || entry3 == null)
			{
				return new OfdVerificationResult(IsValid: false, text, docTitle, 0, HasSemanticXml: false, HasSignature: false, contentSha, "Incomplete OFD document structure");
			}
			bool hasSemanticXml = zipArchive.GetEntry("Doc_0/CustomTags/EmrStructuredData.xml") != null;
			ZipArchiveEntry entry4 = zipArchive.GetEntry("Doc_0/Signs/Sign_1/Signature.xml");
			bool hasSignature = entry4 != null;
			if (entry4 != null)
			{
				using StreamReader streamReader2 = new StreamReader(entry4.Open());
				XDocument xDocument2 = XDocument.Parse(streamReader2.ReadToEnd());
				string text2 = xDocument2.Descendants().FirstOrDefault((XElement x) => x.Name.LocalName == "Signer")?.Value ?? "";
				string text3 = xDocument2.Descendants().FirstOrDefault((XElement x) => x.Name.LocalName == "SignedValue")?.Value ?? "";
				string signedDocId = xDocument2.Descendants().FirstOrDefault((XElement x) => x.Name.LocalName == "DocId")?.Value ?? "";
				if (!string.Equals(signedDocId, text, StringComparison.Ordinal))
					return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "Signed document identity does not match OFD.xml");
				var expectedDigests = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["/Doc_0/Document.xml"] = "",
					["/Doc_0/Pages/Page_1/Content.xml"] = "",
					["/Doc_0/CustomTags/EmrStructuredData.xml"] = ""
				};
				foreach (XElement reference in xDocument2.Descendants().Where(x => x.Name.LocalName == "Reference"))
				{
					string? fileRef = reference.Attribute("FileRef")?.Value;
					if (!string.IsNullOrWhiteSpace(fileRef) && expectedDigests.ContainsKey(fileRef))
						expectedDigests[fileRef] = reference.Attribute("DigestValue")?.Value ?? "";
				}
				if (expectedDigests.Values.Any(string.IsNullOrWhiteSpace))
					return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "Signature does not protect every mandatory OFD content entry");
				byte[] documentBytes = ReadEntryBytes(zipArchive, "Doc_0/Document.xml");
				byte[] bodyBytes = ReadEntryBytes(zipArchive, "Doc_0/Pages/Page_1/Content.xml");
				byte[] semanticBytes = ReadEntryBytes(zipArchive, "Doc_0/CustomTags/EmrStructuredData.xml");
				if (!string.Equals(ComputeEntrySha256(documentBytes), expectedDigests["/Doc_0/Document.xml"], StringComparison.OrdinalIgnoreCase)
					|| !string.Equals(ComputeEntrySha256(bodyBytes), expectedDigests["/Doc_0/Pages/Page_1/Content.xml"], StringComparison.OrdinalIgnoreCase)
					|| !string.Equals(ComputeEntrySha256(semanticBytes), expectedDigests["/Doc_0/CustomTags/EmrStructuredData.xml"], StringComparison.OrdinalIgnoreCase))
					return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "OFD content digest mismatch! Document body or semantic XML was tampered.");
				string expectedSignature = Convert.ToBase64String(ComputeSignedValue(text, documentBytes, bodyBytes, semanticBytes));
				if (!string.Equals(text3, expectedSignature, StringComparison.Ordinal))
				{
					return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "Digital signature verification failed! Package was tampered.");
				}
			}
			if (!hasSemanticXml)
				return VerificationFailure(text, docTitle, hasSemanticXml: false, contentSha, "Missing mandatory EmrStructuredData.xml semantic layer");
			return new OfdVerificationResult(IsValid: true, text, docTitle, 1, hasSemanticXml, hasSignature, contentSha, null);
		}
		catch (Exception ex)
		{
			return new OfdVerificationResult(IsValid: false, "", "", 0, HasSemanticXml: false, HasSignature: false, "", ex.Message);
		}
	}

	private static string BuildRootXml(OfdRenderOptions options)
	{
		string header = options.HospitalHeaderText == null
			? ""
			: "<ofd:CustomData Name=\"HospitalHeader\">" + EscapeXml(options.HospitalHeaderText) + "</ofd:CustomData>";
		string signSlots = options.SignSlotsSummary == null
			? ""
			: "<ofd:CustomData Name=\"SignSlots\">" + EscapeXml(options.SignSlotsSummary) + "</ofd:CustomData>";
		string watermark = options.WatermarkText == null
			? ""
			: "<ofd:CustomData Name=\"Watermark\">" + EscapeXml(options.WatermarkText) + "</ofd:CustomData>";
		return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:OFD xmlns:ofd=\"http://www.ofdspec.org/2016\" Version=\"1.0\" DocType=\"OFD\">\n  <ofd:DocBody>\n    <ofd:DocInfo>\n"
			+ $"      <ofd:DocID>{EscapeXml(options.DocId)}</ofd:DocID>\n"
			+ $"      <ofd:Title>{EscapeXml(options.Title)}</ofd:Title>\n"
			+ $"      <ofd:Author>{EscapeXml(options.Author)}</ofd:Author>\n"
			+ $"      <ofd:CreationDate>{EscapeXml(options.CreationDate)}</ofd:CreationDate>\n"
			+ "      <ofd:CustomDatas>\n"
			+ $"        <ofd:CustomData Name=\"DocNo\">{EscapeXml(options.DocNo)}</ofd:CustomData>\n"
			+ $"        <ofd:CustomData Name=\"PageSizeCode\">{EscapeXml(options.PageSizeCode)}</ofd:CustomData>\n"
			+ $"        <ofd:CustomData Name=\"MarginMm\">{options.MarginTopMm.ToString("0.#", CultureInfo.InvariantCulture)},{options.MarginBottomMm.ToString("0.#", CultureInfo.InvariantCulture)},{options.MarginLeftMm.ToString("0.#", CultureInfo.InvariantCulture)},{options.MarginRightMm.ToString("0.#", CultureInfo.InvariantCulture)}</ofd:CustomData>\n"
			+ "        " + header + "\n        " + signSlots + "\n        " + watermark + "\n"
			+ "      </ofd:CustomDatas>\n    </ofd:DocInfo>\n    <ofd:DocRoot>Doc_0/Document.xml</ofd:DocRoot>\n  </ofd:DocBody>\n</ofd:OFD>";
	}

	private static byte[] ComputeSignedValue(string docId, byte[] documentBytes, byte[] bodyBytes, byte[] semanticBytes)
	{
		string payload = docId + "\n"
			+ ComputeEntrySha256(documentBytes) + "\n"
			+ ComputeEntrySha256(bodyBytes) + "\n"
			+ ComputeEntrySha256(semanticBytes);
		return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
	}

	private static string ComputeEntrySha256(byte[] bytes) =>
		Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	private static byte[] ReadEntryBytes(ZipArchive archive, string path)
	{
		ZipArchiveEntry entry = archive.GetEntry(path)
			?? throw new InvalidDataException($"Missing mandatory OFD entry: {path}");
		using Stream stream = entry.Open();
		using MemoryStream bytes = new MemoryStream();
		stream.CopyTo(bytes);
		return bytes.ToArray();
	}

	private static OfdVerificationResult VerificationFailure(string docId, string title, bool hasSemanticXml, string contentSha, string error) =>
		new(IsValid: false, docId, title, 1, hasSemanticXml, HasSignature: false, contentSha, error);

	private static void AddZipEntry(ZipArchive archive, string path, string content)
	{
		ZipArchiveEntry zipArchiveEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
		using Stream stream = zipArchiveEntry.Open();
		byte[] bytes = Utf8WithoutBom.GetBytes(content);
		stream.Write(bytes, 0, bytes.Length);
	}

	private static void AddZipEntry(ZipArchive archive, string path, string content, byte[] bytes)
	{
		ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
		using Stream stream = entry.Open();
		stream.Write(bytes, 0, bytes.Length);
	}

	private static string EscapeXml(string input)
	{
		return SecurityElement.Escape(input) ?? "";
	}
}
