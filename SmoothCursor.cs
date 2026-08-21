using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace SmoothFrames;

/// <summary>
/// The mouse is polled once per world update, so the in-game cursor moves in 60Hz steps no matter how
/// often the game draws. Re-reading the hardware position at the start of every draw gives the cursor the
/// full frame rate and cuts input latency, since the next update then reads the most recent position.
/// </summary>
public sealed class SmoothCursor : ModSystem
{
	public override void Load()
	{
		On_PlayerInput.UpdateMainMouse += RefreshMouseEveryFrame;
	}

	public override void Unload()
	{
		On_PlayerInput.UpdateMainMouse -= RefreshMouseEveryFrame;
	}

	private void RefreshMouseEveryFrame(On_PlayerInput.orig_UpdateMainMouse orig)
	{
		// Gamepads synthesise a cursor position through PlayerInput, so leave those alone.
		bool refresh = (SmoothFramesConfig.Instance?.SmoothCursor ?? true) && !Main.dedServ && !PlayerInput.UsingGamepad;
		if (refresh) {
			MouseState mouse = Mouse.GetState();
			PlayerInput.MouseX = (int)(mouse.X * PlayerInput.RawMouseScale.X);
			PlayerInput.MouseY = (int)(mouse.Y * PlayerInput.RawMouseScale.Y);
		}

		orig();

		// The zoom helpers rebuild Main.mouseX from a position cached during the update, which would
		// undo the refresh above on every SetZoom call in this draw. Re-cache from the fresh value.
		if (refresh)
			PlayerInput.CacheMousePositionForZoom();
	}
}
