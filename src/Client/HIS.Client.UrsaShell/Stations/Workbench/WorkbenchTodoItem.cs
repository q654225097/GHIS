using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Workbench;

public sealed class WorkbenchTodoItem
{
    public required string Title { get; init; }

    public required string Context { get; init; }

    public required string Detail { get; init; }

    public required string Status { get; init; }

    public required string ActionLabel { get; init; }

    public required string NavigationKey { get; init; }

    public PatientQueueItemModel? OpdPatient { get; init; }

    public InpatientRecordModel? Inpatient { get; init; }

    public PharmacyDispenseModel? Dispense { get; init; }

    public bool IsCritical { get; init; }
}
