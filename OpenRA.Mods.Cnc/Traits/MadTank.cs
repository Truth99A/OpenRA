#region Copyright & License Information
/*
 * Copyright 2007-2019 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.GameRules;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	class MadTankInfo : ITraitInfo, IRulesetLoaded, Requires<ExplodesInfo>, Requires<WithFacingSpriteBodyInfo>
	{
		[SequenceReference] public readonly string ThumpSequence = "piston";
		public readonly int ThumpInterval = 8;
		[WeaponReference]
		public readonly string ThumpDamageWeapon = "MADTankThump";

		[ActorReference]
		public readonly string DriverActor = "e1";

		[VoiceReference] public readonly string Voice = "Action";

		public readonly string DetonationSound = "madexplo.aud";

		[GrantedConditionReference]
		[Desc("The condition to grant to self while deployed.")]
		public readonly string DeployedCondition = null;

		public WeaponInfo ThumpDamageWeaponInfo { get; private set; }

		[Desc("Types of damage that this trait causes to self while self-destructing. Leave empty for no damage types.")]
		public readonly BitSet<DamageType> DamageTypes = default(BitSet<DamageType>);

		public object Create(ActorInitializer init) { return new MadTank(init.Self, this); }
		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			WeaponInfo thumpDamageWeapon;
			var thumpDamageWeaponToLower = (ThumpDamageWeapon ?? string.Empty).ToLowerInvariant();

			if (!rules.Weapons.TryGetValue(thumpDamageWeaponToLower, out thumpDamageWeapon))
				throw new YamlException("Weapons Ruleset does not contain an entry '{0}'".F(thumpDamageWeaponToLower));

			ThumpDamageWeaponInfo = thumpDamageWeapon;
		}
	}

	class MadTank : INotifyCreated, IIssueOrder, IResolveOrder, IOrderVoice, ITick, IIssueDeployOrder
	{
		readonly Actor self;
		readonly MadTankInfo info;
		readonly WithFacingSpriteBody wfsb;
		ConditionManager conditionManager;
		bool deployed;
		int tick;
		int condition;

		public MadTank(Actor self, MadTankInfo info)
		{
			this.self = self;
			this.info = info;
			wfsb = self.Trait<WithFacingSpriteBody>();
		}

		void INotifyCreated.Created(Actor self)
		{
			conditionManager = self.TraitOrDefault<ConditionManager>();
		}

		void ITick.Tick(Actor self)
		{
			if (!deployed)
				return;
			else if (deployed && ++tick >= info.ThumpInterval)
			{
				Thump();
				tick = 0;
			}
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				yield return new TargetTypeOrderTargeter(new BitSet<TargetableType>("ThumpAttack"), "ThumpAttack", 5, "attack", true, false) { ForceAttack = false };
				yield return new DeployOrderTargeter("Thump", 5);
			}
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, Target target, bool queued)
		{
			if (order.OrderID != "ThumpAttack" && order.OrderID != "Thump")
				return null;

			return new Order(order.OrderID, self, target, queued);
		}

		Order IIssueDeployOrder.IssueDeployOrder(Actor self, bool queued)
		{
			if (!deployed) 
			{
				return new Order("Thump", self, queued);
			}
			else
			{
				deployed = false;

				if (conditionManager != null && !string.IsNullOrEmpty(info.DeployedCondition))
					condition = conditionManager.RevokeCondition(self, condition);
				
				return null;
			}
		}

		bool IIssueDeployOrder.CanIssueDeployOrder(Actor self) { return true; }

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			return info.Voice;
		}

		void Thump()
		{
			self.World.AddFrameEndTask(w =>
			{
				if (info.ThumpSequence != null) 
				{
					wfsb.PlayCustomAnimation(self, info.ThumpSequence);
				}

				if (info.ThumpDamageWeapon != null)
				{
					// Use .FromPos since this weapon needs to affect more than just the MadTank actor
					info.ThumpDamageWeaponInfo.Impact(Target.FromPos(self.CenterPosition), self, Enumerable.Empty<int>());
				}

				self.QueueActivity(new CallFunc(() => Game.Sound.Play(SoundType.World, info.DetonationSound, self.CenterPosition)));
			});
		}

		void Thumping() {
			deployed = true;

			if (conditionManager != null && !string.IsNullOrEmpty(info.DeployedCondition))
				condition = conditionManager.GrantCondition(self, info.DeployedCondition);
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "ThumpAttack")
			{
				if (!order.Queued)
					self.CancelActivity();

				self.SetTargetLine(order.Target, Color.Red);
				self.QueueActivity(new MoveAdjacentTo(self, order.Target, targetLineColor: Color.Red));
				self.QueueActivity(new CallFunc(Thumping));
			}
			else if (order.OrderString == "Thump")
			{
				if (!order.Queued)
					self.CancelActivity();
				
				self.QueueActivity(new CallFunc(Thumping));
			}
		}
	}
}
