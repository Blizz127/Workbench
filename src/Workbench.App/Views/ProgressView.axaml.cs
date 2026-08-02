using Avalonia.Controls;

namespace Workbench.App.Views;

/// <summary>
/// Responsive progress layout: Milestones and Frontier side by side on the
/// first row with Blockers spanning the second at wide widths; a single
/// stacked column at narrow widths. Avalonia has no container breakpoints,
/// so the grid is rearranged from <see cref="UserControl.SizeChanged"/>.
/// </summary>
public sealed partial class ProgressView : UserControl
{
    /// <summary>Content width at which the two-column layout kicks in.</summary>
    private const double WideBreakpoint = 980;

    private bool? _wide;

    public ProgressView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        var wide = Bounds.Width >= WideBreakpoint;
        if (_wide == wide)
            return;
        _wide = wide;

        if (wide)
        {
            ProgressGrid.ColumnDefinitions = new ColumnDefinitions("*,16,*");
            ProgressGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Place(MilestonesCard, column: 0, row: 0, columnSpan: 1);
            Place(FrontierCard, column: 2, row: 0, columnSpan: 1);
            Place(BlockersCard, column: 0, row: 1, columnSpan: 3);
        }
        else
        {
            ProgressGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ProgressGrid.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
            Place(MilestonesCard, column: 0, row: 0, columnSpan: 1);
            Place(FrontierCard, column: 0, row: 1, columnSpan: 1);
            Place(BlockersCard, column: 0, row: 2, columnSpan: 1);
        }
    }

    private static void Place(Avalonia.Controls.Control control, int column, int row, int columnSpan)
    {
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        Grid.SetColumnSpan(control, columnSpan);
    }
}
