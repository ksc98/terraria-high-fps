using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace SmoothFrames;

/// <summary>
/// Some particles are spawned from drawing code rather than update code - torch flames and the dust
/// tile entities emit are the obvious ones. Once the game draws four times per update those spawn four
/// times as often, which reads as torches burning too fast and far too much dust.
/// <para/> Extra frames are drawing the same tick again, so particle spawns from them are dropped.
/// 6000 is the index vanilla itself returns for a dust that was never created.
/// </summary>
public sealed class DrawTimeParticleThrottle : ModSystem
{
	public override void Load()
	{
		On_Dust.NewDust += ThrottleDust;
	}

	private int ThrottleDust(On_Dust.orig_NewDust orig, Vector2 Position, int Width, int Height, int Type, float SpeedX, float SpeedY, int Alpha, Color newColor, float Scale)
	{
		if (FrameInterpolation.SuppressDrawTimeParticle)
			return 6000;

		return orig(Position, Width, Height, Type, SpeedX, SpeedY, Alpha, newColor, Scale);
	}
}
