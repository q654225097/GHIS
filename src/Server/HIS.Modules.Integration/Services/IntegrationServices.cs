using HIS.BuildingBlocks.Database;
using HIS.Contracts;

namespace HIS.Modules.Integration.Services;

public interface IMedicalInsuranceAdapter
{
    Task<string> PreSettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);
    Task<string> SettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);
}

public class MockMedicalInsuranceAdapter : IMedicalInsuranceAdapter
{
    public Task<string> PreSettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default)
    {
        return Task.FromResult($"INS-PRE-{Guid.NewGuid():N}");
    }

    public Task<string> SettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default)
    {
        return Task.FromResult($"INS-SETTLE-{Guid.NewGuid():N}");
    }
}

