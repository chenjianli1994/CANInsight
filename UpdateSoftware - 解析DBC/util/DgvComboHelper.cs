using System.Windows.Forms;

namespace PCAN_Client
{
    /// <summary>
    /// DataGridView 下拉列"单击即展开"。
    /// 默认编辑模式(EditOnKeystrokeOrF2)下，下拉单元格第一次单击只把该行选为当前行
    /// （下拉箭头此时才绘制出来），要再点一次才展开列表——表现为"要点好几下才出下拉框"。
    /// 这里在 CellMouseDown 时就把当前格切过去并进入编辑，随后的鼠标抬起由编辑控件（下拉框）接管，
    /// 于是单击即展开；文本框列不受影响（仍是单击选中、双击/键入才编辑），按钮列不受影响。
    /// </summary>
    internal static class DgvComboHelper
    {
        /// <summary>给表格接上下拉列单击展开（每张表调用一次）</summary>
        public static void EnableSingleClickDropDown(DataGridView dgv)
        {
            if (dgv == null) return;
            dgv.CellMouseDown += OnCellMouseDown;
        }

        private static void OnCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            var dgv = (DataGridView)sender;
            if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (!(dgv.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn)) return;
            if (dgv.IsCurrentCellInEditMode) return;   // 已在编辑：展开/收起交给编辑控件自己处理

            dgv.CurrentCell = dgv[e.ColumnIndex, e.RowIndex];
            dgv.BeginEdit(true);
            // BeginEdit(selectAll) 只对文本框列有意义，下拉列不保证展开列表，这里显式展开
            var combo = dgv.EditingControl as DataGridViewComboBoxEditingControl;
            if (combo != null && !combo.DroppedDown) combo.DroppedDown = true;
        }
    }
}
