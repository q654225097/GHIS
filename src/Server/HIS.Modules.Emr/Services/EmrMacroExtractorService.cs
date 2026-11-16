using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Emr.Models;

namespace HIS.Modules.Emr.Services;

public class EmrMacroExtractorService : IEmrMacroExtractorContract
{
	private readonly IRepository<EmrDocumentElement> _docElementRepo;

	private readonly IPatientReadOnlyContract _patientContract;

	public EmrMacroExtractorService(IRepository<EmrDocumentElement> docElementRepo, IPatientReadOnlyContract patientContract)
	{
		_docElementRepo = docElementRepo;
		_patientContract = patientContract;
	}

	public async Task<string?> ExtractMacroValueAsync(string visitOrEncounterId, string macroSourcePath, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(macroSourcePath))
		{
			return null;
		}
		return (await _docElementRepo.QuerySingleOrDefaultAsync("SELECT de.* FROM emr_document_elements de\nINNER JOIN emr_documents d ON d.id = de.document_id\nWHERE d.visit_or_encounter_id = @VisitId AND de.element_code = @Code\nORDER BY de.updated_at_ms DESC LIMIT 1", new
		{
			VisitId = visitOrEncounterId,
			Code = macroSourcePath.Trim()
		}, ct))?.ValString;
	}
}
