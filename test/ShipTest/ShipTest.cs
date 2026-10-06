#nullable enable
// TEST HARNESS ONLY: never shipped, never part of a PR.
// Headless reproduction of the server-owned ship sail bug on a dedicated server with no players:
//  1. find deep ocean, point the server's reference position at it (ZoneSystem loads zones there),
//  2. add that area to the object list DedicatedServer builds from players only (postfix below),
//  3. spawn a longship (server-created, so server-owned), let it settle,
//  4. script sail changes through Ship.Forward/Backward/Stop (the same RPCs the helm sends),
//  5. log every physics tick: speed setting, sail position, hull height, vertical velocity,
//     and whether Ship.UpdateSailSize threw that tick.
// Output: BepInEx/shiptest.csv plus a summary per sail change in the BepInEx log.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("raidfix.harness.shiptest", "ShipTest", "0.0.1")]
public class ShipTest : BaseUnityPlugin
{
	private static Vector3? focus;                    // keep this area loaded and instantiated
	private static int nreThisTick;

	private enum Phase { WaitWorld, WaitZone, Settle, Script, Done }
	private Phase phase = Phase.WaitWorld;
	private float phaseStart;
	private Vector3 spot;
	private Ship? ship;
	private int step;
	private StreamWriter? csv;
	private float waterLevel;

	// (seconds after the script starts, sail setting) — Ship.Speed: 0 Stop, 3 Half, 4 Full; -1 ends.
	// Set directly: vanilla drops Slow/Back without a helmsman, and Slow is paddling with the sail down anyway.
	private static readonly (float t, int speed)[] Script =
	{
		(5, 3), (20, 4), (35, 3), (50, 0), (65, 4), (80, 0), (95, 3), (110, 0),
		(130, -1),
	};

	// Stand-in passenger. Vanilla CustomFixedUpdate sets m_speed = Stop whenever m_players is empty; with real
	// players someone is always aboard while sailing. Same mock as DedicatedServer's own local-player fixes.
	private static readonly Player passenger = new() { m_nview = new ZNetView() };

	// IsWindControllActive iterates passengers' status effects, which the mock has none of (Moder's power; unrelated).
	[HarmonyPatch(typeof(Ship), nameof(Ship.IsWindControllActive))]
	private static class NoWindControl
	{
		private static bool Prefix(ref bool __result) { __result = false; return false; }
	}

	private void Awake()
	{
		new Harmony("raidfix.harness.shiptest").PatchAll(typeof(ShipTest).Assembly);
		Application.logMessageReceived += (msg, stack, type) =>
		{
			if (type == LogType.Exception && stack != null && stack.Contains("Ship.UpdateSailSize"))
			{
				nreThisTick++;
			}
		};
	}

	// DedicatedServer's FindSectorObjects prefix builds the instantiate list from connected players only.
	// Add the test area (only for ZNetScene's list; CreateSyncList's call is left alone, as DedicatedServer does).
	[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.FindSectorObjects))]
	private static class KeepTestAreaInstantiated
	{
		private static void Postfix(ZDOMan __instance, List<ZDO> sectorObjects)
		{
			if (focus is not { } pos || ZNetScene.instance == null || sectorObjects != ZNetScene.instance.m_tempCurrentObjects)
			{
				return;
			}
			Vector2s c = ZoneSystem.GetZone(pos);
			for (int x = -1; x <= 1; x++)
			for (int y = -1; y <= 1; y++)
			{
				Vector2s s = new((short)(c.x + x), (short)(c.y + y));
				ZoneSystem.instance.PokeLocalZone(s);
				if (ZoneSystem.instance.m_zones.ContainsKey(s))
				{
					__instance.FindObjects(s, sectorObjects, __instance.m_visitedSectorIndices);
				}
			}
		}
	}

	private void Update()
	{
		if (ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null || ZNetScene.instance == null)
		{
			return;
		}
		float now = Time.time;
		switch (phase)
		{
			case Phase.WaitWorld:
				if (!ZoneSystem.instance.LocationsGenerated || now < 20f) return;
				waterLevel = ZoneSystem.instance.m_waterLevel;
				spot = FindDeepOcean();
				focus = spot;
				ZNet.instance.SetReferencePosition(spot);
				Logger.LogInfo($"deep ocean at {spot}, ground {WorldGenerator.instance.GetHeight(spot.x, spot.z):F1} m, water level {waterLevel:F1} m");
				Next(Phase.WaitZone);
				return;

			case Phase.WaitZone:
				if (!ZoneSystem.instance.IsZoneLoaded(spot))
				{
					if (now - phaseStart > 120f) { Logger.LogError("zone never loaded"); Next(Phase.Done); }
					return;
				}
				foreach (Ship old in FindObjectsOfType<Ship>())
				{
					Logger.LogInfo($"removing leftover ship at {old.transform.position}");
					ZNetScene.instance.Destroy(old.gameObject);
				}
				GameObject prefab = ZNetScene.instance.GetPrefab("VikingShip");
				GameObject go = Instantiate(prefab, new Vector3(spot.x, waterLevel + 0.5f, spot.z), Quaternion.identity);
				ship = go.GetComponent<Ship>();
				ship.m_players.Add(passenger);
				Rigidbody b = ship.m_body;
				Logger.LogInfo($"spawned {prefab.name}: owner is server = {ship.m_nview.IsOwner()}, hasSail = {ship.m_hasSail}, " +
					$"useGravity = {b.useGravity}, mass = {b.mass}, damping = {b.linearDamping}, maxDepenetrationVelocity = {b.maxDepenetrationVelocity}");
				Next(Phase.Settle);
				return;

			case Phase.Settle:
				if (ship == null) { Logger.LogError("ship vanished while settling"); Next(Phase.Done); return; }
				if (now - phaseStart < 20f) return;
				string path = Path.Combine(Paths.BepInExRootPath, "shiptest.csv");
				csv = new StreamWriter(path, false);
				csv.WriteLine("t,speed,sail,y,vy,nre,owner,health");
				Logger.LogInfo($"settled at y={ship.transform.position.y:F2}; script starts, logging to {path}");
				step = 0;
				Next(Phase.Script);
				return;

			case Phase.Script:
				if (ship == null) { Logger.LogError("ship vanished during the script"); Finish(); return; }
				// follow the ship so its area stays loaded while it sails
				focus = ship.transform.position;
				ZNet.instance.SetReferencePosition(ship.transform.position);
				if (step < Script.Length && now - phaseStart >= Script[step].t)
				{
					int speed = Script[step++].speed;
					if (speed < 0) { Finish(); return; }
					Summarize();
					ship.m_speed = (Ship.Speed)speed;
					Logger.LogInfo($"t={now - phaseStart:F0} s: sail -> {ship.m_speed}");
				}
				return;
		}
	}

	// per physics tick, after the ship's own update ran (MonoUpdaters run in FixedUpdate before late script order)
	private float segMinY = float.MaxValue, segMaxY = float.MinValue, segMinVy = float.MaxValue, segMaxVy = float.MinValue;
	private int segNre;

	private void FixedUpdate()
	{
		if (phase != Phase.Script || ship == null || csv == null) return;
		Vector3 p = ship.transform.position;
		float vy = ship.m_body.linearVelocity.y;
		csv.WriteLine($"{Time.time - phaseStart:F3},{(int)ship.m_speed},{ship.m_sailPosition:F3},{p.y:F3},{vy:F3},{nreThisTick},{(ship.m_nview.IsOwner() ? 1 : 0)},{ship.m_nview.GetZDO().GetFloat(ZDOVars.s_health, -1f):F1}");
		segMinY = Mathf.Min(segMinY, p.y); segMaxY = Mathf.Max(segMaxY, p.y);
		segMinVy = Mathf.Min(segMinVy, vy); segMaxVy = Mathf.Max(segMaxVy, vy);
		segNre += nreThisTick;
		nreThisTick = 0;
	}

	private void Summarize()
	{
		if (segMinY == float.MaxValue) return;
		float hp = ship != null ? ship.m_nview.GetZDO().GetFloat(ZDOVars.s_health, -1f) : -1f;
		Logger.LogInfo($"  segment: sail errors {segNre}, health {hp:F0}, hull y {segMinY:F2}..{segMaxY:F2} m (range {segMaxY - segMinY:F2}), vy {segMinVy:F2}..{segMaxVy:F2} m/s");
		segMinY = segMinVy = float.MaxValue; segMaxY = segMaxVy = float.MinValue; segNre = 0;
	}

	private void Finish()
	{
		Summarize();
		csv?.Flush(); csv?.Dispose(); csv = null;
		if (ship != null) ZNetScene.instance.Destroy(ship.gameObject);
		Logger.LogInfo("SHIPTEST DONE");
		Next(Phase.Done);
	}

	private void Next(Phase p) { phase = p; phaseStart = Time.time; }

	private Vector3 FindDeepOcean()
	{
		for (float r = 1500f; r < 9000f; r += 250f)
		for (float a = 0f; a < 360f; a += 10f)
		{
			float x = r * Mathf.Cos(a * Mathf.Deg2Rad), z = r * Mathf.Sin(a * Mathf.Deg2Rad);
			if (WorldGenerator.instance.GetBiome(x, z) != Heightmap.Biome.Ocean) continue;
			// the whole 3x3 zone block around it must be deep water too
			bool deep = true;
			for (int dx = -96; dx <= 96 && deep; dx += 32)
			for (int dz = -96; dz <= 96 && deep; dz += 32)
				deep = WorldGenerator.instance.GetHeight(x + dx, z + dz) < waterLevel - 20f;
			if (deep) return new Vector3(x, waterLevel, z);
		}
		throw new Exception("no deep ocean found");
	}
}
