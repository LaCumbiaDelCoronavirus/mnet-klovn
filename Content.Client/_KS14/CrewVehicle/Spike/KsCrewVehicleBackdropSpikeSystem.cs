using System.Globalization;
using System.Numerics;
using Robust.Client.Player;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Client._KS14.CrewVehicle.Spike;

// SPIKE: remove in milestone 5. Stands in for the crew-vehicle components that do not exist yet, so the backdrop
//      pass in ScalingViewport.Klovn.CrewVehicle.cs can be tried in a real client. Milestone 5 replaces it with the
//      viewer's interior map, its vehicle, and KsCrewVehicleInteriorTransform.

/// <summary>
///     Where the backdrop under the local player's map is drawn from, as set by <see cref="KsCrewVehicleBackdropSpikeCommand"/>.
/// </summary>
public sealed class KsCrewVehicleBackdropSpikeSystem : EntitySystem
{
    /// <summary>
    ///     The map drawn behind, or null for no backdrop.
    /// </summary>
    public MapId? BackdropMap;

    /// <summary>
    ///     The point on the viewer's own map that sits over <see cref="VehiclePosition"/>: a vehicle's interior anchor.
    /// </summary>
    public Vector2 Anchor;

    /// <summary>
    ///     Where the anchor lands on the backdrop map: the vehicle's world position.
    /// </summary>
    public Vector2 VehiclePosition;

    /// <summary>
    ///     How far the viewer's map is turned on the backdrop map: the vehicle's world rotation.
    /// </summary>
    public Angle VehicleRotation;
}

/// <summary>
///     <c>kscrewbackdrop &lt;mapId&gt; &lt;x&gt; &lt;y&gt; &lt;degrees&gt; [anchorX anchorY]</c>, or <c>kscrewbackdrop off</c>.
/// </summary>
public sealed partial class KsCrewVehicleBackdropSpikeCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public string Command => "kscrewbackdrop";

    public string Description => "SPIKE: draws another map behind your own, as if your map were a vehicle's interior.";

    public string Help => $"Usage: {Command} <mapId> <x> <y> <degrees> [anchorX anchorY] | {Command} off. " +
                          "The anchor defaults to where you stand.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var spikeSystem = _entityManager.System<KsCrewVehicleBackdropSpikeSystem>();

        if (args.Length == 1 && args[0] == "off")
        {
            spikeSystem.BackdropMap = null;
            return;
        }

        if ((args.Length != 4 && args.Length != 6) ||
            !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapNumber) ||
            !TryParse(args[1], out var x) ||
            !TryParse(args[2], out var y) ||
            !TryParse(args[3], out var degrees))
        {
            shell.WriteError(Help);
            return;
        }

        Vector2 anchor;
        if (args.Length == 6)
        {
            if (!TryParse(args[4], out var anchorX) || !TryParse(args[5], out var anchorY))
            {
                shell.WriteError(Help);
                return;
            }

            anchor = new Vector2(anchorX, anchorY);
        }
        else if (_playerManager.LocalEntity is { } localUid)
        {
            anchor = _entityManager.System<SharedTransformSystem>().GetWorldPosition(localUid);
        }
        else
        {
            shell.WriteError("No local entity to take the anchor from; give one.");
            return;
        }

        spikeSystem.BackdropMap = new MapId(mapNumber);
        spikeSystem.Anchor = anchor;
        spikeSystem.VehiclePosition = new Vector2(x, y);
        spikeSystem.VehicleRotation = Angle.FromDegrees(degrees);

        shell.WriteLine($"Backdrop: map {mapNumber} at ({x}, {y}) turned {degrees} degrees, anchor ({anchor.X}, {anchor.Y}).");
    }

    private static bool TryParse(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
