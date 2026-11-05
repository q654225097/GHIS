namespace HIS.Contracts;

/// <summary>
/// 四维科室与部门类别
/// </summary>
public enum DepartmentCategory
{
    OPD = 1,       // 门诊科室
    IPD = 2,       // 住院科室
    MEDTECH = 3,   // 医技科室
    WARD = 4,      // 护理病区
    PHARMACY = 5,  // 药房
    WAREHOUSE = 6, // 药库
    FINANCE = 7,   // 财务
    ADMIN = 8      // 行政
}

/// <summary>
/// 医技子专业细分
/// </summary>
public enum MedTechSubType
{
    LAB,         // 检验科
    RADIOLOGY,   // 放射影像科
    ULTRASOUND,  // 超声科
    ENDOSCOPY,   // 内镜中心
    PATHOLOGY,   // 病理科
    ECG_EEG,     // 心电/脑电
    TREATMENT,   // 理疗治疗
    OR_ANES,     // 手术麻醉
    BLOOD_BANK   // 输血科
}

/// <summary>
/// 住院科室与护理病区 N:M 映射类型
/// </summary>
public enum DeptWardRelationType
{
    PRIMARY,        // 主收治病区
    SHARED,         // 多科共用综合病区
    BORROW_ALLOWED  // 允许满床跨病区借床
}

/// <summary>
/// 六维数据范围控制
/// </summary>
public enum DataScope
{
    ALL_CAMPUS,              // 全院/全院区范围
    OPD_DEPT_SCOPE,          // 本门诊科室范围
    IPD_DEPT_AND_BORROWED,   // 本住院科室范围 (含借床)
    NURSING_WARD_SCOPE,      // 本护理病区范围
    MEDTECH_EXEC_SCOPE,      // 本医技执行科室范围
    PATIENT_SELF_SCOPE       // 患者本人范围
}

/// <summary>
/// 单个 Ursa UI 控件 / 单个病历章节 / 单个数据元 四态访问策略
/// </summary>
public enum ControlAccessPolicy
{
    Hidden = 0,     // 0-隐藏
    Masked = 1,     // 1-脱敏只读
    ReadOnly = 2,   // 2-可见禁用/只读
    FullAccess = 3  // 3-完全可交互
}

/// <summary>
/// 权限资源类别
/// </summary>
public enum ResourceType
{
    MODULE,
    VIEW,
    COMPONENT,
    EMR_SECTION,
    EMR_ELEMENT,
    API
}

/// <summary>
/// 皮试状态闭环
/// </summary>
public enum SkinTestStatus
{
    NotRequired = 0,
    Required = 1,
    Pending = 2,
    Negative = 3, // 阴性（-）允许继续
    Positive = 4  // 阳性（+）严禁执行、退药并标引患者终身过敏
}

/// <summary>
/// 电子病历审签流转状态
/// </summary>
public enum EmrDocumentStatus
{
    Draft = 0,
    ResidentSigned = 1,
    AttendingSigned = 2,
    ChiefLocked = 3,
    Archived = 4,
    ReturnedForCorrection = 5
}

/// <summary>
/// 结构化病历章节渲染模式
/// </summary>
public enum SectionRenderMode
{
    GRID_FORM,
    INLINE_PARAGRAPH,
    TABLE_GRID,
    VECTOR_CANVAS
}

/// <summary>
/// 性别排他约束
/// </summary>
public enum GenderExclusiveRule
{
    ALL,
    MALE_ONLY,
    FEMALE_ONLY
}

/// <summary>
/// 住院就诊状态
/// </summary>
public enum EncounterStatus
{
    Admitted = 1,          // 在院
    DischargePending = 2,  // 关床六项核查通过，待结算
    Discharged = 3,        // 已结算离院
    Recalled = 4           // 召回重入
}

/// <summary>
/// 医嘱状态
/// </summary>
public enum OrderStatus
{
    Draft = 0,
    Verified = 1,
    Executing = 2,
    Stopped = 3,
    Discontinued = 4,
    Rejected = 5
}

/// <summary>
/// 医嘱长期/临时期限
/// </summary>
public enum OrderTermType
{
    Standing = 1, // 长期医嘱
    Stat = 2,     // 临时医嘱
    PRN = 3,      // 备用长期
    SOS = 4       // 备用临时
}

