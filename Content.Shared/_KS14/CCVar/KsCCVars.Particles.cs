using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether particle effects are simulated and drawn at all.
    /// </summary>
    public static readonly CVarDef<bool> ParticlesEnabled =
        CVarDef.Create("klovn.particles.enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Scales how many particles every effect spawns and may keep alive, 0 to 1.
    /// </summary>
    public static readonly CVarDef<float> ParticlesDensity =
        CVarDef.Create("klovn.particles.density", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     The most particles alive across every effect on this client. Emitters stop spawning at this many.
    /// </summary>
    public static readonly CVarDef<int> ParticlesMaxAlive =
        CVarDef.Create("klovn.particles.max_alive", 20000, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether ground particles (dust, snow and splashes kicked up by wheels, tracks and feet) spawn only near what
    ///         was drawn last frame. Off, they spawn everywhere: for a headless client, which draws nothing.
    /// </summary>
    public static readonly CVarDef<bool> ParticlesGroundCull =
        CVarDef.Create("klovn.particles.ground_cull", true, CVar.CLIENTONLY);
}
