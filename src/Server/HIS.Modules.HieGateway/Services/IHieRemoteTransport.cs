using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HIS.Contracts;

namespace HIS.Modules.HieGateway.Services;

public interface IHieRemoteTransport
{
    string TransportMode { get; }
    Task<EmpiResolveResultDto?> QueryEmpiMappingAsync(string localPatientId, string rawIndex, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalEhrRecordDto>> FetchRemoteEhrRecordsAsync(string patientId, CancellationToken ct = default);
    Task<bool> SendOutboundResourceAsync(string resourceType, string resourceId, string targetOrgCode, string payloadJson, CancellationToken ct = default);
}
