// TEST HARNESS ONLY — never shipped, never part of the PR.
// Server-side: once a player has been connected for 60 s, force a raid at their position,
// then log the raid's clock and how many raid creatures exist, every 5 s.
using System.Linq;
using BepInEx;
using UnityEngine;

[BepInPlugin("raidfix.harness.trigger", "RaidTrigger", "0.0.1")]
public class RaidTrigger : BaseUnityPlugin
{
	private const string EventName = "army_eikthyr";
	private const float DelayAfterJoin = 60f;

	private float playerSince = -1f;
	private bool fired;
	private float nextTick;

	private void Update()
	{
		if (ZNet.instance == null || !ZNet.instance.IsServer() || RandEventSystem.instance == null || Time.time < nextTick)
		{
			return;
		}
		nextTick = Time.time + 5f;

		var players = Player.GetAllPlayers();
		if (!fired)
		{
			if (players.Count == 0)
			{
				playerSince = -1f;
				return;
			}
			if (playerSince < 0f)
			{
				playerSince = Time.time;
				Logger.LogInfo($"player '{players[0].GetPlayerName()}' present; forcing {EventName} in {DelayAfterJoin:F0} s. Known events: {string.Join(", ", RandEventSystem.instance.m_events.Select(e => e.m_name))}");
				return;
			}
			if (Time.time - playerSince < DelayAfterJoin)
			{
				return;
			}

			Vector3 pos = players[0].transform.position;
			RandEventSystem.instance.SetRandomEventByName(EventName, pos);
			fired = true;
			Logger.LogInfo($"forced {EventName} at {pos}");
			return;
		}

		RandomEvent ev = RandEventSystem.instance.m_randomEvent;
		int raidMobs = Character.GetAllCharacters().Count(c => c.GetComponent<MonsterAI>() is { } ai && ai.IsEventCreature());
		string where = players.Count > 0 && ev != null
			? $" player {Utils.DistanceXZ(players[0].transform.position, ev.m_pos):F0} m from centre (range {ev.m_eventRange:F0} m)"
			: "";
		Logger.LogInfo(ev == null
			? $"no raid running; raid creatures alive: {raidMobs}"
			: $"{ev.m_name} t={ev.m_time:F0}/{ev.m_duration:F0} s, spawner delay {ev.m_spawnerDelay:F0} s,{where}; raid creatures alive: {raidMobs}");
	}
}
