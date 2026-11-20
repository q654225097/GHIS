using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.OfdEngine;
using HIS.BuildingBlocks.Security;
using HIS.Modules.Emr.Models;

namespace HIS.Modules.Emr.Services;

public class EmrOfdRenderService
{
	private readonly IRepository<EmrOfdArchive> _archiveRepo;

	private readonly IRepository<EmrDocTemplate>? _templateRepo;

	private readonly IRepository<EmrOfdLayoutSchema>? _layoutRepo;

	private readonly IExternalTsaClient? _tsaClient;

	public EmrOfdRenderService(
		IRepository<EmrOfdArchive> archiveRepo,
		IRepository<EmrDocTemplate>? templateRepo = null,
		IRepository<EmrOfdLayoutSchema>? layoutRepo = null,
		IExternalTsaClient? tsaClient = null)
	{
		_archiveRepo = archiveRepo;
		_templateRepo = templateRepo;
		_layoutRepo = layoutRepo;
		_tsaClient = tsaClient;
	}

	public async Task<EmrOfdLayoutSchema?> ResolveLayoutForDocumentAsync(string? templateIdOrCode, string emrScene, string? overrideLayoutId = null, CancellationToken ct = default(CancellationToken))
	{
		if (_layoutRepo == null)
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(overrideLayoutId))
		{
			EmrOfdLayoutSchema? custom = await _layoutRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_ofd_layout_schemas WHERE (id = @Key OR layout_code = @Key) AND is_active = 1 LIMIT 1", new
			{
				Key = overrideLayoutId.Trim()
			}, ct);
			if (custom != null)
			{
				return custom;
			}
		}
		if (_templateRepo != null && !string.IsNullOrWhiteSpace(templateIdOrCode))
		{
			EmrDocTemplate? tpl = await _templateRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_doc_templates WHERE id = @Key OR template_code = @Key ORDER BY template_version DESC LIMIT 1", new
			{
				Key = templateIdOrCode.Trim()
			}, ct);
			if (!string.IsNullOrWhiteSpace(tpl?.OfdLayoutId))
			{
				EmrOfdLayoutSchema? boundLayout = await _layoutRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_ofd_layout_schemas WHERE (id = @Key OR layout_code = @Key) AND is_active = 1 LIMIT 1", new
				{
					Key = tpl.OfdLayoutId
				}, ct);
				if (boundLayout != null)
				{
					return boundLayout;
				}
			}
		}
		string? text = emrScene?.ToUpperInvariant();
		string? text2 = text;
		string text3;
		if (text2 == "IPD")
		{
			text3 = "LAYOUT-IPD-A4";
		}
		else
		{
			text3 = ((!(text2 == "WARD")) ? "LAYOUT-OPD-B5" : "LAYOUT-WARD-A4");
		}
		if (1 == 0)
		{
		}
		string fallbackId = text3;
		return await _layoutRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_ofd_layout_schemas WHERE id = @Id AND is_active = 1 LIMIT 1", new
		{
			Id = fallbackId
		}, ct);
	}

	public async Task<EmrOfdArchive> RenderAndArchiveAsync(EmrDocument doc, string signerName, string? watermark = null, CancellationToken ct = default(CancellationToken))
	{
		EmrOfdLayoutSchema? layout = await ResolveLayoutForDocumentAsync(doc.TemplateId, doc.EmrScene, null, ct);
		string pageSizeCode = layout?.PageSizeCode ?? "A4";
		decimal pageWidthMm = layout?.PageWidthMm ?? 210.0m;
		decimal pageHeightMm = layout?.PageHeightMm ?? 297.0m;
		decimal marginTopMm = layout?.MarginTopMm ?? 20.0m;
		decimal marginBottomMm = layout?.MarginBottomMm ?? 20.0m;
		decimal marginLeftMm = layout?.MarginLeftMm ?? 20.0m;
		decimal marginRightMm = layout?.MarginRightMm ?? 20.0m;
		string effectiveWatermark = watermark ?? layout?.WatermarkPolicy ?? "国家三级甲等综合医院 电子病历归档专章";
		string hospitalHeader = ExtractJsonString(layout?.HeaderLayoutJson, "hospitalName") ?? "国家区域医疗中心 · 三级甲等综合医院";
		string subTitle = ExtractJsonString(layout?.HeaderLayoutJson, "subTitle") ?? doc.DocTitle;
		string structuredSemanticXml = BuildStructuredSemanticXml(doc, layout?.LayoutCode ?? "DEFAULT_A4", signerName, pageSizeCode, pageWidthMm, pageHeightMm);
		string signLevelText = EmrSignLevelText(doc.Status);
		OfdRenderOptions ofdOptions = new OfdRenderOptions(FooterText: ExtractJsonString(layout?.FooterLayoutJson, "footerText") ?? "遵循 GB/T 33190-2016 电子病历归档标准", SignSlotsSummary: $"审签人: {signerName} | 审签等级: {signLevelText}级 | 版式: {pageSizeCode} ({pageWidthMm:0.#}x{pageHeightMm:0.#}mm)", DocId: doc.Id, DocNo: doc.DocNo, Title: doc.DocTitle, Author: signerName, CreationDate: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), ContentText: doc.PlainTextSummary ?? "结构化病历内容", StructuredSemanticXml: structuredSemanticXml, SignerName: signerName, WatermarkText: effectiveWatermark, PageSizeCode: pageSizeCode, PageWidthMm: pageWidthMm, PageHeightMm: pageHeightMm, MarginTopMm: marginTopMm, MarginBottomMm: marginBottomMm, MarginLeftMm: marginLeftMm, MarginRightMm: marginRightMm, HospitalHeaderText: hospitalHeader + " · " + subTitle, PatientBannerText: $"患者ID: {doc.PatientId} | 就诊/住院号: {doc.VisitOrEncounterId} | 场景: {doc.EmrScene}");
		byte[] ofdBytes = OfdDocumentPackage.BuildOfdPackage(ofdOptions);
		string sha256 = Convert.ToHexString(SHA256.HashData(ofdBytes)).ToLowerInvariant();
		byte[] sm3Bytes = CryptoHelper.ComputeSm3Bytes(ofdBytes);
		string sm3Digest = Convert.ToHexString(sm3Bytes).ToLowerInvariant();
		string caSignSerialNo = $"CA-2026-{Math.Abs(signerName.GetHashCode()):X8}";
		string tsaTimestampToken;
		if (_tsaClient != null)
		{
			var tsaResult = await _tsaClient.RequestTimestampAsync(sm3Bytes, ct);
			tsaTimestampToken = tsaResult.TokenBase64;
		}
		else
		{
			tsaTimestampToken = TsaHelper.GenerateTimeStampToken(sm3Bytes);
		}

		string storageDir = Path.Combine("AppData", "OfdStorage");
		Directory.CreateDirectory(storageDir);
		string filePath = Path.Combine(storageDir, doc.DocNo + ".ofd");
		await File.WriteAllBytesAsync(filePath, ofdBytes, ct);
		string fileKey = "AppData/OfdStorage/" + doc.DocNo + ".ofd";

		EmrOfdArchive? existingArchive = await _archiveRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_ofd_archives WHERE ofd_no = @OfdNo", new
		{
			OfdNo = "OFD-" + doc.DocNo
		}, ct);

		var signChainList = new System.Collections.Generic.List<object>();
		if (existingArchive != null)
		{
			if (!string.IsNullOrWhiteSpace(existingArchive.ExtDataJson))
			{
				try
				{
					using var docJson = JsonDocument.Parse(existingArchive.ExtDataJson);
					if (docJson.RootElement.TryGetProperty("signChain", out var chain) && chain.ValueKind == JsonValueKind.Array)
					{
						foreach (var el in chain.EnumerateArray())
						{
							signChainList.Add(JsonSerializer.Deserialize<object>(el.GetRawText())!);
						}
					}
				}
				catch { }
			}

			signChainList.Add(new
			{
				signLevel = doc.Status,
				signerName,
				signedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				caSerialNo = caSignSerialNo,
				sm3Digest
			});

			existingArchive.PageCount = 1;
			existingArchive.FileStorageKey = fileKey;
			existingArchive.FileSizeBytes = ofdBytes.Length;
			existingArchive.ContentSha256 = sha256;
			existingArchive.Sm3Digest = sm3Digest;
			existingArchive.CaSignSerialNo = caSignSerialNo;
			existingArchive.TsaTimestampToken = tsaTimestampToken;
			existingArchive.GeneratedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			existingArchive.ExtDataJson = JsonSerializer.Serialize(new { signChain = signChainList });
			await _archiveRepo.UpdateAsync(existingArchive, ct);
			return existingArchive;
		}

		signChainList.Add(new
		{
			signLevel = doc.Status,
			signerName,
			signedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			caSerialNo = caSignSerialNo,
			sm3Digest
		});

		EmrOfdArchive archive = new EmrOfdArchive
		{
			OfdNo = "OFD-" + doc.DocNo,
			ArchiveScope = "SINGLE_DOC",
			VisitOrEncounterId = doc.VisitOrEncounterId,
			DocumentId = doc.Id,
			PatientId = doc.PatientId,
			OfdVersionStandard = "GB/T 33190-2016",
			PageCount = 1,
			FileStorageKey = fileKey,
			FileSizeBytes = ofdBytes.Length,
			ContentSha256 = sha256,
			Sm3Digest = sm3Digest,
			CaSignSerialNo = caSignSerialNo,
			TsaTimestampToken = tsaTimestampToken,
			HasEmbeddedSemanticXml = 1,
			ArchiveStatus = 0,
			GeneratedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			VersionNo = Math.Max(1, doc.Status),
			ExtDataJson = JsonSerializer.Serialize(new { signChain = signChainList })
		};
		await _archiveRepo.InsertAsync(archive, ct);
		return archive;
	}

	internal static string? ExtractJsonString(string? json, string propertyName)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object && jsonDocument.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
			{
				return value.GetString();
			}
		}
		catch (JsonException)
		{
		}
		return null;
	}

	private static string BuildStructuredSemanticXml(
		EmrDocument doc,
		string layoutCode,
		string signerName,
		string pageSizeCode,
		decimal pageWidthMm,
		decimal pageHeightMm)
	{
		XNamespace cda = "urn:hl7-org:v3";
		XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

		var root = new XElement(cda + "ClinicalDocument",
			new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
			new XElement(cda + "realmCode", new XAttribute("code", "CN")),
			new XElement(cda + "typeId", new XAttribute("root", "2.16.840.1.113883.1.3"), new XAttribute("extension", "POCD_HD000040")),
			new XElement(cda + "templateId", new XAttribute("root", "2.16.156.10011.2.1.1.21")),
			new XElement(cda + "id", new XAttribute("root", "2.16.156.10011.1.1"), new XAttribute("extension", doc.DocNo)),
			new XElement(cda + "code", new XAttribute("code", doc.DocTypeCode ?? "C0001"), new XAttribute("codeSystem", "2.16.156.10011.2.5.1"), new XAttribute("displayName", doc.DocTitle ?? "临床病历记录")),
			new XElement(cda + "title", doc.DocTitle ?? "电子病历"),
			new XElement(cda + "effectiveTime", new XAttribute("value", DateTime.UtcNow.ToString("yyyyMMddHHmmss"))),
			new XElement(cda + "confidentialityCode", new XAttribute("code", "N"), new XAttribute("codeSystem", "2.16.840.1.113883.5.25"), new XAttribute("displayName", "正常")),
			new XElement(cda + "languageCode", new XAttribute("code", "zh-CN")),
			new XElement(cda + "recordTarget",
				new XElement(cda + "patientRole",
					new XElement(cda + "id", new XAttribute("root", "2.16.156.10011.1.2"), new XAttribute("extension", doc.PatientId)),
					new XElement(cda + "patient",
						new XElement(cda + "name", doc.PatientId)))),
			new XElement(cda + "author",
				new XElement(cda + "time", new XAttribute("value", DateTime.UtcNow.ToString("yyyyMMddHHmmss"))),
				new XElement(cda + "assignedAuthor",
					new XElement(cda + "id", new XAttribute("root", "2.16.156.10011.1.4"), new XAttribute("extension", doc.AuthorId ?? "DOC001")),
					new XElement(cda + "assignedPerson",
						new XElement(cda + "name", signerName)))),
			new XElement(cda + "custodian",
				new XElement(cda + "assignedCustodian",
					new XElement(cda + "representedCustodianOrganization",
						new XElement(cda + "name", "国家区域医疗中心 · 三级甲等综合医院")))),
			new XElement(cda + "component",
				new XElement(cda + "structuredBody",
					new XElement(cda + "component",
						new XElement(cda + "section",
							new XElement(cda + "code", new XAttribute("code", "DE04.01.119.00"), new XAttribute("codeSystem", "2.16.156.10011.2.2.1"), new XAttribute("displayName", "主诉")),
							new XElement(cda + "title", "主诉"),
							new XElement(cda + "text", doc.PlainTextSummary ?? string.Empty),
							new XElement(cda + "entry",
								new XElement(cda + "observation", new XAttribute("classCode", "OBS"), new XAttribute("moodCode", "EVN"),
									new XElement(cda + "code", new XAttribute("code", "DE04.01.119.00"), new XAttribute("codeSystem", "2.16.156.10011.2.2.1")),
									new XElement(cda + "value", new XAttribute(xsi + "type", "ST"), doc.PlainTextSummary ?? string.Empty))))))));

		return root.ToString(SaveOptions.DisableFormatting);
	}

	private static string EmrSignLevelText(int status) => status switch
	{
		3 => "3 (主任终审锁定)",
		2 => "2 (主治医师/护士长审签)",
		1 => "1 (住院医师/责任护士签名)",
		_ => status.ToString()
	};

	internal static bool ExtractJsonBool(string? json, string propertyName, bool defaultValue = true)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return defaultValue;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object && jsonDocument.RootElement.TryGetProperty(propertyName, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
			{
				return value.GetBoolean();
			}
		}
		catch (JsonException)
		{
		}
		return defaultValue;
	}
}
