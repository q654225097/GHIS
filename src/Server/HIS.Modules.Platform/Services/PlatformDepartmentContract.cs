using System;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Platform.Models;

namespace HIS.Modules.Platform.Services;

public class PlatformDepartmentContract : IDepartmentReadOnlyContract
{
	private readonly IRepository<SysDepartment> _deptRepo;

	private readonly IRepository<SysDeptWardRelation> _relationRepo;

	public PlatformDepartmentContract(IRepository<SysDepartment> deptRepo, IRepository<SysDeptWardRelation> relationRepo)
	{
		_deptRepo = deptRepo;
		_relationRepo = relationRepo;
	}

	public async Task<DepartmentSummaryDto?> GetDeptSummaryAsync(string deptId, CancellationToken ct = default(CancellationToken))
	{
		SysDepartment? dept = await _deptRepo.GetByIdAsync(deptId, ct);
		if (dept == null)
		{
			return null;
		}
		Enum.TryParse<DepartmentCategory>(dept.DeptCategory, out var category);
		return new DepartmentSummaryDto(dept.Id, dept.CampusId, dept.DeptCode, dept.DeptName, category, dept.SubSpecialtyType, dept.DefaultPharmacyId, dept.RatedBedCount, dept.IsAllowAppointment == 1, dept.IsActive == 1);
	}

	public async Task<bool> IsDeptWardRelationValidAsync(string ipdDeptId, string wardId, CancellationToken ct = default(CancellationToken))
	{
		return (await _relationRepo.QueryAsync("SELECT * FROM sys_dept_ward_relations WHERE ipd_dept_id = @IpdDeptId AND ward_dept_id = @WardId AND is_active = 1", new
		{
			IpdDeptId = ipdDeptId,
			WardId = wardId
		}, ct)).Count > 0;
	}
}
