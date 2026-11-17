using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using HIS.BuildingBlocks.Security;

namespace HIS.BuildingBlocks.OfdEngine;

/// <summary>
/// 国标版式电子文件 (GB/T 33190-2016) 与安全电子签章 (GB/T 38540-2020 / GM/T 0031) 生成与验证引擎
/// </summary>
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
			// 1. OFD.xml (Root)
			string content = BuildRootXml(options);
			AddZipEntry(archive, "OFD.xml", content);

			// 2. Doc_0/Document.xml
			string content2 = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Document xmlns:ofd=\"{OfdNs}\">\n  <ofd:CommonData>\n    <ofd:MaxUnitID>120</ofd:MaxUnitID>\n    <ofd:PageArea>\n      <ofd:PhysicalBox>0 0 {value} {value2}</ofd:PhysicalBox>\n    </ofd:PageArea>\n    <ofd:PublicRes>PublicRes.xml</ofd:PublicRes>\n  </ofd:CommonData>\n  <ofd:Pages>\n    <ofd:Page ID=\"1\" BaseLoc=\"Pages/Page_1/Content.xml\"/>\n  </ofd:Pages>\n  <ofd:CustomTags>CustomTags/CustomTags.xml</ofd:CustomTags>\n  {((options.SignerName != null) ? "<ofd:Signatures>Signs/Signatures.xml</ofd:Signatures>" : "")}\n</ofd:Document>";
			byte[] documentBytes = Utf8WithoutBom.GetBytes(content2);
			AddZipEntry(archive, "Doc_0/Document.xml", content2, documentBytes);

			// 3. Doc_0/PublicRes.xml
			string content3 = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Res xmlns:ofd=\"{OfdNs}\" BaseLoc=\"Res\">\n  <ofd:Fonts>\n    <ofd:Font ID=\"1\" FontName=\"SimSun\" FamilyName=\"Songti\"/>\n    <ofd:Font ID=\"2\" FontName=\"SimHei\" FamilyName=\"Heiti\"/>\n  </ofd:Fonts>\n</ofd:Res>";
			AddZipEntry(archive, "Doc_0/PublicRes.xml", content3);

			// 4. Doc_0/Pages/Page_1/Content.xml
			string input = string.IsNullOrWhiteSpace(options.HospitalHeaderText) ? options.Title : (options.HospitalHeaderText + " · " + options.Title);
			string input2 = string.IsNullOrWhiteSpace(options.PatientBannerText) ? options.ContentText : (options.PatientBannerText + "\n" + options.ContentText);
			string content4 = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Page xmlns:ofd=\"{OfdNs}\">\n  <ofd:Area>\n    <ofd:PhysicalBox>0 0 {value} {value2}</ofd:PhysicalBox>\n  </ofd:Area>\n  <ofd:Content>\n    <ofd:Layer ID=\"10\">\n      <ofd:TextObject ID=\"11\" Font=\"2\" Size=\"5.5\" Boundary=\"{value3} {value4} {value5} 16\">\n        <ofd:TextCode X=\"2\" Y=\"8\">{EscapeXml(input)}</ofd:TextCode>\n      </ofd:TextObject>\n      <ofd:TextObject ID=\"12\" Font=\"1\" Size=\"3.8\" Boundary=\"{value3} {value7} {value5} {value6}\">\n        <ofd:TextCode X=\"2\" Y=\"6\">{EscapeXml(input2)}</ofd:TextCode>\n      </ofd:TextObject>\n      {((options.SignSlotsSummary != null) ? $"<ofd:TextObject ID=\"15\" Font=\"1\" Size=\"3.5\" Boundary=\"{value3} {value8} {value5} 10\">\n  <ofd:TextCode X=\"2\" Y=\"5\">{EscapeXml(options.SignSlotsSummary)}</ofd:TextCode>\n</ofd:TextObject>" : "")}\n      {((options.FooterText != null) ? $"<ofd:TextObject ID=\"16\" Font=\"1\" Size=\"3.0\" Boundary=\"{value3} {value8} {value5} 8\">\n  <ofd:TextCode X=\"2\" Y=\"7\">{EscapeXml(options.FooterText)}</ofd:TextCode>\n</ofd:TextObject>" : "")}\n      {((options.WatermarkText != null) ? ("<ofd:TextObject ID=\"99\" Font=\"2\" Size=\"8.0\" Boundary=\"30 120 150 40\" Alpha=\"64\">\n  <ofd:TextCode X=\"10\" Y=\"20\">" + EscapeXml(options.WatermarkText) + "</ofd:TextCode>\n</ofd:TextObject>") : "")}\n    </ofd:Layer>\n  </ofd:Content>\n</ofd:Page>";
			byte[] contentBytes = Utf8WithoutBom.GetBytes(content4);
			AddZipEntry(archive, "Doc_0/Pages/Page_1/Content.xml", content4, contentBytes);

			// 5. Doc_0/CustomTags/CustomTags.xml & EmrStructuredData.xml (WS/T 500-2016 HL7 CDA R2)
			string content5 = options.StructuredSemanticXml ?? BuildDefaultCdaXml(options);
			string content6 = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:CustomTags xmlns:ofd=\"{OfdNs}\">\n  <ofd:CustomTag TypeId=\"EMR_SEMANTIC_DATA\">\n    <ofd:FileLoc>CustomTags/EmrStructuredData.xml</ofd:FileLoc>\n  </ofd:CustomTag>\n</ofd:CustomTags>";
			AddZipEntry(archive, "Doc_0/CustomTags/CustomTags.xml", content6);
			byte[] semanticBytes = Utf8WithoutBom.GetBytes(content5);
			AddZipEntry(archive, "Doc_0/CustomTags/EmrStructuredData.xml", content5, semanticBytes);

			// 6. Doc_0/Signs/ (GB/T 38540-2020 & GM/T 0031 国密电子签章)
			if (options.SignerName != null)
			{
				string docSha256 = ComputeEntrySha256(documentBytes);
				string contentSha256 = ComputeEntrySha256(contentBytes);
				string semanticSha256 = ComputeEntrySha256(semanticBytes);

				string docSm3 = CryptoHelper.ComputeSm3(documentBytes);
				string contentSm3 = CryptoHelper.ComputeSm3(contentBytes);
				string semanticSm3 = CryptoHelper.ComputeSm3(semanticBytes);

				byte[] signedValueSha = ComputeSignedValue(options.DocId, documentBytes, contentBytes, semanticBytes);
				byte[] signDataSm3 = Encoding.UTF8.GetBytes($"{options.DocId}\n{docSm3}\n{contentSm3}\n{semanticSm3}");
				string sm2SignatureBase64 = CryptoHelper.SignSm2(signDataSm3);
				byte[] rawSm2Bytes = Convert.FromBase64String(sm2SignatureBase64);

				// Signs/Signatures.xml
				string signaturesXml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Signatures xmlns:ofd=\"{OfdNs}\">\n  <ofd:Signature ID=\"Sign_0\" BaseLoc=\"Signs/Sign_0/Signature.xml\" Type=\"Seal\"/>\n  <ofd:Signature ID=\"Sign_1\" BaseLoc=\"Signs/Sign_1/Signature.xml\"/>\n</ofd:Signatures>";
				AddZipEntry(archive, "Doc_0/Signs/Signatures.xml", signaturesXml);

				// Doc_0/Signs/Sign_0/SignedValue.dat (Raw SM2 Signature Value)
				AddZipEntry(archive, "Doc_0/Signs/Sign_0/SignedValue.dat", rawSm2Bytes);

				// Doc_0/Signs/Sign_0/Signature.xml (GB/T 38540 国标签名描述)
				string sign0Xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Signature xmlns:ofd=\"{OfdNs}\">\n  <ofd:SignedInfo>\n    <ofd:Provider ProviderName=\"NationalSM2CA\" Version=\"1.0\" SignatureMethod=\"1.2.156.10197.1.501\"/>\n    <ofd:SignatureMethod>SM2-SM3</ofd:SignatureMethod>\n    <ofd:SignatureDateTime>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</ofd:SignatureDateTime>\n    <ofd:Signer>{EscapeXml(options.SignerName)}</ofd:Signer>\n    <ofd:DocId>{EscapeXml(options.DocId)}</ofd:DocId>\n    <ofd:References CheckMethod=\"SM3\">\n      <ofd:Reference FileRef=\"/Doc_0/Document.xml\" DigestMethod=\"SM3\" DigestValue=\"{docSm3}\"/>\n      <ofd:Reference FileRef=\"/Doc_0/Pages/Page_1/Content.xml\" DigestMethod=\"SM3\" DigestValue=\"{contentSm3}\"/>\n      <ofd:Reference FileRef=\"/Doc_0/CustomTags/EmrStructuredData.xml\" DigestMethod=\"SM3\" DigestValue=\"{semanticSm3}\"/>\n    </ofd:References>\n  </ofd:SignedInfo>\n  <ofd:SignedValue BaseLoc=\"SignedValue.dat\">{sm2SignatureBase64}</ofd:SignedValue>\n</ofd:Signature>";
				AddZipEntry(archive, "Doc_0/Signs/Sign_0/Signature.xml", sign0Xml);

				// Doc_0/Signs/Sign_1/Signature.xml (向后兼容经典签名描述)
				string sign1Xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ofd:Signature xmlns:ofd=\"{OfdNs}\">\n  <ofd:SignedInfo>\n    <ofd:Provider ProviderName=\"NationalSM2CA\" Version=\"1.0\" SignatureMethod=\"1.2.156.10197.1.501\"/>\n    <ofd:SignatureMethod>SM2-SM3</ofd:SignatureMethod>\n    <ofd:SignatureDateTime>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</ofd:SignatureDateTime>\n    <ofd:Signer>{EscapeXml(options.SignerName)}</ofd:Signer>\n    <ofd:DocId>{EscapeXml(options.DocId)}</ofd:DocId>\n    <ofd:References>\n      <ofd:Reference FileRef=\"/Doc_0/Document.xml\" DigestMethod=\"SHA256\" DigestValue=\"{docSha256}\"/>\n      <ofd:Reference FileRef=\"/Doc_0/Pages/Page_1/Content.xml\" DigestMethod=\"SHA256\" DigestValue=\"{contentSha256}\"/>\n      <ofd:Reference FileRef=\"/Doc_0/CustomTags/EmrStructuredData.xml\" DigestMethod=\"SHA256\" DigestValue=\"{semanticSha256}\"/>\n    </ofd:References>\n  </ofd:SignedInfo>\n  <ofd:SignedValue>{Convert.ToBase64String(signedValueSha)}</ofd:SignedValue>\n</ofd:Signature>";
				AddZipEntry(archive, "Doc_0/Signs/Sign_1/Signature.xml", sign1Xml);
			}
		}
		return memoryStream.ToArray();
	}

	public static OfdVerificationResult VerifyOfdPackage(byte[] ofdBytes)
	{
		try
		{
			string contentSha = Convert.ToHexString(SHA256.HashData(ofdBytes)).ToLowerInvariant();
			string contentSm3 = CryptoHelper.ComputeSm3(ofdBytes);

			using MemoryStream stream = new MemoryStream(ofdBytes);
			using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
			foreach (ZipArchiveEntry entry5 in zipArchive.Entries)
			{
				using Stream stream2 = entry5.Open();
				using Stream destination = Stream.Null;
				stream2.CopyTo(destination);
			}

			ZipArchiveEntry? entry = zipArchive.GetEntry("OFD.xml");
			if (entry == null)
			{
				return new OfdVerificationResult(IsValid: false, "", "", 0, HasSemanticXml: false, HasSignature: false, contentSha, "Missing OFD.xml root", contentSm3);
			}

			string text = "";
			string docTitle = "";
			using (StreamReader streamReader = new StreamReader(entry.Open()))
			{
				XDocument xDocument = XDocument.Parse(streamReader.ReadToEnd());
				XElement? xElement = xDocument.Descendants().FirstOrDefault(x => x.Name.LocalName == "DocInfo");
				text = xElement?.Elements().FirstOrDefault(x => x.Name.LocalName == "DocID")?.Value ?? "";
				docTitle = xElement?.Elements().FirstOrDefault(x => x.Name.LocalName == "Title")?.Value ?? "";
			}

			ZipArchiveEntry? entry2 = zipArchive.GetEntry("Doc_0/Document.xml");
			ZipArchiveEntry? entry3 = zipArchive.GetEntry("Doc_0/Pages/Page_1/Content.xml");
			if (entry2 == null || entry3 == null)
			{
				return new OfdVerificationResult(IsValid: false, text, docTitle, 0, HasSemanticXml: false, HasSignature: false, contentSha, "Incomplete OFD document structure", contentSm3);
			}

			bool hasSemanticXml = zipArchive.GetEntry("Doc_0/CustomTags/EmrStructuredData.xml") != null;
			ZipArchiveEntry? entry4 = zipArchive.GetEntry("Doc_0/Signs/Sign_0/Signature.xml")
								  ?? zipArchive.GetEntry("Doc_0/Signs/Sign_1/Signature.xml");
			bool hasSignature = entry4 != null;

			if (entry4 != null)
			{
				using StreamReader streamReader2 = new StreamReader(entry4.Open());
				XDocument xDocument2 = XDocument.Parse(streamReader2.ReadToEnd());
				string text2 = xDocument2.Descendants().FirstOrDefault(x => x.Name.LocalName == "Signer")?.Value ?? "";
				string text3 = xDocument2.Descendants().FirstOrDefault(x => x.Name.LocalName == "SignedValue")?.Value ?? "";
				string signedDocId = xDocument2.Descendants().FirstOrDefault(x => x.Name.LocalName == "DocId")?.Value ?? "";
				if (!string.Equals(signedDocId, text, StringComparison.Ordinal))
				{
					return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "Signed document identity does not match OFD.xml", contentSm3);
				}

				var expectedDigests = new Dictionary<string, (string Digest, string Method)>(StringComparer.Ordinal)
				{
					["/Doc_0/Document.xml"] = ("", "SHA256"),
					["/Doc_0/Pages/Page_1/Content.xml"] = ("", "SHA256"),
					["/Doc_0/CustomTags/EmrStructuredData.xml"] = ("", "SHA256")
				};

				foreach (XElement reference in xDocument2.Descendants().Where(x => x.Name.LocalName == "Reference"))
				{
					string? fileRef = reference.Attribute("FileRef")?.Value;
					string method = reference.Attribute("DigestMethod")?.Value ?? "SHA256";
					if (!string.IsNullOrWhiteSpace(fileRef) && expectedDigests.ContainsKey(fileRef))
					{
						expectedDigests[fileRef] = (reference.Attribute("DigestValue")?.Value ?? "", method);
					}
				}

				if (expectedDigests.Values.Any(v => string.IsNullOrWhiteSpace(v.Digest)))
				{
					return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "Signature does not protect every mandatory OFD content entry", contentSm3);
				}

				byte[] documentBytes = ReadEntryBytes(zipArchive, "Doc_0/Document.xml");
				byte[] bodyBytes = ReadEntryBytes(zipArchive, "Doc_0/Pages/Page_1/Content.xml");
				byte[] semanticBytes = ReadEntryBytes(zipArchive, "Doc_0/CustomTags/EmrStructuredData.xml");

				bool isSm3 = expectedDigests["/Doc_0/Document.xml"].Method.Equals("SM3", StringComparison.OrdinalIgnoreCase);

				if (isSm3)
				{
					if (!string.Equals(CryptoHelper.ComputeSm3(documentBytes), expectedDigests["/Doc_0/Document.xml"].Digest, StringComparison.OrdinalIgnoreCase)
						|| !string.Equals(CryptoHelper.ComputeSm3(bodyBytes), expectedDigests["/Doc_0/Pages/Page_1/Content.xml"].Digest, StringComparison.OrdinalIgnoreCase)
						|| !string.Equals(CryptoHelper.ComputeSm3(semanticBytes), expectedDigests["/Doc_0/CustomTags/EmrStructuredData.xml"].Digest, StringComparison.OrdinalIgnoreCase))
					{
						return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "OFD content digest mismatch! Document body or semantic XML was tampered.", contentSm3);
					}

					byte[] signDataSm3 = Encoding.UTF8.GetBytes($"{text}\n{expectedDigests["/Doc_0/Document.xml"].Digest}\n{expectedDigests["/Doc_0/Pages/Page_1/Content.xml"].Digest}\n{expectedDigests["/Doc_0/CustomTags/EmrStructuredData.xml"].Digest}");
					bool sm2Valid = CryptoHelper.VerifySm2(signDataSm3, text3);
					if (!sm2Valid)
					{
						return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "SM2 digital signature verification failed! Package was tampered.", contentSm3);
					}
				}
				else
				{
					if (!string.Equals(ComputeEntrySha256(documentBytes), expectedDigests["/Doc_0/Document.xml"].Digest, StringComparison.OrdinalIgnoreCase)
						|| !string.Equals(ComputeEntrySha256(bodyBytes), expectedDigests["/Doc_0/Pages/Page_1/Content.xml"].Digest, StringComparison.OrdinalIgnoreCase)
						|| !string.Equals(ComputeEntrySha256(semanticBytes), expectedDigests["/Doc_0/CustomTags/EmrStructuredData.xml"].Digest, StringComparison.OrdinalIgnoreCase))
					{
						return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "OFD content digest mismatch! Document body or semantic XML was tampered.", contentSm3);
					}

					string expectedSignature = Convert.ToBase64String(ComputeSignedValue(text, documentBytes, bodyBytes, semanticBytes));
					if (!string.Equals(text3, expectedSignature, StringComparison.Ordinal))
					{
						return VerificationFailure(text, docTitle, hasSemanticXml, contentSha, "Digital signature verification failed! Package was tampered.", contentSm3);
					}
				}
			}

			if (!hasSemanticXml)
			{
				return VerificationFailure(text, docTitle, hasSemanticXml: false, contentSha, "Missing mandatory EmrStructuredData.xml semantic layer", contentSm3);
			}

			return new OfdVerificationResult(IsValid: true, text, docTitle, 1, hasSemanticXml, hasSignature, contentSha, null, contentSm3);
		}
		catch (Exception ex)
		{
			return new OfdVerificationResult(IsValid: false, "", "", 0, HasSemanticXml: false, HasSignature: false, "", ex.Message, null);
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
		string signaturesEntry = options.SignerName != null
			? "    <ofd:Signatures>Doc_0/Signs/Signatures.xml</ofd:Signatures>\n"
			: "";

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
			+ "      </ofd:CustomDatas>\n    </ofd:DocInfo>\n    <ofd:DocRoot>Doc_0/Document.xml</ofd:DocRoot>\n"
			+ signaturesEntry
			+ "  </ofd:DocBody>\n</ofd:OFD>";
	}

	/// <summary>
	/// 生成符合国家卫健委 WS/T 500-2016 的 HL7 CDA R2 临床文档标准结构
	/// </summary>
	private static string BuildDefaultCdaXml(OfdRenderOptions options)
	{
		return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<ClinicalDocument xmlns=""urn:hl7-org:v3"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <realmCode code=""CN""/>
  <typeId root=""2.16.840.1.113883.1.3"" extension=""POCD_HD000040""/>
  <templateId root=""2.16.156.10011.2.1.1.21""/>
  <id root=""2.16.156.10011.1.1"" extension=""{EscapeXml(options.DocNo)}""/>
  <code code=""C0001"" codeSystem=""2.16.156.10011.2.5.1"" displayName=""{EscapeXml(options.Title)}""/>
  <title>{EscapeXml(options.Title)}</title>
  <effectiveTime value=""{DateTime.UtcNow:yyyyMMddHHmmss}""/>
  <confidentialityCode code=""N"" codeSystem=""2.16.840.1.113883.5.25"" displayName=""正常""/>
  <recordTarget typeCode=""RCT"">
    <patientRole classCode=""PAT"">
      <id root=""2.16.156.10011.1.2"" extension=""{EscapeXml(options.DocId)}""/>
      <patient classCode=""PSN"" determinerCode=""INSTANCE"">
        <name>{EscapeXml(options.PatientBannerText ?? "患者")}</name>
      </patient>
    </patientRole>
  </recordTarget>
  <author typeCode=""AUT"">
    <time value=""{DateTime.UtcNow:yyyyMMddHHmmss}""/>
    <assignedAuthor classCode=""ASSIGNED"">
      <id root=""2.16.156.10011.1.4"" extension=""{EscapeXml(options.Author)}""/>
      <assignedPerson classCode=""PSN"" determinerCode=""INSTANCE"">
        <name>{EscapeXml(options.Author)}</name>
      </assignedPerson>
    </assignedAuthor>
  </author>
  <custodian typeCode=""CST"">
    <assignedCustodian classCode=""ASSIGNED"">
      <representedCustodianOrganization classCode=""ORG"" determinerCode=""INSTANCE"">
        <name>{EscapeXml(options.HospitalHeaderText ?? "国家三甲综合医院")}</name>
      </representedCustodianOrganization>
    </assignedCustodian>
  </custodian>
  <legalAuthenticator typeCode=""LA"">
    <time value=""{DateTime.UtcNow:yyyyMMddHHmmss}""/>
    <signatureCode code=""S""/>
    <assignedEntity classCode=""ASSIGNED"">
      <id root=""2.16.156.10011.1.4"" extension=""{EscapeXml(options.SignerName ?? options.Author)}""/>
      <assignedPerson classCode=""PSN"" determinerCode=""INSTANCE"">
        <name>{EscapeXml(options.SignerName ?? options.Author)}</name>
      </assignedPerson>
    </assignedEntity>
  </legalAuthenticator>
  <component>
    <structuredBody>
      <section>
        <code code=""DE04.01.119.00"" codeSystem=""2.16.156.10011.2.2.1"" displayName=""主诉""/>
        <text>{EscapeXml(options.ContentText)}</text>
        <entry>
          <observation classCode=""OBS"" moodCode=""EVN"">
            <code code=""DE04.01.119.00"" codeSystem=""2.16.156.10011.2.2.1"" displayName=""主诉""/>
            <value xsi:type=""ST"">{EscapeXml(options.ContentText)}</value>
          </observation>
        </entry>
      </section>
    </structuredBody>
  </component>
</ClinicalDocument>";
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

	private static OfdVerificationResult VerificationFailure(string docId, string title, bool hasSemanticXml, string contentSha, string error, string? sm3 = null) =>
		new(IsValid: false, docId, title, 1, hasSemanticXml, HasSignature: false, contentSha, error, sm3);

	private static void AddZipEntry(ZipArchive archive, string path, string content)
	{
		ZipArchiveEntry zipArchiveEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
		using Stream stream = zipArchiveEntry.Open();
		byte[] bytes = Utf8WithoutBom.GetBytes(content);
		stream.Write(bytes, 0, bytes.Length);
	}

	private static void AddZipEntry(ZipArchive archive, string path, byte[] bytes)
	{
		ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
		using Stream stream = entry.Open();
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
