using Content.Shared._KS14.Fluidmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Fluidmos;

/// <summary>
///     A table of the reagents a gas mixture carries, by phase: the fluid half of the gas analyzer's and canister's
///         readouts. Hidden while there is nothing to list.
/// </summary>
public sealed partial class KsFluidListControl : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly GridContainer _table;

    public KsFluidListControl()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Vertical;
        Visible = false;

        AddChild(new Label
        {
            Text = Loc.GetString("ks-fluid-list-header"),
            StyleClasses = { "LabelHeading" },
        });

        _table = new GridContainer { Columns = 4, HSeparationOverride = 10 };
        AddChild(_table);
    }

    public void SetEntries(KsFluidEntry[]? entries)
    {
        _table.RemoveAllChildren();
        Visible = entries is { Length: > 0 };
        if (entries == null)
            return;

        AddHeader("ks-fluid-list-column-reagent", alignRight: false);
        AddHeader("ks-fluid-list-column-liquid", alignRight: true);
        AddHeader("ks-fluid-list-column-solid", alignRight: true);
        AddHeader("ks-fluid-list-column-vapor", alignRight: true);

        foreach (var entry in entries)
        {
            var name = _prototypeManager.TryIndex(entry.Reagent, out var reagentPrototype)
                ? reagentPrototype.LocalizedName
                : entry.Reagent.Id;

            _table.AddChild(new Label { Text = name, HorizontalExpand = true });
            AddAmount(entry.Liquid);
            AddAmount(entry.Solid);
            AddAmount(entry.Vapor);
        }
    }

    private void AddHeader(string key, bool alignRight)
    {
        _table.AddChild(new Label
        {
            Text = Loc.GetString(key),
            Align = alignRight ? Label.AlignMode.Right : Label.AlignMode.Left,
            FontColorOverride = Color.Gray,
        });
    }

    private void AddAmount(float units)
    {
        _table.AddChild(new Label
        {
            Text = units >= KsFluidEntry.MinUnits ? Loc.GetString("ks-fluid-list-units", ("units", $"{units:0.##}")) : "-",
            Align = Label.AlignMode.Right,
        });
    }
}
