using GameSvr.PasEngine;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int TaskStateReceived = 2;
        private const int TaskStateFinished = 3;

        private bool TryGetTaskMetadata(int taskId, out PasTaskMetadata metadata)
        {
            metadata = null;
            var host = M2Share.PasEngine;
            if (host == null)
                return false;

            foreach (var task in host.GetTaskScripts())
            {
                if (task.TaskId != taskId)
                    continue;

                metadata = task;
                return true;
            }

            return false;
        }

        private void SendTaskPacket(PasTaskMetadata task, int showUiFlag, int mode)
        {
            var host = M2Share.PasEngine;
            if (host == null || task == null)
                return;

            host.TryGetTaskState(task.TaskId, this, out var state);
            switch (mode)
            {
                case 1:
                    SendDefMessage(Grobal2.SM_TASK_BRIEF_INFO, showUiFlag, task.TaskId,
                        task.TaskType, state, task.Title);
                    break;
                case 2:
                    host.TryGetTaskDetail(task.TaskId, this, out var detail);
                    SendDefMessage(Grobal2.SM_TASK_DETAIL_INFO, showUiFlag, task.TaskId,
                        task.TaskType, state, detail);
                    break;
                case 3:
                    host.TryGetTaskProgress(task.TaskId, this, out var progress);
                    SendDefMessage(Grobal2.SM_TASK_PROGRESS_INFO, showUiFlag, task.TaskId,
                        task.TaskType, state, progress);
                    break;
                case 100:
                    SendDefMessage(Grobal2.SM_TASK_DELETE, showUiFlag, task.TaskId,
                        task.TaskType, state, string.Empty);
                    break;
            }
        }

        public void AddTaskToUIList(int taskId, int showUiFlag)
        {
            // AddTaskToUIList = sub_6E12E4. Two calls, in this order:
            //   0x6E12F0 push 0 / 0x6E12F2 push 1 / 0x6E12FB mov cl,1
            //   0x6E12FF call sub_604B4C   <- the task-state write (what makes this an "add")
            //   0x6E1304 push 2 / 0x6E1306 push edi (=showUiFlag)
            //   0x6E1312 call sub_604C1C   <- the packet send, MODE 2
            // The mode byte is 2, not 1: sub_6E12E4 and UpdateTaskDetail (sub_6E131C) are
            // byte-identical in shape and both push 2 (0x6E1304 vs 0x6E133C); they differ
            // only in that the add also runs sub_604B4C first. For contrast
            // UpdateTaskProgress (sub_6E1354) pushes 3 @0x6E1374 and DeleteTaskFromUIList
            // (sub_6E138C) has a much shorter body that pushes 0x64 @0x6E138F straight into
            // sub_604C1C with NO state write. Sending mode 1 emitted a packet the native
            // client never receives from this API, so the quest never appeared in the panel.
            if (TryGetTaskMetadata(taskId, out var task))
                SendTaskPacket(task, showUiFlag, 2);
        }

        public void UpdateTaskDetail(int taskId, int showUiFlag)
        {
            if (TryGetTaskMetadata(taskId, out var task))
                SendTaskPacket(task, showUiFlag, 2);
        }

        public void UpdateTaskProgress(int taskId, int showUiFlag)
        {
            if (TryGetTaskMetadata(taskId, out var task))
                SendTaskPacket(task, showUiFlag, 3);
        }

        public void DeleteTaskFromUIList(int taskId, int showUiFlag)
        {
            if (TryGetTaskMetadata(taskId, out var task))
                SendTaskPacket(task, showUiFlag, 100);
        }

        private void SendAllTaskDetails(int includeFinished)
        {
            var host = M2Share.PasEngine;
            if (host == null)
                return;

            SendDefMessage(Grobal2.SM_TASK_CLEAR_ALL, 0, 0, 0, 0, string.Empty);
            foreach (var task in host.GetTaskScripts())
            {
                if (!host.TryGetTaskState(task.TaskId, this, out var state))
                    continue;

                if (state != TaskStateReceived &&
                    (includeFinished == 0 || state != TaskStateFinished))
                    continue;

                SendTaskPacket(task, 0, 2);
            }
        }

        private void SendTaskDetailAndProgress(int taskId, int showUiFlag)
        {
            if (!TryGetTaskMetadata(taskId, out var task))
                return;

            SendTaskPacket(task, showUiFlag, 2);
            SendTaskPacket(task, showUiFlag, 3);
        }

        private void ExecuteTaskCommand(int taskId, string value)
        {
            M2Share.PasEngine?.TryDoTaskCommand(taskId, this, value ?? string.Empty, out _);
        }
    }
}
