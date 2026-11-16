using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace HIS.Client.UrsaShell.Shell.Controls;

public partial class PatientBanner : UserControl
{
    public static readonly StyledProperty<bool> HasPatientProperty =
        AvaloniaProperty.Register<PatientBanner, bool>(nameof(HasPatient));

    public static readonly StyledProperty<string> EncounterBadgeProperty =
        AvaloniaProperty.Register<PatientBanner, string>(nameof(EncounterBadge), "门诊");

    public static readonly StyledProperty<string> PatientNameProperty =
        AvaloniaProperty.Register<PatientBanner, string>(nameof(PatientName), string.Empty);

    public static readonly StyledProperty<string> GenderAndAgeProperty =
        AvaloniaProperty.Register<PatientBanner, string>(nameof(GenderAndAge), string.Empty);

    public static readonly StyledProperty<string> IdentifierTextProperty =
        AvaloniaProperty.Register<PatientBanner, string>(nameof(IdentifierText), string.Empty);

    public static readonly StyledProperty<string> DiagnosisTextProperty =
        AvaloniaProperty.Register<PatientBanner, string>(nameof(DiagnosisText), string.Empty);

    public static readonly StyledProperty<bool> HasAllergyProperty =
        AvaloniaProperty.Register<PatientBanner, bool>(nameof(HasAllergy));

    public static readonly StyledProperty<string> AllergyTextProperty =
        AvaloniaProperty.Register<PatientBanner, string>(nameof(AllergyText), string.Empty);

    public static readonly StyledProperty<ICommand?> TimelineCommandProperty =
        AvaloniaProperty.Register<PatientBanner, ICommand?>(nameof(TimelineCommand));

    public static readonly StyledProperty<ICommand?> SwitchPatientCommandProperty =
        AvaloniaProperty.Register<PatientBanner, ICommand?>(nameof(SwitchPatientCommand));

    public bool HasPatient { get => GetValue(HasPatientProperty); set => SetValue(HasPatientProperty, value); }
    public string EncounterBadge { get => GetValue(EncounterBadgeProperty); set => SetValue(EncounterBadgeProperty, value); }
    public string PatientName { get => GetValue(PatientNameProperty); set => SetValue(PatientNameProperty, value); }
    public string GenderAndAge { get => GetValue(GenderAndAgeProperty); set => SetValue(GenderAndAgeProperty, value); }
    public string IdentifierText { get => GetValue(IdentifierTextProperty); set => SetValue(IdentifierTextProperty, value); }
    public string DiagnosisText { get => GetValue(DiagnosisTextProperty); set => SetValue(DiagnosisTextProperty, value); }
    public bool HasAllergy { get => GetValue(HasAllergyProperty); set => SetValue(HasAllergyProperty, value); }
    public string AllergyText { get => GetValue(AllergyTextProperty); set => SetValue(AllergyTextProperty, value); }
    public ICommand? TimelineCommand { get => GetValue(TimelineCommandProperty); set => SetValue(TimelineCommandProperty, value); }
    public ICommand? SwitchPatientCommand { get => GetValue(SwitchPatientCommandProperty); set => SetValue(SwitchPatientCommandProperty, value); }

    public PatientBanner()
    {
        InitializeComponent();
    }
}

