using System;
using System.Collections.Generic;
using System.Linq;

namespace GameSvr.Features.Instance
{
    /// <summary>
    /// Treasure Battle Settlement - MVI Implementation
    /// Handles treasure battle instance completion and reward distribution
    /// 宝藏战斗结算 - MVI实现
    /// </summary>
    public class TreasureBattleSettlement
    {
        #region Model - State representation

        /// <summary>
        /// Settlement state model - represents the current state of settlement
        /// </summary>
        public class SettlementModel
        {
            public string InstanceId { get; set; }
            public SettlementStatus Status { get; set; }
            public List<ParticipantScore> Participants { get; set; }
            public List<RewardAllocation> Rewards { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string ErrorMessage { get; set; }

            public SettlementModel()
            {
                Participants = new List<ParticipantScore>();
                Rewards = new List<RewardAllocation>();
                Status = SettlementStatus.Pending;
            }
        }

        /// <summary>
        /// Participant scoring data
        /// </summary>
        public class ParticipantScore
        {
            public string PlayerId { get; set; }
            public string PlayerName { get; set; }
            public int DamageDealt { get; set; }
            public int MonstersKilled { get; set; }
            public int TimeInBattle { get; set; }
            public int TotalScore { get; set; }
            public int Rank { get; set; }
        }

        /// <summary>
        /// Reward allocation for a participant
        /// </summary>
        public class RewardAllocation
        {
            public string PlayerId { get; set; }
            public int Gold { get; set; }
            public int Experience { get; set; }
            public List<ItemReward> Items { get; set; }

            public RewardAllocation()
            {
                Items = new List<ItemReward>();
            }
        }

        /// <summary>
        /// Item reward details
        /// </summary>
        public class ItemReward
        {
            public string ItemName { get; set; }
            public int Count { get; set; }
            public int Quality { get; set; }
        }

        /// <summary>
        /// Settlement status enumeration
        /// </summary>
        public enum SettlementStatus
        {
            Pending,
            Calculating,
            Distributing,
            Completed,
            Failed
        }

        #endregion

        #region Intent - User actions

        /// <summary>
        /// Settlement intent - represents actions that can be performed
        /// </summary>
        public abstract class SettlementIntent
        {
            public string InstanceId { get; set; }
        }

        /// <summary>
        /// Intent to start settlement calculation
        /// </summary>
        public class StartSettlementIntent : SettlementIntent
        {
            public List<ParticipantScore> Participants { get; set; }
        }

        /// <summary>
        /// Intent to calculate rewards based on scores
        /// </summary>
        public class CalculateRewardsIntent : SettlementIntent
        {
            public List<ParticipantScore> RankedParticipants { get; set; }
        }

        /// <summary>
        /// Intent to distribute rewards to players
        /// </summary>
        public class DistributeRewardsIntent : SettlementIntent
        {
            public List<RewardAllocation> Rewards { get; set; }
        }

        /// <summary>
        /// Intent to finalize settlement
        /// </summary>
        public class FinalizeSettlementIntent : SettlementIntent
        {
            public bool Success { get; set; }
        }

        /// <summary>
        /// Intent to handle settlement error
        /// </summary>
        public class SettlementErrorIntent : SettlementIntent
        {
            public string ErrorMessage { get; set; }
        }

        #endregion

        #region View - State updates

        /// <summary>
        /// Processes intent and returns updated model
        /// </summary>
        public SettlementModel ProcessIntent(SettlementIntent intent, SettlementModel currentModel)
        {
            if (intent == null || currentModel == null)
            {
                return currentModel ?? new SettlementModel();
            }

            return intent switch
            {
                StartSettlementIntent startIntent => HandleStartSettlement(startIntent, currentModel),
                CalculateRewardsIntent calcIntent => HandleCalculateRewards(calcIntent, currentModel),
                DistributeRewardsIntent distIntent => HandleDistributeRewards(distIntent, currentModel),
                FinalizeSettlementIntent finalIntent => HandleFinalizeSettlement(finalIntent, currentModel),
                SettlementErrorIntent errorIntent => HandleSettlementError(errorIntent, currentModel),
                _ => currentModel
            };
        }

        private SettlementModel HandleStartSettlement(StartSettlementIntent intent, SettlementModel model)
        {
            var newModel = CloneModel(model);
            newModel.InstanceId = intent.InstanceId;
            newModel.Participants = intent.Participants ?? new List<ParticipantScore>();
            newModel.Status = SettlementStatus.Calculating;
            newModel.StartTime = DateTime.Now;
            newModel.ErrorMessage = null;

            // Rank participants by total score
            RankParticipants(newModel.Participants);

            return newModel;
        }

        private SettlementModel HandleCalculateRewards(CalculateRewardsIntent intent, SettlementModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status != SettlementStatus.Calculating)
            {
                return newModel;
            }

            // Calculate rewards based on rank
            newModel.Rewards = CalculateRewardAllocations(intent.RankedParticipants);
            newModel.Status = SettlementStatus.Distributing;

            return newModel;
        }

        private SettlementModel HandleDistributeRewards(DistributeRewardsIntent intent, SettlementModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status != SettlementStatus.Distributing)
            {
                return newModel;
            }

            // Rewards distribution would be handled externally
            // This just updates the state to reflect distribution has started
            newModel.Rewards = intent.Rewards;

            return newModel;
        }

        private SettlementModel HandleFinalizeSettlement(FinalizeSettlementIntent intent, SettlementModel model)
        {
            var newModel = CloneModel(model);
            newModel.Status = intent.Success ? SettlementStatus.Completed : SettlementStatus.Failed;
            newModel.EndTime = DateTime.Now;

            if (!intent.Success && string.IsNullOrEmpty(newModel.ErrorMessage))
            {
                newModel.ErrorMessage = "Settlement finalization failed";
            }

            return newModel;
        }

        private SettlementModel HandleSettlementError(SettlementErrorIntent intent, SettlementModel model)
        {
            var newModel = CloneModel(model);
            newModel.Status = SettlementStatus.Failed;
            newModel.ErrorMessage = intent.ErrorMessage;
            newModel.EndTime = DateTime.Now;

            return newModel;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Ranks participants by total score
        /// </summary>
        private void RankParticipants(List<ParticipantScore> participants)
        {
            if (participants == null || participants.Count == 0)
            {
                return;
            }

            var sorted = participants.OrderByDescending(p => p.TotalScore).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].Rank = i + 1;
            }
        }

        /// <summary>
        /// Calculates reward allocations based on participant rankings
        /// </summary>
        private List<RewardAllocation> CalculateRewardAllocations(List<ParticipantScore> participants)
        {
            var allocations = new List<RewardAllocation>();

            if (participants == null || participants.Count == 0)
            {
                return allocations;
            }

            foreach (var participant in participants)
            {
                var reward = new RewardAllocation
                {
                    PlayerId = participant.PlayerId
                };

                // Calculate rewards based on rank
                switch (participant.Rank)
                {
                    case 1:
                        reward.Gold = 100000;
                        reward.Experience = 50000;
                        reward.Items.Add(new ItemReward { ItemName = "宝藏宝箱", Count = 1, Quality = 5 });
                        break;
                    case 2:
                        reward.Gold = 75000;
                        reward.Experience = 37500;
                        reward.Items.Add(new ItemReward { ItemName = "宝藏宝箱", Count = 1, Quality = 4 });
                        break;
                    case 3:
                        reward.Gold = 50000;
                        reward.Experience = 25000;
                        reward.Items.Add(new ItemReward { ItemName = "宝藏宝箱", Count = 1, Quality = 3 });
                        break;
                    default:
                        // Base rewards for participation
                        reward.Gold = 25000;
                        reward.Experience = 10000;
                        reward.Items.Add(new ItemReward { ItemName = "宝藏碎片", Count = 5, Quality = 1 });
                        break;
                }

                // Bonus based on score
                int scoreBonus = participant.TotalScore / 1000;
                reward.Gold += scoreBonus * 100;
                reward.Experience += scoreBonus * 50;

                allocations.Add(reward);
            }

            return allocations;
        }

        /// <summary>
        /// Creates a shallow clone of the model to ensure immutability
        /// </summary>
        private SettlementModel CloneModel(SettlementModel model)
        {
            return new SettlementModel
            {
                InstanceId = model.InstanceId,
                Status = model.Status,
                Participants = new List<ParticipantScore>(model.Participants),
                Rewards = new List<RewardAllocation>(model.Rewards),
                StartTime = model.StartTime,
                EndTime = model.EndTime,
                ErrorMessage = model.ErrorMessage
            };
        }

        #endregion
    }
}
