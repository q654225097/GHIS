using System.Collections.Generic;

namespace HIS.Modules.Inpatient.Services;

public sealed record DischargeCheckResult(bool AllPassed, IReadOnlyList<string> FailedItems)
{
    public IReadOnlyList<string> BlockingReasons => FailedItems;
}
