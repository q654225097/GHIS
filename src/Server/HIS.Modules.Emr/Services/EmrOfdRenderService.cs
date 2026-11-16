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

	public EmrOfdRenderService(IRepository<EmrOfdArchive> archiveRepo, IRepository<EmrDocTemplate>? templateRepo = null, IRepository<EmrOfdLayoutSchema>? layoutRepo = null)
	{
		_archiveRepo = archiveRepo;
		_templateRepo = templateRepo;
		_layoutRepo = layoutRepo;
	}

	public async Task<EmrOfdLayoutSchema?> ResolveLayoutForDocumentAsync(string? templateIdOrCode, string emrScene, string? overrideLayoutId = null, CancellationToken ct = default(CancellationToken))
	{
		if (_layoutRepo == null)
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(overrideLayoutId))
		{
			EmrOfdLayoutSchema custom = await _layoutRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_ofd_layout_schemas WHERE (id = @Key OR layout_code = @Key) AND is_active = 1 LIMIT 1", new
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
			EmrDocTemplate tpl = await _templateRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_doc_templates WHERE id = @Key OR template_code = @Key ORDER BY template_version DESC LIMIT 1", new
			{
				Key = templateIdOrCode.Trim()
			}, ct);
			if (!string.IsNullOrWhiteSpace(tpl?.OfdLayoutId))
			{
				EmrOfdLayoutSchema boundLayout = await _layoutRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_ofd_layout_schemas WHERE (id = @Key OR layout_code = @Key) AND is_active = 1 LIMIT 1", new
				{
					Key = tpl.OfdLayoutId
				}, ct);
				if (boundLayout != null)
				{
					return boundLayout;
				}
			}
		}
		string text = emrScene?.ToUpperInvariant();
		if (1 == 0)
		{
		}
		string text2 = text;
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
		EmrOfdLayoutSchema layout = await ResolveLayoutForDocumentAsync(doc.TemplateId, doc.EmrScene, null, ct);
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
		string storageDir = Path.Combine("AppData", "OfdStorage");
		Directory.CreateDirectory(storageDir);
		string filePath = Path.Combine(storageDir, doc.DocNo + ".ofd");
		await File.WriteAllBytesAsync(filePath, ofdBytes, ct);
		string fileKey = "AppData/OfdStorage/" + doc.DocNo + ".ofd";
		EmrOfdArchive existingArchive = await _archiveRepo.QuerySingleOrDefaultAsync("SELECT * FROM emr_ofd_archives WHERE ofd_no = @OfdNo", new
		{
			OfdNo = "OFD-" + doc.DocNo
		}, ct);
		if (existingArchive != null)
		{
			existingArchive.PageCount = 1;
			existingArchive.FileStorageKey = fileKey;
			existingArchive.FileSizeBytes = ofdBytes.Length;
			existingArchive.ContentSha256 = sha256;
			existingArchive.Sm3Digest = sha256.Substring(0, 32);
			existingArchive.CaSignSerialNo = $"CA-{Guid.NewGuid():N}";
			existingArchive.TsaTimestampToken = $"TSA-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
			existingArchive.GeneratedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			await _archiveRepo.UpdateAsync(existingArchive, ct);
			return existingArchive;
		}
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
			Sm3Digest = sha256.Substring(0, 32),
			CaSignSerialNo = $"CA-{Guid.NewGuid():N}",
			TsaTimestampToken = $"TSA-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
			HasEmbeddedSemanticXml = 1,
			ArchiveStatus = 0,
			GeneratedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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
		var root = new XElement("EmrDocumentOfdData",
			new XAttribute("DocNo", doc.DocNo),
			new XAttribute("Scene", doc.EmrScene),
			new XAttribute("DocTypeCode", doc.DocTypeCode),
			new XAttribute("LayoutCode", layoutCode),
			new XElement("PatientId", doc.PatientId),
			new XElement("VisitId", doc.VisitOrEncounterId),
			new XElement("Signer", signerName),
			new XElement("PageSize",
				new XAttribute("Code", pageSizeCode),
				new XAttribute("WidthMm", pageWidthMm.ToString("0.#")),
				new XAttribute("HeightMm", pageHeightMm.ToString("0.#"))),
			new XElement("StructuredAst", doc.StructuredAstJson ?? string.Empty));
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
