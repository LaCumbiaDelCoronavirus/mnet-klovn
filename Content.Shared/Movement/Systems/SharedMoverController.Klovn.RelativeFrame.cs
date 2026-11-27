using Content.Shared._KS14.Movement;
using Content.Shared.Movement.Components;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private EntityQuery<KsMoverRelativeFrameComponent> _ksRelativeFrameQuery = default!;

    /// <summary>
    ///     The transform a mover's frame is read from: that of its <see cref="KsMoverRelativeFrameComponent.Frame"/> if
    ///         it borrows one, else its own. <see cref="TryUpdateRelative"/> reads only the grid and map from it.
    /// </summary>
    private TransformComponent KsFrameTransform(EntityUid uid, TransformComponent ownTransform)
    {
        if (!_ksRelativeFrameQuery.TryComp(uid, out var frameComponent) ||
            frameComponent.LifeStage > ComponentLifeStage.Running ||
            !XformQuery.TryComp(frameComponent.Frame, out var frameTransform) ||
            frameTransform.MapUid == null)
            return ownTransform;

        return frameTransform;
    }

    /// <summary>
    ///     Puts a mover straight back in the frame of where it is itself, at <paramref name="relativeRotation"/>, with no
    ///         easing: for when the view it borrowed a frame for is gone at once.
    /// </summary>
    public void KsSnapToOwnFrame(Entity<InputMoverComponent?> mover, Angle relativeRotation)
    {
        if (!MoverQuery.Resolve(mover, ref mover.Comp, logMissing: false) ||
            !XformQuery.TryComp(mover, out var transformComponent))
            return;

        mover.Comp.RelativeEntity = transformComponent.GridUid ?? transformComponent.MapUid;
        mover.Comp.RelativeRotation = relativeRotation;
        mover.Comp.TargetRelativeRotation = relativeRotation;
        mover.Comp.LerpTarget = TimeSpan.Zero;
        Dirty(mover.Owner, mover.Comp);
    }

    /// <summary>
    ///     Puts a mover that has just borrowed a frame straight into it, at its default: as the align-camera key leaves
    ///         someone standing on that grid. Changes of the frame after this ease as a mob's do.
    /// </summary>
    public void KsSnapToBorrowedFrame(Entity<InputMoverComponent?> mover)
    {
        if (!MoverQuery.Resolve(mover, ref mover.Comp, logMissing: false) ||
            !XformQuery.TryComp(mover, out var ownTransform))
            return;

        var frameTransform = KsFrameTransform(mover, ownTransform);
        mover.Comp.RelativeEntity = frameTransform.GridUid ?? frameTransform.MapUid;
        mover.Comp.RelativeRotation = Angle.Zero;
        mover.Comp.TargetRelativeRotation = Angle.Zero;
        mover.Comp.LerpTarget = TimeSpan.Zero;
        Dirty(mover.Owner, mover.Comp);
    }

    /// <summary>
    ///     A mover relaying its keys to another never updates its own frame upstream, which is copied onto the one it
    ///         relays to. One that borrows its frame still follows it, so a driver looking out of a vehicle turns with
    ///         the grid under the vehicle.
    /// </summary>
    private void KsUpdateRelayedFrame(EntityUid uid, InputMoverComponent mover)
    {
        if (!_ksRelativeFrameQuery.HasComp(uid) ||
            mover.LerpTarget >= Timing.CurTime ||
            !XformQuery.TryComp(uid, out var ownTransform))
            return;

        TryUpdateRelative(uid, mover, KsFrameTransform(uid, ownTransform));
    }
}
