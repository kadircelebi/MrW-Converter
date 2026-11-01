using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MrWConverter.App.ViewModels;

namespace MrWConverter.App.Wheel;

/// <summary>
/// One slice of the wheel. It is a real button, so the keyboard and screen readers
/// work on it; during a drag the window highlights it from the pointer position.
/// </summary>
public sealed class WheelSector : Button
{
    public static readonly DependencyProperty IsChosenProperty =
        DependencyProperty.Register(nameof(IsChosen), typeof(bool), typeof(WheelSector));

    public static readonly DependencyProperty ShapeProperty =
        DependencyProperty.Register(nameof(Shape), typeof(Geometry), typeof(WheelSector));

    public WheelSector(WheelChoice choice)
    {
        Choice = choice;
        Content = choice.Label;
    }

    public WheelChoice Choice { get; }

    public bool IsChosen
    {
        get => (bool)GetValue(IsChosenProperty);
        set => SetValue(IsChosenProperty, value);
    }

    public Geometry? Shape
    {
        get => (Geometry?)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }
}
