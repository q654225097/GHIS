using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace HIS.Client.UrsaShell.Stations.WorkflowDesigner;

public partial class WorkflowDesignerStationView : UserControl
{
    private bool _isDragging;
    private Point _lastPointerPosition;
    private WorkflowNodeModel? _draggedNode;

    public WorkflowDesignerStationView()
    {
        InitializeComponent();
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is WorkflowNodeModel node)
        {
            _isDragging = true;
            _draggedNode = node;
            _lastPointerPosition = e.GetPosition(this);
            e.Pointer.Capture(border);

            if (DataContext is WorkflowDesignerViewModel vm)
            {
                vm.SelectNodeCommand.Execute(node);
            }
            e.Handled = true;
        }
    }

    private void OnNodePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging && _draggedNode != null && sender is Border border)
        {
            var currentPos = e.GetPosition(this);
            double deltaX = currentPos.X - _lastPointerPosition.X;
            double deltaY = currentPos.Y - _lastPointerPosition.Y;
            _lastPointerPosition = currentPos;

            if (DataContext is WorkflowDesignerViewModel vm)
            {
                vm.MoveNodeByDelta(_draggedNode, deltaX, deltaY);
            }
            e.Handled = true;
        }
    }

    private void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && sender is Border border)
        {
            _isDragging = false;
            _draggedNode = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
