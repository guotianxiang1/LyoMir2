namespace DBSvr.Core
{
    public enum NativeUserSessionDispatchOutcome
    {
        Dispatch,
        SilentDrop,
        TerminalReject
    }

    /// <summary>
    /// Native 2.08 UserSoc opcode admission before the per-state dispatch
    /// tables. State-specific helpers consume the raw client ident; the
    /// state-zero helper is also usable with its native numeric values because
    /// those three commands are not mobile aliases.
    /// </summary>
    public static class NativeUserSessionDispatchProtocol
    {
        public const ushort StateZeroLoginAuthCommand = 0x0FA4;
        public const ushort StateZeroDirectAuthCommand = 0x0FA8;
        public const ushort StateZeroReconnectCommand = 0x0FBF;

        public const ushort StateFiveNewCharacterCommand = 0x0FAC;
        public const ushort StateFiveDeleteCharacterCommand = 0x0FAD;
        public const ushort StateFiveQueryDeletedCommand = 0x0FAE;
        public const ushort StateFiveRestoreDeletedCommand = 0x0FAF;
        public const ushort StateFiveRenameCharacterCommand = 0x0FB0;
        public const ushort StateFiveSelectCharacterCommand = 0x0FB1;
        public const ushort StateFiveExitAckCommand = 0x0FC7;
        public const ushort StateFiveAlreadyOnlineCommand = 0x0FC9;

        /// <summary>
        /// Reproduces the cumulative subtract chain at
        /// <c>0x005CE07D..0x005CE08D</c>. State zero admits exactly 4004,
        /// 4008, and 4031. Opcode 4027 belongs to the separate state-four
        /// table and must not be admitted here.
        /// </summary>
        public static bool IsStateZeroOpcodeAllowed(ushort ident)
            => ident == StateZeroLoginAuthCommand
               || ident == StateZeroDirectAuthCommand
               || ident == StateZeroReconnectCommand;

        /// <summary>
        /// Reproduces the queue gate and corrected 30-byte L1 table at
        /// <c>0x005CE307..0x005CE362</c>. This decision consumes the raw wire
        /// opcode because mobile mapping aliases raw 4017 and raw 103.
        /// </summary>
        public static NativeUserSessionDispatchOutcome ClassifyStateFiveOpcode(
            ushort rawClientIdent, ushort queuePosition)
        {
            if (queuePosition > 0
                && rawClientIdent != StateFiveExitAckCommand)
                return NativeUserSessionDispatchOutcome.SilentDrop;

            return rawClientIdent is StateFiveNewCharacterCommand
                or StateFiveDeleteCharacterCommand
                or StateFiveQueryDeletedCommand
                or StateFiveRestoreDeletedCommand
                or StateFiveRenameCharacterCommand
                or StateFiveSelectCharacterCommand
                or StateFiveExitAckCommand
                or StateFiveAlreadyOnlineCommand
                    ? NativeUserSessionDispatchOutcome.Dispatch
                    : NativeUserSessionDispatchOutcome.TerminalReject;
        }
    }
}
