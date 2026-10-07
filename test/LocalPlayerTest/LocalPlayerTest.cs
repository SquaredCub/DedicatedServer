#nullable enable
// TEST HARNESS ONLY: never shipped, never part of a PR.
// Headless check of the two server-side local-player fixes, on a dedicated server with no players.
// Once the world is up:
//  1. Leviathan: spawn one (server-created, so server-owned) and call Leave(), as a hit reaction does.
//     Logs whether Leave() threw and whether "RPC_Left" was handed on towards the clients
//     (ZRoutedRpc.RouteRPC, counted by a postfix): that RPC is what credits nearby players' dive stat.
//  2. Ashlands ocean: spawn a Karve on the boiling sea, clear the AshlandsOcean global key, and run
//     Ship.TakeAshlandsDamage with nobody aboard, then with a player aboard. Logs the key after each.
// Then it quits the server. Output: "LPTEST" lines in BepInEx/LogOutput.log.
using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("squaredcub.test.localplayer", "LocalPlayerTest", "0.0.1")]
public class LocalPlayerTest : BaseUnityPlugin
{
	private static int routedLeft;
	private bool done;

	[HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.RouteRPC))]
	private static class CountRoutedLeft
	{
		private static void Postfix(ZRoutedRpc.RoutedRPCData rpcData)
		{
			if (rpcData.m_methodHash == "RPC_Left".GetStableHashCode())
			{
				routedLeft++;
			}
		}
	}

	private void Awake() => new Harmony("squaredcub.test.localplayer").PatchAll(typeof(CountRoutedLeft));

	private void Update()
	{
		if (done || Time.realtimeSinceStartup < 20 || ZNet.instance == null || !ZNet.instance.IsServer() || ZNetScene.instance == null || ZoneSystem.instance == null)
		{
			return;
		}
		done = true;
		Logger.LogInfo($"LPTEST server build: DedicatedServer {BepInEx.Bootstrap.Chainloader.PluginInfos["org.bepinex.plugins.dedicatedserver"].Location}");
		try { Leviathan(); } catch (Exception e) { Logger.LogError("LPTEST leviathan harness error: " + e); }
		try { Ashlands(); } catch (Exception e) { Logger.LogError("LPTEST ashlands harness error: " + e); }
		Logger.LogInfo("LPTEST DONE");
		Application.Quit();
	}

	private void Leviathan()
	{
		GameObject go = Instantiate(ZNetScene.instance.GetPrefab("Leviathan"), new Vector3(0, 30, 0), Quaternion.identity);
		Leviathan lev = go.GetComponent<Leviathan>();
		Logger.LogInfo($"LPTEST leviathan owned by server: {lev.m_nview.IsOwner()}");
		string threw = "no";
		try { lev.Leave(); }
		catch (Exception e) { threw = e.GetType().Name; }
		Logger.LogInfo($"LPTEST leviathan Leave() threw: {threw}; m_left={lev.m_left}; RPC_Left routed to clients: {routedLeft}");
		ZNetScene.instance.Destroy(go);
	}

	private void Ashlands()
	{
		Vector3 pos = Vector3.zero;
		for (float z = -1000; z > -12000; z -= 100)
		{
			if (WorldGenerator.GetAshlandsOceanGradient(0, z) > 0.5f)
			{
				pos = new Vector3(0, 30, z);
				break;
			}
		}
		Logger.LogInfo($"LPTEST ashlands spot {pos}, gradient {WorldGenerator.GetAshlandsOceanGradient(pos)}");
		GameObject go = Instantiate(ZNetScene.instance.GetPrefab("Karve"), pos, Quaternion.identity);
		Ship ship = go.GetComponent<Ship>();
		ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.AshlandsOcean);
		Logger.LogInfo($"LPTEST ashlands key before: {ZoneSystem.instance.GetGlobalKey(GlobalKeys.AshlandsOcean)}");

		ship.m_ashDamageMsgTimer = 0f;
		ship.TakeAshlandsDamage(0.1f);
		Logger.LogInfo($"LPTEST ashlands nobody aboard -> key: {ZoneSystem.instance.GetGlobalKey(GlobalKeys.AshlandsOcean)}");

		GameObject pgo = Instantiate(ZNetScene.instance.GetPrefab("Player"), pos + Vector3.up, Quaternion.identity);
		Player player = pgo.GetComponent<Player>();
		ship.m_players.Add(player);
		ship.m_ashDamageMsgTimer = 0f;
		ship.TakeAshlandsDamage(0.1f);
		Logger.LogInfo($"LPTEST ashlands player aboard -> key: {ZoneSystem.instance.GetGlobalKey(GlobalKeys.AshlandsOcean)}");

		ship.m_players.Clear();
		ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.AshlandsOcean);
		ZNetScene.instance.Destroy(pgo);
		ZNetScene.instance.Destroy(go);
	}
}
