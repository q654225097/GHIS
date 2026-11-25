using System.Linq;
using System.Threading.Tasks;
using HIS.Client.Core;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.IpdNurse;

public sealed partial class IpdNurseViewModel
{
    public async Task ExecutePdaScanVerifyAndExecuteServerAsync()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null)
        {
            _shell?.ShowToast("请先选择患者", "Warning");
            return;
        }

        if (string.IsNullOrWhiteSpace(_pdaWristbandBarcode))
        {
            _shell?.ShowToast("请扫描患者腕带条码（住院号或患者编号）", "Warning");
            return;
        }

        bool isPatientMatch = string.Equals(_pdaWristbandBarcode.Trim(), patient.AdmissionNo.Trim(), System.StringComparison.OrdinalIgnoreCase)
                           || string.Equals(_pdaWristbandBarcode.Trim(), patient.PatientId.Trim(), System.StringComparison.OrdinalIgnoreCase)
                           || string.Equals(_pdaWristbandBarcode.Trim(), patient.MedicalRecordNo.Trim(), System.StringComparison.OrdinalIgnoreCase)
                           || _pdaWristbandBarcode.Contains(patient.AdmissionNo.Trim(), System.StringComparison.OrdinalIgnoreCase);

        if (!isPatientMatch)
        {
            _shell?.ShowToast("【409 严重条码不匹配】患者腕带与当前在院档案不一致，严禁给药！", "Error");
            return;
        }

        var pendingTask = patient.NursingTasks.FirstOrDefault(t => t.IsPending);
        if (pendingTask == null)
        {
            _shell?.ShowToast("腕带核对一致，当前患者暂无待执行护理任务", "Info");
            return;
        }

        if (pendingTask.IsServerBacked && !string.IsNullOrWhiteSpace(pendingTask.PlanId))
        {
            string itemBarcode = string.IsNullOrWhiteSpace(_pdaMedicationBarcode) ? pendingTask.SourceOrder : _pdaMedicationBarcode.Trim();
            var (ok, msg) = await Workbench.ExecutePlanOnServerAsync(
                patient, pendingTask, _pdaWristbandBarcode.Trim(), itemBarcode);
            _shell?.ShowToast(msg, ok ? "Success" : "Error");
            if (ok) PdaMedicationBarcode = string.Empty;
        }
        else
        {
            ExecuteTask(pendingTask);
            _shell?.ShowToast($"PDA 双码核验通过：已执行医嘱 [{pendingTask.Content}]", "Success");
            PdaMedicationBarcode = string.Empty;
        }
        RaiseComputed();
    }
}
