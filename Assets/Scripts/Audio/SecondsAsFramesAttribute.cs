using UnityEngine;

/// <summary>
/// 标点时间数组的界面写法标记(2026-09-21 saika 定)。
///
/// 口径:**资产里存的永远是秒**(运行时 7 个文件都按秒读,不能动);
/// 只是 Inspector 这一层按「秒.帧@30」显示和输入 —— 你敲 9.08 存进去是 9.2667 秒,
/// 读出来显示回 9.08。换算只活在编辑器界面,数据本身还是秒。
///
/// 表格(CSV)那边同样以秒为准;两边口径一致,导入导出都不需要挑单位。
/// 见 Assets/Editor/SecondsAsFramesDrawer.cs。
/// </summary>
public class SecondsAsFramesAttribute : PropertyAttribute
{
}
