using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public interface IMedicalInsuranceAdapter
{
    Task<string> PreSettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);
    Task<string> SettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);
}
