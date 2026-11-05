using Avalonia.Controls;
using Avalonia.Interactivity;
using HIS.BuildingBlocks.OfdEngine;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class UrsaStructuredEmrEditorView : UserControl
{
    public UrsaStructuredEmrEditorView()
    {
        InitializeComponent();
        var vm = new EmrEditorWorkspaceModel();
        DataContext = vm;

        BtnSignOfd.Click += async (s, e) =>
        {
            vm.IsBusy = true;
            try
            {
                await Task.Delay(500); // Simulate network roundtrip
                var ofdOptions = new OfdRenderOptions(
                    DocId: Guid.NewGuid().ToString("D"),
                    DocNo: vm.DocNo,
                    Title: vm.DocTitle,
                    Author: "张建国 (主治医师)",
                    CreationDate: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ContentText: $"【主诉】{vm.ChiefComplaint}\n【现病史】{vm.PresentIllness}\n【初步诊断】{vm.Diagnosis}",
                    SignerName: "张建国 (CA签章)",
                    WatermarkText: "HIS-V4 临床归档件"
                );
                var ofdBytes = OfdDocumentPackage.BuildOfdPackage(ofdOptions);
                var verification = OfdDocumentPackage.VerifyOfdPackage(ofdBytes);
                if (verification.IsValid)
                {
                    vm.OfdNo = "OFD-" + vm.DocNo;
                }
            }
            finally
            {
                vm.IsBusy = false;
            }
        };
    }
}

