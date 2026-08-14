using SystemModule;

namespace GameSvr.Features.Team
{
    /// <summary>
    /// 取消组队请求处理器
    /// Handles canceling a pending team/group invitation request.
    ///
    /// 战神引擎逆向来源：
    /// - CM_DELGROUPMEMBER (ident 1022) @ sub_6C3CF0
    /// - 权限检查：队长或自己删除自己
    /// - 成员查找：sub_6B7B8C
    /// - 删除操作：sub_726E68 (group.DelMember)
    /// - 响应码：SM_GROUPDELMEM_OK (663) / SM_GROUPDELMEM_FAIL (665)
    /// </summary>
    public class CancelTeamRequest
    {
        private readonly TPlayObject _requester;
        private readonly string _targetName;

        public CancelTeamRequest(TPlayObject requester, string targetName)
        {
            _requester = requester ?? throw new System.ArgumentNullException(nameof(requester));
            _targetName = targetName?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// 执行取消组队请求
        /// Execute the team cancellation request.
        ///
        /// 战神逻辑：
        ///   6C3D18 mov byte [ebp-5],0        ; allowed := False
        ///   6C3D1C mov esi,0xFFFFFF9D        ; code := -99
        ///   6C3D23 call sub_6B7BAC / je 0x6c3d32  => leader -> allowed := True
        ///   6C3D32 lea eax,[ebp-0xC] / 6C3D35 lea edx,[ebx+0x106] / 6C3D3B call 0x405774
        ///   6C3D46 call 0x40591c / 6C3D4B jne 0x6c3d53  => own name == argument -> allowed := True (self-leave)
        ///   6C3D53 or esi,0xFFFFFFFF          => else code := -1
        ///   6C3D61 call sub_6B7B8C / 6C3D68 je 0x6c3d96  => not a member -> -3
        ///   6C3D73 call sub_726E68            => group.DelMember(arg)
        ///   6C3D84 mov dx,0x297 (663) recog=0 msg=arg ; then esi := 0
        ///   6C3D9B test esi,esi / jne -> 6C3DA9 mov dx,0x299 (665) recog=esi
        /// </summary>
        /// <returns>操作结果</returns>
        public CancelTeamResult Execute()
        {
            // 验证权限：必须是队长或删除自己
            if (!ValidatePermission())
            {
                return CancelTeamResult.Unauthorized();
            }

            // 查找目标玩家
            var targetPlayer = M2Share.UserEngine?.GetPlayObject(_targetName);
            if (targetPlayer == null)
            {
                return CancelTeamResult.TargetNotFound();
            }

            // 验证目标是否是组队成员
            if (!_requester.IsGroupMember(targetPlayer))
            {
                return CancelTeamResult.NotGroupMember();
            }

            // 执行删除操作（通过队长对象）
            var groupOwner = _requester.m_GroupOwner as TPlayObject;
            groupOwner?.DelMember(targetPlayer);

            // 触发脚本事件
            TriggerScriptEvent();

            return CancelTeamResult.Success(_targetName);
        }

        /// <summary>
        /// 验证权限：队长或自己删除自己
        /// 战神 6C3D23: 检查是否为队长
        /// 战神 6C3D46: 检查名字是否匹配（自己删除自己）
        /// </summary>
        private bool ValidatePermission()
        {
            // 是队长
            if (_requester.m_GroupOwner == _requester)
                return true;

            // 删除自己（忽略大小写比较）
            if (string.Compare(_requester.m_sCharName, _targetName,
                System.StringComparison.OrdinalIgnoreCase) == 0)
                return true;

            return false;
        }

        /// <summary>
        /// 触发 NPC 脚本事件
        /// 战神 6C3D5F: 调用 @GroupDelMember 标签
        /// </summary>
        private void TriggerScriptEvent()
        {
            if (M2Share.g_FunctionNPC != null)
            {
                M2Share.g_FunctionNPC.GotoLable(_requester, "@GroupDelMember", false);
            }
        }
    }

    /// <summary>
    /// 取消组队操作结果
    /// </summary>
    public class CancelTeamResult
    {
        public bool IsSuccess { get; private set; }
        public int ErrorCode { get; private set; }
        public string TargetName { get; private set; }
        public string ErrorMessage { get; private set; }

        private CancelTeamResult() { }

        /// <summary>
        /// 成功：战神 SM_GROUPDELMEM_OK (663), recog=0
        /// </summary>
        public static CancelTeamResult Success(string targetName)
        {
            return new CancelTeamResult
            {
                IsSuccess = true,
                ErrorCode = 0,
                TargetName = targetName,
                ErrorMessage = string.Empty
            };
        }

        /// <summary>
        /// 未授权：战神 -1 (不是队长且不是删除自己)
        /// 战神 6C3D53: or esi,0xFFFFFFFF
        /// </summary>
        public static CancelTeamResult Unauthorized()
        {
            return new CancelTeamResult
            {
                IsSuccess = false,
                ErrorCode = -1,
                TargetName = string.Empty,
                ErrorMessage = "只有队长或本人可以执行此操作"
            };
        }

        /// <summary>
        /// 目标不在线或不是组队成员：战神 -3
        /// 战神 6C3D61: call sub_6B7B8C / 6C3D68 je 0x6c3d96
        /// </summary>
        public static CancelTeamResult TargetNotFound()
        {
            return new CancelTeamResult
            {
                IsSuccess = false,
                ErrorCode = -3,
                TargetName = string.Empty,
                ErrorMessage = "目标玩家不存在或不在线"
            };
        }

        /// <summary>
        /// 不是组队成员：战神 -3
        /// </summary>
        public static CancelTeamResult NotGroupMember()
        {
            return new CancelTeamResult
            {
                IsSuccess = false,
                ErrorCode = -3,
                TargetName = string.Empty,
                ErrorMessage = "目标不是队伍成员"
            };
        }
    }
}
