using UnityEngine;

namespace SniperDuel.Game.Ui
{
    /// <summary>The reticle etched in a scope's glass.</summary>
    public enum ReticleKind
    {
        /// <summary>First-focal-plane mil hashes, numbered every 2 mil, heavy posts from 10 mil.</summary>
        MilHash,
        /// <summary>The classic mil-dot: a dot at every mil out to 4, heavy posts from 5 mil.</summary>
        MilDot,
        /// <summary>A "Christmas tree" holdover grid: rows of dots a mil apart below the centre.</summary>
        Tree,
        /// <summary>A heavy duplex: thick posts close in, a fine centre cross and a lit red dot.</summary>
        Duplex,
    }

    /// <summary>
    /// How one rifle's scope looks through the eyepiece, after the optic that rifle carries in
    /// service: the reticle, the colour the glass's coatings give the picture, the light it loses,
    /// the size of the exit pupil in the tube and the tube's metal. The zoom range still comes from
    /// the gunsmith's optic part.
    /// </summary>
    public sealed class ScopeStyle
    {
        public string Name = "";
        public ReticleKind Reticle;
        /// <summary>Colour filter the glass puts on the picture (white: none).</summary>
        public Color Glass = Color.white;
        /// <summary>Exposure change through the glass, stops (negative: the light it loses).</summary>
        public float Exposure;
        /// <summary>Clear glass radius against the standard eyepiece.</summary>
        public float Pupil = 1;
        /// <summary>Outer edge of the tube, in glass radii.</summary>
        public float Tube = 1.32f;
        /// <summary>Where the dark fall-off at the edge of the glass begins, in glass radii.</summary>
        public float Falloff = 0.76f;
        /// <summary>Tint on the tube's metal.</summary>
        public Color Metal = Color.white;
        /// <summary>Strength of the glare sliver on the glass (1: standard).</summary>
        public float Glare = 1;
        /// <summary>Reticle ink.</summary>
        public Color Ink = new Color(0.02f, 0.02f, 0.02f, 0.96f);

        /// <summary>
        /// The .308 (an M40A5-type rifle): a 3–12×50 tactical scope with the mil-dot reticle of the
        /// US Marine Corps; older coatings with a faint green cast, a smaller eyepiece and a softer edge.
        /// </summary>
        public static readonly ScopeStyle MilDot308 = new ScopeStyle
        {
            Name = "3–12×50 MIL-DOT",
            Reticle = ReticleKind.MilDot,
            Glass = new Color(0.95f, 1f, 0.93f),
            Exposure = -0.06f,
            Pupil = 0.92f,
            Tube = 1.4f,
            Falloff = 0.7f,
            Metal = new Color(0.95f, 1f, 0.95f),
            Glare = 1.3f,
        };

        /// <summary>
        /// The 6.5 Creedmoor (an FN SPR-type precision rifle): a modern 5–25×56 competition scope
        /// with a Christmas-tree holdover grid; cool, very clear glass with little glare.
        /// </summary>
        public static readonly ScopeStyle Tree65 = new ScopeStyle
        {
            Name = "5–25×56 TREE GRID",
            Reticle = ReticleKind.Tree,
            Glass = new Color(0.95f, 0.99f, 1.04f),
            Exposure = 0.02f,
            Pupil = 1.02f,
            Tube = 1.26f,
            Falloff = 0.8f,
            Metal = new Color(0.92f, 0.95f, 1.02f),
            Glare = 0.6f,
        };

        /// <summary>
        /// The .338 Lapua (an L115A3-type rifle): the 5–25×56 first-focal-plane mil hash scope it
        /// carries in British service, with neutral, slightly warm glass. The reference look.
        /// </summary>
        public static readonly ScopeStyle MilHash338 = new ScopeStyle
        {
            Name = "5–25×56 FFP MIL HASH",
            Reticle = ReticleKind.MilHash,
            Glass = new Color(1f, 0.985f, 0.95f),
        };

        /// <summary>
        /// The .50 (a Hecate II-type anti-materiel rifle): the French army's heavy fixed-power
        /// optic look, a thick duplex with a lit red centre, amber-coated glass that loses a little
        /// light, a narrow eyepiece in a thick tube and a hard dark edge.
        /// </summary>
        public static readonly ScopeStyle Duplex50 = new ScopeStyle
        {
            Name = "HEAVY DUPLEX, LIT CENTRE",
            Reticle = ReticleKind.Duplex,
            Glass = new Color(1.04f, 0.97f, 0.84f),
            Exposure = -0.1f,
            Pupil = 0.88f,
            Tube = 1.46f,
            Falloff = 0.66f,
            Metal = new Color(1f, 0.98f, 0.9f),
            Glare = 1.1f,
        };

        /// <summary>The scope fitted to a rifle, by the rifle's base id.</summary>
        public static ScopeStyle For(string rifleId) => rifleId switch
        {
            "bolt308" => MilDot308,
            "bolt65cm" => Tree65,
            "am50" => Duplex50,
            _ => MilHash338,
        };

        /// <summary>The scope the player is looking through now.</summary>
        public static ScopeStyle Current { get; private set; } = MilHash338;

        /// <summary>Fits the rifle's scope: the reticle and eyepiece redraw and the glass colours the picture.</summary>
        public static void Use(string rifleId)
        {
            Current = For(rifleId);
            World.PostFx.Glass(Current.Glass, Current.Exposure);
        }
    }
}
