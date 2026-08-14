using System;
using System.IO;
using GameSvr.PasEngine;

namespace GameSvr.ScriptSystem
{
    // ================================================================================================
    // LogoutQuest executor — the minimal, validated interface (MVI) for running PsMapQuest/LogoutQuest
    // scripts when a player logs out. This executor provides a type-safe, auditable seam that:
    //   (a) validates the script exists before attempting execution,
    //   (b) isolates the call path so it can be gated/monitored,
    //   (c) provides clear success/failure outcomes without exceptions leaking to callers.
    //
    // Native behavior (PAS script system, sub_731350 procedure registration table):
    //   - LogoutQuest scripts are stored in PsMapQuest/LogoutQuest/*.pas
    //   - Scripts are executed in the context of the logging-out player
    //   - Execution is fire-and-forget: script errors do not block logout
    //   - No return value is expected or consumed
    //
    // Conservation/safety properties:
    //   - READ-ONLY from the executor's perspective: the script may mutate player state via the
    //     PasApiBridge API, but the executor itself holds no state and performs no direct mutations.
    //   - ISOLATION: script exceptions are caught and logged; they never propagate to the logout path.
    //   - FAIL-SAFE: if the script is missing or fails to compile/execute, the outcome reflects this
    //     but the player logout proceeds normally (native behavior: scripts are optional enhancements).
    //
    // Usage:
    //   var outcome = LogoutQuestExecutor.Execute(player, scriptHost, "MyLogoutScript");
    //   if (!outcome.Succeeded) { /* log or handle, but continue logout */ }
    // ================================================================================================
    public static class LogoutQuestExecutor
    {
        private const string ScriptSubDirectory = "LogoutQuest";

        /// <summary>
        /// Execute a LogoutQuest script for the given player. The script is located in
        /// PsMapQuest/LogoutQuest/{scriptName}.pas and is invoked with the player as context.
        /// </summary>
        /// <param name="player">The player logging out (context for the script).</param>
        /// <param name="scriptHost">The PAS script host (provides script loading and execution).</param>
        /// <param name="scriptName">The script name (without .pas extension).</param>
        /// <returns>An outcome describing success or the reason for failure.</returns>
        public static LogoutQuestOutcome Execute(
            TPlayObject player,
            PasScriptHost scriptHost,
            string scriptName)
        {
            var outcome = new LogoutQuestOutcome { ScriptName = scriptName };

            // Gate: validate inputs
            if (player == null)
            {
                outcome.Code = LogoutQuestResultCode.InvalidPlayer;
                outcome.Message = "Player is null";
                return outcome;
            }

            if (scriptHost == null)
            {
                outcome.Code = LogoutQuestResultCode.ScriptHostUnavailable;
                outcome.Message = "Script host is null";
                return outcome;
            }

            if (string.IsNullOrWhiteSpace(scriptName))
            {
                outcome.Code = LogoutQuestResultCode.InvalidScriptName;
                outcome.Message = "Script name is empty or whitespace";
                return outcome;
            }

            // Resolve the script path via the script host's resolution logic
            string scriptPath = scriptHost.ResolveScriptPath(scriptName);

            // If not found via standard resolution, try explicit LogoutQuest directory
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                // Construct explicit path: PsMapQuest/LogoutQuest/{scriptName}.pas
                var envirPath = scriptHost.GetEnvirPath();
                if (!string.IsNullOrEmpty(envirPath))
                {
                    scriptPath = Path.Combine(envirPath, "PsMapQuest", ScriptSubDirectory, scriptName + ".pas");
                }
            }

            // Gate: script must exist
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                outcome.Code = LogoutQuestResultCode.ScriptNotFound;
                outcome.Message = $"Script not found: {scriptName} (searched: {scriptPath ?? "N/A"})";
                return outcome;
            }

            outcome.ResolvedPath = scriptPath;

            // Execute the script's main procedure with the player as context
            // Convention: LogoutQuest scripts typically have a parameterless main procedure or
            // use implicit player context via the PasApiBridge.
            try
            {
                // Attempt to call a procedure named after the script, or fall back to a standard
                // entry point. Native behavior: scripts define their own entry points; the exact
                // convention may vary. We use TryCallProcedure for safe, non-throwing invocation.
                bool success = scriptHost.TryCallProcedure(
                    scriptPath,
                    "main",  // Standard entry point convention
                    player,
                    null,    // No NPC context for logout scripts
                    out PasValue result);

                if (success)
                {
                    outcome.Code = LogoutQuestResultCode.Success;
                    outcome.Message = "Script executed successfully";
                }
                else
                {
                    // TryCallProcedure returns false if the procedure is not found or execution fails.
                    // This is not necessarily an error: the script may not define "main", or may use
                    // a different entry point. Native behavior: missing procedures are silently ignored.
                    outcome.Code = LogoutQuestResultCode.ProcedureNotFound;
                    outcome.Message = $"Procedure 'main' not found or failed in {scriptName}";
                }
            }
            catch (Exception ex)
            {
                // Catch any unexpected exceptions (should be rare, as TryCallProcedure is designed
                // to handle errors internally). Log and return a failure outcome, but do NOT propagate.
                outcome.Code = LogoutQuestResultCode.ExecutionError;
                outcome.Message = $"Unexpected error executing {scriptName}: {ex.Message}";
                outcome.Exception = ex;
            }

            return outcome;
        }
    }

    /// <summary>
    /// Result codes for LogoutQuest execution.
    /// </summary>
    public enum LogoutQuestResultCode
    {
        /// <summary>Script executed successfully.</summary>
        Success = 0,

        /// <summary>Player parameter is null.</summary>
        InvalidPlayer = -1,

        /// <summary>Script host is unavailable or null.</summary>
        ScriptHostUnavailable = -2,

        /// <summary>Script name is empty or invalid.</summary>
        InvalidScriptName = -3,

        /// <summary>Script file was not found in the expected location.</summary>
        ScriptNotFound = -4,

        /// <summary>Script was found but the entry procedure was not found or failed.</summary>
        ProcedureNotFound = -5,

        /// <summary>An unexpected error occurred during script execution.</summary>
        ExecutionError = -6,
    }

    /// <summary>
    /// Outcome of a LogoutQuest script execution.
    /// </summary>
    public sealed class LogoutQuestOutcome
    {
        /// <summary>Result code indicating success or the reason for failure.</summary>
        public LogoutQuestResultCode Code { get; set; }

        /// <summary>Human-readable message describing the outcome.</summary>
        public string Message { get; set; }

        /// <summary>The name of the script that was requested (without .pas extension).</summary>
        public string ScriptName { get; set; }

        /// <summary>The resolved full path to the script file (if found).</summary>
        public string ResolvedPath { get; set; }

        /// <summary>Exception details, if an unexpected error occurred.</summary>
        public Exception Exception { get; set; }

        /// <summary>True if the script executed successfully.</summary>
        public bool Succeeded => Code == LogoutQuestResultCode.Success;

        /// <summary>True if the failure was due to a missing script (a common, non-critical case).</summary>
        public bool IsScriptMissing => Code == LogoutQuestResultCode.ScriptNotFound;
    }
}
