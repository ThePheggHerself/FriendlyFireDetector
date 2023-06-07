using PlayerStatsSystem;
using PluginAPI.Core.Attributes;
using PluginAPI.Core;
using PluginAPI.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PlayerRoles;
using static UnityEngine.GraphicsBuffer;
using UnityEngine;
using AdminToys;
using CustomPlayerEffects;
using InventorySystem.Items;
using InventorySystem.Items.ThrowableProjectiles;
using InventorySystem.Items.Pickups;
using static RoundSummary;
using Mirror;

namespace FriendlyFireDetector
{
	public class Handler
	{
		public readonly Dictionary<string, RoleTypeId> PreviousRoles = new Dictionary<string, RoleTypeId>();
		public readonly Dictionary<string, RoleTypeId> Roles = new Dictionary<string, RoleTypeId>();
		public readonly Dictionary<string, List<GrenadeThrowerInfo>> grenadeInfo = new Dictionary<string, List<GrenadeThrowerInfo>>();
		public readonly Dictionary<string, FFInfo> ffInfo = new Dictionary<string, FFInfo>();
		public static bool RoundInProgess = false;

		[PluginEvent(ServerEventType.PlayerSpawn)]
		public void PlayerSpawned(Player player, RoleTypeId role)
		{
			if (player == null || player.IsServer || player.UserId == null || role == RoleTypeId.None)
				return;

			if (Roles.ContainsKey(player.UserId))
				Roles[player.UserId] = role;
			else Roles.Add(player.UserId, role);
		}

		[PluginEvent(ServerEventType.PlayerDamage), PluginPriority(LoadPriority.Highest)]
		public bool PlayerDamageEvent(Player victim, Player attacker, DamageHandlerBase damageHandler)
		{
			if (Plugin.Paused || !RoundInProgess || attacker == null || victim == null || !(damageHandler is AttackerDamageHandler aDH) || aDH == null || aDH.Attacker.LogUserID == null || victim.IsServer || aDH.Attacker.LogUserID == victim.UserId)
				return true;

			if (attacker == null && Roles.ContainsKey(aDH.Attacker.LogUserID))
				return false;

			if (!IsFF(victim.Role, attacker.Role))
				return true;

			List<Player> friendlies = new List<Player>();
			List<Player> hostiles = new List<Player>();

			foreach (var plr in GetNearbyPlayers(attacker))
			{
				if (IsFF(plr.Role, attacker.Role))
					friendlies.Add(plr);
				else hostiles.Add(plr);
			}

			if (hostiles.Count > 0)
			{
				return true;
			}
			else
			{
				if (attacker.TemporaryData.Contains("ffdstop"))
					attacker.TemporaryData.Override("ffdstop", $"{aDH.Damage}");
				else
					attacker.TemporaryData.Add<string>("ffdstop", $"{aDH.Damage}");

				//attacker.Damage(5, "FFD Reversal");

				return false;
			}
		}

		[PluginEvent(ServerEventType.PlayerDamagedShootingTarget)]
		public void TargetDamagedEvent(Player attacker, ShootingTarget target, DamageHandlerBase damageHandler, float amount)
		{
			if (target.CommandName.ToLowerInvariant().Contains("dboy"))
				UpdateLegitDamage(attacker, false);
			if (target.CommandName.ToLowerInvariant().Contains("sport"))
			{
				UpdateFFDamage(attacker);
				HandlePunishments(attacker, amount);
			}
		}

		public bool IsFF(RoleTypeId victim, RoleTypeId attacker)
		{
			if ((victim == RoleTypeId.ClassD || isChaos(victim)) && (attacker == RoleTypeId.ClassD || isChaos(attacker)))
			{
				if (victim == RoleTypeId.ClassD && attacker == RoleTypeId.ClassD)
					return false;
				return true;
			}
			else if ((victim == RoleTypeId.Scientist || isMtf(victim)) && (attacker == RoleTypeId.Scientist || isMtf(attacker)))
				return true;

			return false;
		}

		private bool isChaos(RoleTypeId role)
		{
			switch (role)
			{
				case RoleTypeId.ChaosConscript:
				case RoleTypeId.ChaosRifleman:
				case RoleTypeId.ChaosRepressor:
				case RoleTypeId.ChaosMarauder:
					return true;
				default:
					return false;
			}
		}

		private bool isMtf(RoleTypeId role)
		{
			switch (role)
			{
				case RoleTypeId.FacilityGuard:
				case RoleTypeId.NtfCaptain:
				case RoleTypeId.NtfSpecialist:
				case RoleTypeId.NtfPrivate:
				case RoleTypeId.NtfSergeant:
					return true;
				default:
					return false;
			}
		}

		private bool isSCP(RoleTypeId role)
		{
			switch (role)
			{
				case RoleTypeId.Scp173:
				case RoleTypeId.Scp106:
				case RoleTypeId.Scp049:
				case RoleTypeId.Scp079:
				case RoleTypeId.Scp096:
				case RoleTypeId.Scp0492:
				case RoleTypeId.Scp939:
					return true;
				default:
					return false;
			}
		}

		public FFInfo GetInfo(Player player)
		{
			if (!ffInfo.ContainsKey(player.UserId))
				ffInfo.Add(player.UserId, new FFInfo());

			return ffInfo[player.UserId];
		}

		public void UpdateLegitDamage(Player player, bool updateTime = true)
		{
			FFInfo info = GetInfo(player);

			info.Value = Mathf.Clamp(info.Value - 1, -5, 15);
			if (updateTime)
				info.LastUpdate = DateTime.Now;
		}

		public void UpdateFFDamage(Player player)
		{
			FFInfo info = GetInfo(player);

			info.Value = Mathf.Clamp(info.Value + 1, -5, 15);
		}

		public void HandlePunishments(Player player, float damage)
		{
			FFInfo info = GetInfo(player);

			if (info.Value > 2)
				player.Damage(damage, "Anti-FF: Damage reversal due to friendly fire");
			if (info.Value > 4)
				player.DropEverything();
			if (info.Value > 6)
			{
				player.EffectsManager.EnableEffect<Deafened>(info.Value);
				player.EffectsManager.EnableEffect<Blinded>(info.Value);
				player.EffectsManager.EnableEffect<Disabled>(info.Value * 2);
			}
			if (info.Value > 9)
			{
				player.Kill();
			}
			if (info.Value > 12)
			{
				player.Kick("Anti-FF: Automatic kick for too much friendly damage");
			}
			if (info.Value > 14)
			{
				player.Ban("Anti-FF: Automatic ban for too much friendly damage", 1440 * 60);
			}
		}

		[PluginEvent(ServerEventType.PlayerDamagedShootingTarget)]
		public void PlayerShootTarget(Player player, ShootingTarget target, DamageHandlerBase damageHandler, float damageAmount)
		{
			try
			{
				if (player.TemporaryData.Contains("ffdstop"))
					player.TemporaryData.Override("ffdstop", $"{damageAmount}");
				else
					player.TemporaryData.Add<string>("ffdstop", $"{damageAmount}");
			}
			catch (Exception e)
			{
				Log.Error(e.ToString());
			}
		}

		/// <summary>
		/// Gets a list of all players close to the attacker (100 meters for Surface, 50 for the facility)
		/// </summary>
		/// <returns></returns>
		public List<Player> GetNearbyPlayers(Player atkr)
		{
			float distanceCheck = atkr.Position.y > 900 ? 70 : 35;
			List<Player> nearbyPlayers = new List<Player>();

			foreach (var plr in Server.GetPlayers())
			{
				if (plr.IsServer || plr.Role == RoleTypeId.Spectator)
					continue;

				var distance = Vector3.Distance(atkr.Position, plr.Position);
				var angle = Vector3.Angle(atkr.GameObject.transform.forward, atkr.Position - plr.Position);

				if ((distance <= distanceCheck && angle > 130) || distance < 5)
					nearbyPlayers.Add(plr);
			}

			return nearbyPlayers;
		}

		public List<Player> GetNearbyPlayers(Vector3 position)
		{
			float distanceCheck = position.y > 900 ? 70 : 35;
			List<Player> nearbyPlayers = new List<Player>();

			foreach (var plr in Server.GetPlayers())
			{
				if (plr.IsServer || plr.Role == RoleTypeId.Spectator)
					continue;

				var distance = Vector3.Distance(position, plr.Position);

				if (distance <= distanceCheck)
					nearbyPlayers.Add(plr);
			}

			return nearbyPlayers;
		}

		[PluginEvent(ServerEventType.RoundEnd)]
		public void RoundEnd(LeadingTeam team)
		{
			Plugin.Paused = false;
			RoundInProgess = false;
		}

		[PluginEvent(ServerEventType.RoundStart)]
		public void RoundStart()
		{
			RoundInProgess = true;
		}
	}
}
