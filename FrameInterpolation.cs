using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace SmoothFrames;

/// <summary>
/// The world updates at exactly 60 ticks per second. When the game draws more often than that, every
/// extra frame is identical to the last one, so a 240Hz display shows the same 60 distinct positions.
/// <para/> This records where everything was at the end of the last two updates and, while drawing,
/// moves entities to the point between them that matches how far through the current tick we are.
/// Positions are restored immediately after the draw, so no game logic ever sees an interpolated value.
/// </summary>
public sealed class FrameInterpolation : ModSystem
{
	private struct Snapshot
	{
		public Vector2 Position;
		public float Rotation;
		public bool Valid;
	}

	private static Snapshot[] _playerPrev, _playerCurr;
	/// <summary>
	/// Where the player's held item sits in the world. Drawing derives the item's position from
	/// <c>drawPosition + (itemLocation - position)</c>, so moving the player without moving this too
	/// leaves the sword hanging a tick's worth of movement away from the hand.
	/// </summary>
	private static Vector2[] _itemLocPrev, _itemLocCurr;
	private static Snapshot[] _npcPrev, _npcCurr;
	private static Snapshot[] _projPrev, _projCurr;
	private static Snapshot[] _itemPrev, _itemCurr;
	private static Snapshot[] _dustPrev, _dustCurr;

	// The camera is deliberately not interpolated. Main.DoDraw_UpdateCameraPosition derives screenPosition
	// from the local player's position during every draw, and that position is already interpolated here, so
	// the camera follows for free. Writing screenPosition directly was actively harmful: the value vanilla
	// computed mid-draw got replaced by a stale one on restore, and the next update reads it back for tile
	// targeting (tileTargetX = (mouseX + screenPosition.X) / 16), which is why mining hit the wrong tile.

	/// <summary> Set while entity positions hold interpolated values rather than simulation values. </summary>
	private static bool _applied;
	/// <summary> True for the first draw after a world update, false for every extra frame in between. </summary>
	private static bool _firstDrawAfterUpdate;
	private static bool _inDraw;
	private static uint _lastUpdateCount = uint.MaxValue;
	private static Terraria.Enums.FrameSkipMode? _savedFrameSkip;

	public override void Load()
	{
		_playerPrev = new Snapshot[Main.maxPlayers + 1];
		_playerCurr = new Snapshot[Main.maxPlayers + 1];
		_itemLocPrev = new Vector2[Main.maxPlayers + 1];
		_itemLocCurr = new Vector2[Main.maxPlayers + 1];
		_npcPrev = new Snapshot[Main.maxNPCs + 1];
		_npcCurr = new Snapshot[Main.maxNPCs + 1];
		_projPrev = new Snapshot[Main.maxProjectiles + 1];
		_projCurr = new Snapshot[Main.maxProjectiles + 1];
		_itemPrev = new Snapshot[Main.maxItems + 1];
		_itemCurr = new Snapshot[Main.maxItems + 1];
		_dustPrev = new Snapshot[Main.maxDust + 1];
		_dustCurr = new Snapshot[Main.maxDust + 1];

		On_Main.DoUpdateInWorld += CaptureAfterUpdate;
		On_Main.DoDraw += InterpolateAroundDraw;
	}

	public override void Unload()
	{
		On_Main.DoUpdateInWorld -= CaptureAfterUpdate;
		On_Main.DoDraw -= InterpolateAroundDraw;

		_playerPrev = _playerCurr = _npcPrev = _npcCurr = null;
		_itemLocPrev = _itemLocCurr = null;
		_projPrev = _projCurr = _itemPrev = _itemCurr = null;
		_dustPrev = _dustCurr = null;
		SmoothFramesConfig.Instance = null;
	}

	/// <summary> Vanilla drawing code spawns particles; those must not scale with the frame rate. </summary>
	internal static bool SuppressDrawTimeParticle
		=> _inDraw && !_firstDrawAfterUpdate && (SmoothFramesConfig.Instance?.ThrottleDrawTimeParticles ?? true);

	private void CaptureAfterUpdate(On_Main.orig_DoUpdateInWorld orig, Main self)
	{
		if (Main.GameUpdateCount % 60 == 0 && Main.LocalPlayer != null) {
			Player p = Main.LocalPlayer;
			ModContent.GetInstance<SmoothFrames>().Logger.Info(
				$"[aim] uiScale={Main.UIScale:F2} screen={Main.screenWidth}x{Main.screenHeight} " +
				$"mouse={Main.mouseX},{Main.mouseY} screenPos={Main.screenPosition.X:F1},{Main.screenPosition.Y:F1} " +
				$"expectTile={(int)((Main.mouseX + Main.screenPosition.X) / 16f)},{(int)((Main.mouseY + Main.screenPosition.Y) / 16f)} " +
				$"tileTarget={Player.tileTargetX},{Player.tileTargetY} playerTile={(int)(p.position.X / 16f)},{(int)(p.position.Y / 16f)} " +
				$"interp={(SmoothFramesConfig.Instance?.MotionInterpolation ?? true)}");
		}

		orig(self);

		if (!Enabled)
			return;

		for (int i = 0; i < _playerCurr.Length && i < Main.player.Length; i++) {
			_playerPrev[i] = _playerCurr[i];
			_itemLocPrev[i] = _itemLocCurr[i];

			Player player = Main.player[i];
			_playerCurr[i] = player != null && player.active ? new Snapshot { Position = player.position, Valid = true } : default;
			_itemLocCurr[i] = player?.itemLocation ?? default;
		}

		for (int i = 0; i < _npcCurr.Length && i < Main.npc.Length; i++) {
			_npcPrev[i] = _npcCurr[i];

			NPC npc = Main.npc[i];
			_npcCurr[i] = npc != null && npc.active ? new Snapshot { Position = npc.position, Rotation = npc.rotation, Valid = true } : default;
		}

		for (int i = 0; i < _projCurr.Length && i < Main.projectile.Length; i++) {
			_projPrev[i] = _projCurr[i];

			Projectile projectile = Main.projectile[i];
			_projCurr[i] = projectile != null && projectile.active ? new Snapshot { Position = projectile.position, Rotation = projectile.rotation, Valid = true } : default;
		}

		for (int i = 0; i < _itemCurr.Length && i < Main.item.Length; i++) {
			_itemPrev[i] = _itemCurr[i];

			WorldItem item = Main.item[i];
			_itemCurr[i] = item != null && item.active ? new Snapshot { Position = item.position, Valid = true } : default;
		}

		for (int i = 0; i < _dustCurr.Length && i < Main.dust.Length; i++) {
			_dustPrev[i] = _dustCurr[i];

			Dust dust = Main.dust[i];
			_dustCurr[i] = dust != null && dust.active ? new Snapshot { Position = dust.position, Rotation = dust.rotation, Valid = true } : default;
		}

	}

	private void InterpolateAroundDraw(On_Main.orig_DoDraw orig, Main self, GameTime gameTime)
	{
		_firstDrawAfterUpdate = Main.GameUpdateCount != _lastUpdateCount;
		_lastUpdateCount = Main.GameUpdateCount;
		_inDraw = true;

		if (SmoothFramesConfig.Instance?.ForceFrameSkipOff ?? true) {
			if (Main.gameMenu) {
				// No reason to render the menu hundreds of times a second; hand the setting back.
				if (_savedFrameSkip.HasValue) {
					Main.FrameSkipMode = _savedFrameSkip.Value;
					_savedFrameSkip = null;
				}
			}
			else {
				_savedFrameSkip ??= Main.FrameSkipMode;
				Main.FrameSkipMode = Terraria.Enums.FrameSkipMode.Off;
			}
		}

		bool apply = Enabled && !Main.gamePaused;
		try {
			Player local = Main.LocalPlayer;
			bool logSwing = _firstDrawAfterUpdate && !Main.gameMenu && local != null && (local.itemAnimation > 0 || Main.GameUpdateCount % 120 == 0);
			string before = logSwing ? $"style={local.HeldItem?.useStyle} type={local.HeldItem?.type} anim={local.itemAnimation}/{local.itemAnimationMax} rot={local.itemRotation:F3} loc={local.itemLocation.X:F1},{local.itemLocation.Y:F1} pos={local.position.X:F1},{local.position.Y:F1}" : null;

			if (apply)
				Apply(Alpha());

			if (logSwing)
				ModContent.GetInstance<SmoothFrames>().Logger.Info($"[swing] before {before} | after rot={local.itemRotation:F3} loc={local.itemLocation.X:F1},{local.itemLocation.Y:F1} pos={local.position.X:F1},{local.position.Y:F1} alpha={Alpha():F2} applied={apply}");

			orig(self, gameTime);
		}
		finally {
			if (_applied)
				Restore();

			_inDraw = false;
		}
	}

	private static bool Enabled
		=> !Main.gameMenu && !Main.dedServ && (SmoothFramesConfig.Instance?.MotionInterpolation ?? true);

	/// <summary>
	/// On a multiplayer client, entities owned by other people move in jumps as net updates land, and
	/// vanilla already smooths those with <see cref="Entity.netOffset"/>. Interpolating them as well
	/// fights that correction and shows up as teleporting, so only locally simulated things are touched.
	/// </summary>
	private static bool InterpolateRemoteEntities => Main.netMode != 1;

	/// <summary> How far the game is through the current update, 0 at the last update and 1 at the next one. </summary>
	private static float Alpha()
		=> (float)Math.Clamp(Main.UpdateTimeAccumulator / Main.TARGET_FRAME_TIME, 0.0, 1.0);

	private static void Apply(float alpha)
	{
		float threshold = SmoothFramesConfig.Instance?.TeleportThreshold ?? 160f;
		float thresholdSq = threshold * threshold;

		for (int i = 0; i < _playerCurr.Length && i < Main.player.Length && (SmoothFramesConfig.Instance?.InterpolatePlayers ?? true); i++) {
			if (!InterpolateRemoteEntities && i != Main.myPlayer)
				continue;

			if (!TryLerp(_playerPrev[i], _playerCurr[i], alpha, thresholdSq, out Vector2 pos, out _))
				continue;

			Main.player[i].position = pos;
			// Kept in step with the position so held items stay in the hand.
			Main.player[i].itemLocation = Vector2.Lerp(_itemLocPrev[i], _itemLocCurr[i], alpha);
		}

		for (int i = 0; i < _npcCurr.Length && i < Main.npc.Length && InterpolateRemoteEntities && (SmoothFramesConfig.Instance?.InterpolateNPCs ?? true); i++) {
			if (TryLerp(_npcPrev[i], _npcCurr[i], alpha, thresholdSq, out Vector2 pos, out float rot)) {
				Main.npc[i].position = pos;
				Main.npc[i].rotation = rot;
			}
		}

		for (int i = 0; i < _projCurr.Length && i < Main.projectile.Length && (SmoothFramesConfig.Instance?.InterpolateProjectiles ?? true); i++) {
			if (!InterpolateRemoteEntities && Main.projectile[i].owner != Main.myPlayer)
				continue;

			if (TryLerp(_projPrev[i], _projCurr[i], alpha, thresholdSq, out Vector2 pos, out float rot)) {
				Main.projectile[i].position = pos;
				Main.projectile[i].rotation = rot;
			}
		}

		for (int i = 0; i < _itemCurr.Length && i < Main.item.Length && InterpolateRemoteEntities && (SmoothFramesConfig.Instance?.InterpolateItems ?? true); i++) {
			if (TryLerp(_itemPrev[i], _itemCurr[i], alpha, thresholdSq, out Vector2 pos, out _))
				Main.item[i].position = pos;
		}

		for (int i = 0; i < _dustCurr.Length && i < Main.dust.Length && (SmoothFramesConfig.Instance?.InterpolateDust ?? true); i++) {
			if (TryLerp(_dustPrev[i], _dustCurr[i], alpha, thresholdSq, out Vector2 pos, out float rot)) {
				Main.dust[i].position = pos;
				Main.dust[i].rotation = rot;
			}
		}

		_applied = true;
	}

	private static void Restore()
	{
		for (int i = 0; i < _playerCurr.Length && i < Main.player.Length; i++) {
			if (_playerCurr[i].Valid) {
				Main.player[i].position = _playerCurr[i].Position;
				Main.player[i].itemLocation = _itemLocCurr[i];
			}
		}

		for (int i = 0; i < _npcCurr.Length && i < Main.npc.Length; i++) {
			if (_npcCurr[i].Valid) {
				Main.npc[i].position = _npcCurr[i].Position;
				Main.npc[i].rotation = _npcCurr[i].Rotation;
			}
		}

		for (int i = 0; i < _projCurr.Length && i < Main.projectile.Length; i++) {
			if (_projCurr[i].Valid) {
				Main.projectile[i].position = _projCurr[i].Position;
				Main.projectile[i].rotation = _projCurr[i].Rotation;
			}
		}

		for (int i = 0; i < _itemCurr.Length && i < Main.item.Length; i++) {
			if (_itemCurr[i].Valid)
				Main.item[i].position = _itemCurr[i].Position;
		}

		for (int i = 0; i < _dustCurr.Length && i < Main.dust.Length; i++) {
			if (_dustCurr[i].Valid) {
				Main.dust[i].position = _dustCurr[i].Position;
				Main.dust[i].rotation = _dustCurr[i].Rotation;
			}
		}

		_applied = false;
	}

	/// <summary>
	/// Produces the drawn position for one entity, or false when it should be left alone: it was not
	/// alive for both of the last two updates, or it moved far enough that this was a teleport rather
	/// than motion, in which case interpolating would drag it across the screen.
	/// </summary>
	private static bool TryLerp(in Snapshot prev, in Snapshot curr, float alpha, float thresholdSq, out Vector2 position, out float rotation)
	{
		position = default;
		rotation = default;

		if (!prev.Valid || !curr.Valid)
			return false;

		if (Vector2.DistanceSquared(prev.Position, curr.Position) > thresholdSq)
			return false;

		position = Vector2.Lerp(prev.Position, curr.Position, alpha);
		rotation = curr.Rotation + MathHelper.WrapAngle(curr.Rotation - prev.Rotation) * (alpha - 1f);
		return true;
	}
}
