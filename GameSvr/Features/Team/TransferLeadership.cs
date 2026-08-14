using SystemModule;

namespace GameSvr.Features.Team
{
    /// <summary>
    /// 转让队长处理器
    /// Handles transferring group leadership to another member.
    ///
    /// 战神引擎逆向来源：
    /// - CM_CHANGEGROUPLEADER (ident 1023) @ sub_6C3B10
    /// - 权限检查：必须是当前队长
    /// - 成员查找：sub_6B7B8C
    /// - 转让逻辑：更新 m_GroupOwner 指向新队长
    /// - 响应码：SM_CHGGROUPLEADER_OK (661) / SM_CHGGROUPLEADER_FAIL (662)
    /// </summary>
    public class TransferLeadership
    {
        private readonly TPlayObject _currentLeader;
        private readonly string _newLeaderName;

        public TransferLeadership(TPlayObject currentLeader, string newLeaderName)
        {
            _currentLeader = currentLeader ?? throw new System.ArgumentNullException(nameof(currentLeader));
            _newLeaderName = newLeaderName?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// 执行队长转让操作
        /// Execute the leadership transfer.
        ///
        /// 战神逻辑：
        ///   6C3B2E mov esi,0xFFFFFFFF         ; code := -1
        ///   6C3B33 call sub_6B7BAC / je      => 不是队长 -> -1
        ///   6C3B47 call sub_6B7B8C / je      => 目标不是成员 -> -3
        ///   6C3B5C mov ecx,[ebx+0x568]       ; ecx := m_GroupMembers (TList)
        ///   6C3B62 call TList.GetCount
        ///   6C3B67 cmp eax,2 / jl 0x6c3ba1   => 成员数 < 2 -> -2
        ///   6C3B77 mov [edi+0x568],eax       ; newLeader.m_GroupOwner := newLeader
        ///   6C3B7D mov ecx,[ebx+0x568]       ; 遍历所有成员
        ///   6C3B83 call TList.GetItem
        ///   6C3B8C mov [eax+0x568],edi       ; member.m_GroupOwner := newLeader
        ///   6C3B9E mov dx,0x295 (661)        ; SM_CHGGROUPLEADER_OK, esi := 0
        ///   6C3BAB mov dx,0x296 (662)        ; SM_CHGGROUPLEADER_FAIL, recog := esi
        /// </summary>
        /// <returns>操作结果</returns>
        public TransferLeadershipResult Execute()
        {
            // 验证权限：必须是当前队长
            if (!IsCurrentLeader())
            {
                return TransferLeadershipResult.Unauthorized();
            }

            // 验证队伍成员数量
            if (_currentLeader.m_GroupMembers == null || _currentLeader.m_GroupMembers.Count < 2)
            {
                return TransferLeadershipResult.InsufficientMembers();
            }

            // 查找新队长
            var newLeader = M2Share.UserEngine?.GetPlayObject(_newLeaderName);
            if (newLeader == null)
            {
                return TransferLeadershipResult.TargetNotFound();
            }

            // 验证目标是否是组队成员
            if (!_currentLeader.IsGroupMember(newLeader))
            {
                return TransferLeadershipResult.NotGroupMember();
            }

            // 不能转让给自己
            if (_currentLeader == newLeader)
            {
                return TransferLeadershipResult.CannotTransferToSelf();
            }

            // 执行转让操作
            PerformTransfer(newLeader);

            // 触发脚本事件
            TriggerScriptEvent(newLeader);

            return TransferLeadershipResult.Success(_newLeaderName);
        }

        /// <summary>
        /// 验证当前玩家是否是队长
        /// 战神 6C3B33: call sub_6B7BAC => 检查 m_GroupOwner == self
        /// </summary>
        private bool IsCurrentLeader()
        {
            return _currentLeader.m_GroupOwner == _currentLeader;
        }

        /// <summary>
        /// 执行队长转移操作
        /// 战神 6C3B77: 设置新队长的 m_GroupOwner 指向自己
        /// 战神 6C3B7D-6C3B8C: 遍历所有成员，更新其 m_GroupOwner 指向新队长
        /// </summary>
        private void PerformTransfer(TPlayObject newLeader)
        {
            // 设置新队长的 m_GroupOwner 指向自己
            newLeader.m_GroupOwner = newLeader;

            // 更新所有成员的 m_GroupOwner 指向新队长
            if (_currentLeader.m_GroupMembers != null)
            {
                for (int i = 0; i < _currentLeader.m_GroupMembers.Count; i++)
                {
                    if (_currentLeader.m_GroupMembers[i] is TPlayObject member)
                    {
                        member.m_GroupOwner = newLeader;
                    }
                }
            }

            // 将旧队长的成员列表转移给新队长
            newLeader.m_GroupMembers = _currentLeader.m_GroupMembers;
            _currentLeader.m_GroupMembers = null;
        }

        /// <summary>
        /// 触发 NPC 脚本事件
        /// 战神可能有 @ChangeGroupLeader 标签触发
        /// </summary>
        private void TriggerScriptEvent(TPlayObject newLeader)
        {
            if (M2Share.g_FunctionNPC != null)
            {
                M2Share.g_FunctionNPC.GotoLable(_currentLeader, "@ChangeGroupLeader", false);
            }
        }
    }

    /// <summary>
    /// 转让队长操作结果
    /// </summary>
    public class TransferLeadershipResult
    {
        public bool IsSuccess { get; private set; }
        public int ErrorCode { get; private set; }
        public string NewLeaderName { get; private set; }
        public string ErrorMessage { get; private set; }

        private TransferLeadershipResult() { }

        /// <summary>
        /// 成功：战神 SM_CHGGROUPLEADER_OK (661), recog=0
        /// 战神 6C3B9E: mov dx,0x295
        /// </summary>
        public static TransferLeadershipResult Success(string newLeaderName)
        {
            return new TransferLeadershipResult
            {
                IsSuccess = true,
                ErrorCode = 0,
                NewLeaderName = newLeaderName,
                ErrorMessage = string.Empty
            };
        }

        /// <summary>
        /// 未授权：不是队长
        /// 战神 6C3B2E: mov esi,0xFFFFFFFF => code := -1
        /// </summary>
        public static TransferLeadershipResult Unauthorized()
        {
            return new TransferLeadershipResult
            {
                IsSuccess = false,
                ErrorCode = -1,
                NewLeaderName = string.Empty,
                ErrorMessage = "只有队长可以转让队长职位"
            };
        }

        /// <summary>
        /// 队伍成员不足：战神 -2
        /// 战神 6C3B67: cmp eax,2 / jl => 成员数 < 2
        /// </summary>
        public static TransferLeadershipResult InsufficientMembers()
        {
            return new TransferLeadershipResult
            {
                IsSuccess = false,
                ErrorCode = -2,
                NewLeaderName = string.Empty,
                ErrorMessage = "队伍成员不足，无法转让"
            };
        }

        /// <summary>
        /// 目标不是组队成员：战神 -3
        /// 战神 6C3B47: call sub_6B7B8C / je
        /// </summary>
        public static TransferLeadershipResult NotGroupMember()
        {
            return new TransferLeadershipResult
            {
                IsSuccess = false,
                ErrorCode = -3,
                NewLeaderName = string.Empty,
                ErrorMessage = "目标不是队伍成员"
            };
        }

        /// <summary>
        /// 目标玩家不存在或不在线：战神 -3
        /// </summary>
        public static TransferLeadershipResult TargetNotFound()
        {
            return new TransferLeadershipResult
            {
                IsSuccess = false,
                ErrorCode = -3,
                NewLeaderName = string.Empty,
                ErrorMessage = "目标玩家不存在或不在线"
            };
        }

        /// <summary>
        /// 不能转让给自己：逻辑错误
        /// </summary>
        public static TransferLeadershipResult CannotTransferToSelf()
        {
            return new TransferLeadershipResult
            {
                IsSuccess = false,
                ErrorCode = -4,
                NewLeaderName = string.Empty,
                ErrorMessage = "不能将队长转让给自己"
            };
        }
    }
}
