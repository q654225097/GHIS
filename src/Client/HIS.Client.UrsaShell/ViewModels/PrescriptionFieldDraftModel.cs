using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class PrescriptionFieldDraftModel : ClientModelBase
{
	private string _name = string.Empty;

	private string _placeholder = "请输入";

	private string _dataType = "STRING";

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			SetField(ref _name, value, "Name");
		}
	}

	public string Placeholder
	{
		get
		{
			return _placeholder;
		}
		set
		{
			SetField(ref _placeholder, value, "Placeholder");
		}
	}

	public string DataType
	{
		get
		{
			return _dataType;
		}
		set
		{
			SetField(ref _dataType, value, "DataType");
		}
	}
}
