namespace Content.Shared.Damage.Components;

public sealed partial class DamageContactsComponent
{
    /// <summary>
    ///     If true, whoever touches this is only damaged while moving: standing still in it is safe, and the first step
    ///         after standing still hurts at once if a damage tick is due.
    /// </summary>
    /// <seealso cref="Content.Shared.Damage.Systems.DamageContactsSystem.KsMovingSpeed"/>
    [DataField]
    public bool OnlyWhileMoving;

    /// <summary>
    ///     Sources with the same group never hurt the same entity at once: touching several of them hurts like touching
    ///         one, whichever was touched first, and when that one is left the next takes over. Sources of different
    ///         groups each hurt for themselves. Null is no group of its own: of all the sources without one that an
    ///         entity touches, one hurts it, as upstream has it.
    /// </summary>
    [DataField]
    public string? ExclusiveGroup;
}
