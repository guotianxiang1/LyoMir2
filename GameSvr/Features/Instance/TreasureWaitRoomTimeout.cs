using System;
using System.Collections.Generic;

namespace GameSvr.Features.Instance
{
    /// <summary>
    /// Treasure Wait Room Timeout - MVI Implementation
    /// Manages timeout behavior for treasure battle waiting rooms
    /// 宝藏等待室超时管理 - MVI实现
    /// </summary>
    public class TreasureWaitRoomTimeout
    {
        #region Model - State representation

        /// <summary>
        /// Wait room timeout state model
        /// </summary>
        public class TimeoutModel
        {
            public string RoomId { get; set; }
            public TimeoutStatus Status { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? StartedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
            public int TimeoutDurationMs { get; set; }
            public int RemainingTimeMs { get; set; }
            public List<WaitingParticipant> Participants { get; set; }
            public int MinParticipants { get; set; }
            public int MaxParticipants { get; set; }
            public bool IsWarningIssued { get; set; }
            public int WarningThresholdMs { get; set; }
            public string TimeoutReason { get; set; }

            public TimeoutModel()
            {
                Participants = new List<WaitingParticipant>();
                Status = TimeoutStatus.Idle;
                TimeoutDurationMs = 300000; // 5 minutes default
                WarningThresholdMs = 60000; // 1 minute warning
            }
        }

        /// <summary>
        /// Waiting participant information
        /// </summary>
        public class WaitingParticipant
        {
            public string PlayerId { get; set; }
            public string PlayerName { get; set; }
            public DateTime JoinedAt { get; set; }
            public bool IsReady { get; set; }
            public bool HasBeenWarned { get; set; }
        }

        /// <summary>
        /// Timeout status enumeration
        /// </summary>
        public enum TimeoutStatus
        {
            Idle,
            Waiting,
            WarningIssued,
            TimedOut,
            Cancelled,
            Completed
        }

        #endregion

        #region Intent - User actions

        /// <summary>
        /// Timeout intent - represents actions that can be performed
        /// </summary>
        public abstract class TimeoutIntent
        {
            public string RoomId { get; set; }
        }

        /// <summary>
        /// Intent to start the timeout timer
        /// </summary>
        public class StartTimeoutIntent : TimeoutIntent
        {
            public int TimeoutDurationMs { get; set; }
            public int MinParticipants { get; set; }
            public int MaxParticipants { get; set; }
            public int WarningThresholdMs { get; set; }
        }

        /// <summary>
        /// Intent to add a participant to the waiting room
        /// </summary>
        public class AddParticipantIntent : TimeoutIntent
        {
            public WaitingParticipant Participant { get; set; }
        }

        /// <summary>
        /// Intent to remove a participant from the waiting room
        /// </summary>
        public class RemoveParticipantIntent : TimeoutIntent
        {
            public string PlayerId { get; set; }
        }

        /// <summary>
        /// Intent to mark a participant as ready
        /// </summary>
        public class ParticipantReadyIntent : TimeoutIntent
        {
            public string PlayerId { get; set; }
        }

        /// <summary>
        /// Intent to update the remaining time (called periodically)
        /// </summary>
        public class UpdateTimeIntent : TimeoutIntent
        {
            public DateTime CurrentTime { get; set; }
        }

        /// <summary>
        /// Intent to issue a timeout warning
        /// </summary>
        public class IssueWarningIntent : TimeoutIntent
        {
            public int RemainingTimeMs { get; set; }
        }

        /// <summary>
        /// Intent to trigger timeout expiration
        /// </summary>
        public class ExpireTimeoutIntent : TimeoutIntent
        {
            public string Reason { get; set; }
        }

        /// <summary>
        /// Intent to cancel the timeout (e.g., battle started)
        /// </summary>
        public class CancelTimeoutIntent : TimeoutIntent
        {
            public string Reason { get; set; }
        }

        /// <summary>
        /// Intent to complete the timeout successfully (all ready)
        /// </summary>
        public class CompleteTimeoutIntent : TimeoutIntent
        {
        }

        #endregion

        #region View - State updates

        /// <summary>
        /// Processes intent and returns updated model
        /// </summary>
        public TimeoutModel ProcessIntent(TimeoutIntent intent, TimeoutModel currentModel)
        {
            if (intent == null || currentModel == null)
            {
                return currentModel ?? new TimeoutModel();
            }

            return intent switch
            {
                StartTimeoutIntent startIntent => HandleStartTimeout(startIntent, currentModel),
                AddParticipantIntent addIntent => HandleAddParticipant(addIntent, currentModel),
                RemoveParticipantIntent removeIntent => HandleRemoveParticipant(removeIntent, currentModel),
                ParticipantReadyIntent readyIntent => HandleParticipantReady(readyIntent, currentModel),
                UpdateTimeIntent updateIntent => HandleUpdateTime(updateIntent, currentModel),
                IssueWarningIntent warningIntent => HandleIssueWarning(warningIntent, currentModel),
                ExpireTimeoutIntent expireIntent => HandleExpireTimeout(expireIntent, currentModel),
                CancelTimeoutIntent cancelIntent => HandleCancelTimeout(cancelIntent, currentModel),
                CompleteTimeoutIntent completeIntent => HandleCompleteTimeout(completeIntent, currentModel),
                _ => currentModel
            };
        }

        private TimeoutModel HandleStartTimeout(StartTimeoutIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status != TimeoutStatus.Idle)
            {
                return newModel;
            }

            newModel.RoomId = intent.RoomId;
            newModel.Status = TimeoutStatus.Waiting;
            newModel.CreatedAt = DateTime.Now;
            newModel.StartedAt = DateTime.Now;
            newModel.TimeoutDurationMs = intent.TimeoutDurationMs > 0
                ? intent.TimeoutDurationMs
                : 300000;
            newModel.ExpiresAt = newModel.StartedAt.Value.AddMilliseconds(newModel.TimeoutDurationMs);
            newModel.RemainingTimeMs = newModel.TimeoutDurationMs;
            newModel.MinParticipants = intent.MinParticipants;
            newModel.MaxParticipants = intent.MaxParticipants;
            newModel.WarningThresholdMs = intent.WarningThresholdMs > 0
                ? intent.WarningThresholdMs
                : 60000;
            newModel.IsWarningIssued = false;
            newModel.TimeoutReason = null;

            return newModel;
        }

        private TimeoutModel HandleAddParticipant(AddParticipantIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status != TimeoutStatus.Waiting && newModel.Status != TimeoutStatus.WarningIssued)
            {
                return newModel;
            }

            if (intent.Participant == null || string.IsNullOrEmpty(intent.Participant.PlayerId))
            {
                return newModel;
            }

            // Check if participant already exists
            var existingIndex = newModel.Participants.FindIndex(p =>
                p.PlayerId == intent.Participant.PlayerId);

            if (existingIndex >= 0)
            {
                // Update existing participant
                newModel.Participants[existingIndex] = intent.Participant;
            }
            else if (newModel.Participants.Count < newModel.MaxParticipants)
            {
                // Add new participant
                intent.Participant.JoinedAt = DateTime.Now;
                newModel.Participants.Add(intent.Participant);
            }

            return newModel;
        }

        private TimeoutModel HandleRemoveParticipant(RemoveParticipantIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (string.IsNullOrEmpty(intent.PlayerId))
            {
                return newModel;
            }

            var participantIndex = newModel.Participants.FindIndex(p =>
                p.PlayerId == intent.PlayerId);

            if (participantIndex >= 0)
            {
                newModel.Participants.RemoveAt(participantIndex);
            }

            // If no participants left and still waiting, expire
            if (newModel.Participants.Count == 0 &&
                (newModel.Status == TimeoutStatus.Waiting ||
                 newModel.Status == TimeoutStatus.WarningIssued))
            {
                newModel.Status = TimeoutStatus.TimedOut;
                newModel.TimeoutReason = "All participants left";
            }

            return newModel;
        }

        private TimeoutModel HandleParticipantReady(ParticipantReadyIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (string.IsNullOrEmpty(intent.PlayerId))
            {
                return newModel;
            }

            var participant = newModel.Participants.Find(p => p.PlayerId == intent.PlayerId);
            if (participant != null)
            {
                participant.IsReady = true;
            }

            // Check if all participants are ready and minimum threshold is met
            if (newModel.Participants.Count >= newModel.MinParticipants &&
                newModel.Participants.TrueForAll(p => p.IsReady))
            {
                newModel.Status = TimeoutStatus.Completed;
            }

            return newModel;
        }

        private TimeoutModel HandleUpdateTime(UpdateTimeIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status != TimeoutStatus.Waiting &&
                newModel.Status != TimeoutStatus.WarningIssued)
            {
                return newModel;
            }

            if (!newModel.ExpiresAt.HasValue)
            {
                return newModel;
            }

            var timeSpan = newModel.ExpiresAt.Value - intent.CurrentTime;
            newModel.RemainingTimeMs = Math.Max(0, (int)timeSpan.TotalMilliseconds);

            // Check if timeout has expired
            if (newModel.RemainingTimeMs <= 0)
            {
                newModel.Status = TimeoutStatus.TimedOut;
                newModel.TimeoutReason = "Wait time exceeded";
                return newModel;
            }

            // Check if warning threshold is reached
            if (!newModel.IsWarningIssued &&
                newModel.RemainingTimeMs <= newModel.WarningThresholdMs)
            {
                newModel.Status = TimeoutStatus.WarningIssued;
                newModel.IsWarningIssued = true;
            }

            return newModel;
        }

        private TimeoutModel HandleIssueWarning(IssueWarningIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status == TimeoutStatus.Waiting)
            {
                newModel.Status = TimeoutStatus.WarningIssued;
                newModel.IsWarningIssued = true;
                newModel.RemainingTimeMs = intent.RemainingTimeMs;

                // Mark all participants as warned
                foreach (var participant in newModel.Participants)
                {
                    participant.HasBeenWarned = true;
                }
            }

            return newModel;
        }

        private TimeoutModel HandleExpireTimeout(ExpireTimeoutIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status == TimeoutStatus.Waiting ||
                newModel.Status == TimeoutStatus.WarningIssued)
            {
                newModel.Status = TimeoutStatus.TimedOut;
                newModel.TimeoutReason = intent.Reason ?? "Timeout expired";
                newModel.RemainingTimeMs = 0;
            }

            return newModel;
        }

        private TimeoutModel HandleCancelTimeout(CancelTimeoutIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status != TimeoutStatus.TimedOut &&
                newModel.Status != TimeoutStatus.Completed)
            {
                newModel.Status = TimeoutStatus.Cancelled;
                newModel.TimeoutReason = intent.Reason ?? "Cancelled";
            }

            return newModel;
        }

        private TimeoutModel HandleCompleteTimeout(CompleteTimeoutIntent intent, TimeoutModel model)
        {
            var newModel = CloneModel(model);

            if (newModel.Status == TimeoutStatus.Waiting ||
                newModel.Status == TimeoutStatus.WarningIssued)
            {
                newModel.Status = TimeoutStatus.Completed;
                newModel.TimeoutReason = "All participants ready";
            }

            return newModel;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a shallow clone of the model to ensure immutability
        /// </summary>
        private TimeoutModel CloneModel(TimeoutModel model)
        {
            return new TimeoutModel
            {
                RoomId = model.RoomId,
                Status = model.Status,
                CreatedAt = model.CreatedAt,
                StartedAt = model.StartedAt,
                ExpiresAt = model.ExpiresAt,
                TimeoutDurationMs = model.TimeoutDurationMs,
                RemainingTimeMs = model.RemainingTimeMs,
                Participants = new List<WaitingParticipant>(model.Participants),
                MinParticipants = model.MinParticipants,
                MaxParticipants = model.MaxParticipants,
                IsWarningIssued = model.IsWarningIssued,
                WarningThresholdMs = model.WarningThresholdMs,
                TimeoutReason = model.TimeoutReason
            };
        }

        /// <summary>
        /// Checks if the timeout is active
        /// </summary>
        public bool IsActive(TimeoutModel model)
        {
            return model != null &&
                   (model.Status == TimeoutStatus.Waiting ||
                    model.Status == TimeoutStatus.WarningIssued);
        }

        /// <summary>
        /// Checks if the timeout has expired
        /// </summary>
        public bool HasExpired(TimeoutModel model)
        {
            return model != null && model.Status == TimeoutStatus.TimedOut;
        }

        /// <summary>
        /// Checks if the timeout is completed successfully
        /// </summary>
        public bool IsCompleted(TimeoutModel model)
        {
            return model != null && model.Status == TimeoutStatus.Completed;
        }

        /// <summary>
        /// Gets the number of ready participants
        /// </summary>
        public int GetReadyCount(TimeoutModel model)
        {
            if (model?.Participants == null)
            {
                return 0;
            }

            return model.Participants.FindAll(p => p.IsReady).Count;
        }

        /// <summary>
        /// Checks if minimum participants requirement is met
        /// </summary>
        public bool HasMinimumParticipants(TimeoutModel model)
        {
            return model != null &&
                   model.Participants.Count >= model.MinParticipants;
        }

        /// <summary>
        /// Checks if room is full
        /// </summary>
        public bool IsFull(TimeoutModel model)
        {
            return model != null &&
                   model.Participants.Count >= model.MaxParticipants;
        }

        #endregion
    }
}
