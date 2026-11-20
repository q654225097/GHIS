using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HIS.Client.UrsaShell.Printing;

namespace HIS.Client.UrsaShell.Stations.PrintDesigner;

public partial class PrintDesignerStationView : UserControl
{
    private PrintElementModel? _draggingElement;
    private Point _dragStartPointer;
    private double _dragStartXMm;
    private double _dragStartYMm;

    public PrintDesignerStationView()
    {
        InitializeComponent();
    }

    private void OnElementPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control ctrl && ctrl.DataContext is PrintElementModel el && DataContext is PrintDesignerViewModel vm)
        {
            vm.SelectedElement = el;
            _draggingElement = el;
            _dragStartPointer = e.GetPosition(PaperCanvasHost);
            _dragStartXMm = el.XMm;
            _dragStartYMm = el.YMm;
            e.Pointer.Capture(ctrl);
            e.Handled = true;
        }
    }

    private void OnElementPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingElement == null || DataContext is not PrintDesignerViewModel vm) return;
        var curr = e.GetPosition(PaperCanvasHost);
        double dxMm = (curr.X - _dragStartPointer.X) / Math.Max(0.5, vm.ZoomScale);
        double dyMm = (curr.Y - _dragStartPointer.Y) / Math.Max(0.5, vm.ZoomScale);

        double snapX = Math.Round((_dragStartXMm + dxMm) * 2.0) / 2.0; // 0.5mm 网格吸附
        double snapY = Math.Round((_dragStartYMm + dyMm) * 2.0) / 2.0;

        _draggingElement.XMm = Math.Clamp(snapX, 0, Math.Max(0, vm.EffectiveWidthMm - _draggingElement.WidthMm));
        _draggingElement.YMm = Math.Clamp(snapY, 0, Math.Max(0, vm.EffectiveHeightMm - _draggingElement.HeightMm));
        e.Handled = true;
    }

    private void OnElementPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingElement != null)
        {
            _draggingElement = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
