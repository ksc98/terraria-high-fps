using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace SmoothFrames;

public class SmoothFramesConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ClientSide;

	public static SmoothFramesConfig Instance;

	[DefaultValue(true)]
	public bool MotionInterpolation { get; set; }

	/// <summary>
	/// Re-reads the mouse position every frame instead of once per world update, so the cursor moves at
	/// the full frame rate.
	/// </summary>
	[DefaultValue(true)]
	public bool SmoothCursor { get; set; }

	[DefaultValue(true)]
	public bool InterpolatePlayers { get; set; }

	[DefaultValue(true)]
	public bool InterpolateNPCs { get; set; }

	[DefaultValue(true)]
	public bool InterpolateProjectiles { get; set; }

	[DefaultValue(true)]
	public bool InterpolateItems { get; set; }

	[DefaultValue(true)]
	public bool InterpolateDust { get; set; }

	/// <summary>
	/// Frame skip has to be Off for the game to draw more often than it updates: the "Subtle" mode
	/// busy-waits in Main.EndDraw until a whole update's worth of time has passed.
	/// </summary>
	[DefaultValue(true)]
	public bool ForceFrameSkipOff { get; set; }

	/// <summary>
	/// Entities that move further than this in a single update are treated as having teleported and
	/// are drawn at their new position instead of being smeared across the gap.
	/// </summary>
	[Range(16f, 2000f)]
	[DefaultValue(160f)]
	public float TeleportThreshold { get; set; }

	/// <summary>
	/// Vanilla spawns some particles from drawing code (torch flames, tile entity dust). Those would
	/// scale with the frame rate, so they are only allowed through on the first draw after an update.
	/// </summary>
	[DefaultValue(true)]
	public bool ThrottleDrawTimeParticles { get; set; }

	public override void OnLoaded() => Instance = this;

	// Static references into the mod's own assembly keep it from unloading cleanly on a mod reload.
	public override void OnChanged()
	{
		Instance = this;
	}
}
