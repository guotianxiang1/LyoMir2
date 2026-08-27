namespace DBSvr.Core
{
    /// <summary>
    /// Native 2.08 UserSoc opcode admission before the per-state dispatch
    /// tables. Values are the internal packet idents after mobile mapping.
    /// </summary>
    public static class NativeUserSessionDispatchProtocol
    {
        public const ushort StateZeroLoginAuthCommand = 0x0FA4;
        public const ushort StateZeroDirectAuthCommand = 0x0FA8;
        public const ushort StateZeroReconnectCommand = 0x0FBF;

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
    }
}
