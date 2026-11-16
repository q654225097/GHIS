using System.Collections.Generic;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class DynamicFormSchemaDto
{
	public DynamicFormSchemaModel Schema { get; set; } = new DynamicFormSchemaModel();

	public List<DynamicFormFieldDto> Fields { get; set; } = new List<DynamicFormFieldDto>();
}
