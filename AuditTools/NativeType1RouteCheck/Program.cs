using System.Text.RegularExpressions;

var root = AuditRepoRoot.Resolve(args);
var sourcePath = Path.Combine(root, "GameSvr", "Services", "DBService.cs");
if (!File.Exists(sourcePath))
{
    Console.WriteLine("SKIP: GameSvr/Services/DBService.cs not found under " + root);
    return 0;
}

var source = File.ReadAllText(sourcePath);
var start = source.IndexOf("private static void ProcessNativeType1",
    StringComparison.Ordinal);
var end = source.IndexOf("private bool SendRegistration", start,
    StringComparison.Ordinal);
Check(start >= 0 && end > start,
    "DBService.ProcessNativeType1 boundaries are missing");
var route = source[start..end];

RequireSingle(route, "HumDataService.AddNativeLoadFrame(frame)",
    "0050 human-load owner");
RequireSingle(route, "NativeForceDisconnectClient.ProcessResponse(frame)",
    "0052 force-disconnect owner");
RequireSingle(route, "NativeWhitelistReloadClient.ProcessResponse(frame)",
    "0132 whitelist-reload owner");
RequireSingle(route, "NativeItemExtractionClient.ProcessResponse(frame)",
    "0055 item-extraction owner");
RequireSingle(route, "NativeItemInjectionClient.ProcessResponse(frame)",
    "0056 mail-injection owner");
RequireSingle(route, "NativeType1YbTransactionAck.TryProcessResponse(frame)",
    "0060/0061 YB acknowledgement owner");
RequireSingle(route, "NativeAccountStorageClient.ProcessResponse(frame)",
    "0062/0063 account-storage owner");
RequireSingle(route,
    "NativeHeroAuxiliaryResponseClient.ProcessResponse(frame)",
    "005D/005E/0070 hero auxiliary owner");
RequireSingle(route, "HeroDataService.TryAddNativeResponse(wire)",
    "hero response owner");

Require(route,
    @"command\s*==\s*(?:0x0050|NativeDbServerProtocol\.LoadHumanCommand)",
    "0050 command is not routed to the human loader");
Require(route,
    @"command\s*==\s*NativeForceDisconnectClient\.ResponseCommand",
    "0052 constant is not routed");
Require(route,
    @"command\s*==\s*NativeWhitelistReloadClient\.ResponseCommand",
    "0132 constant is not routed");
Require(route,
    @"command\s*==\s*NativeItemExtractionProtocol\.ResponseCommand",
    "0055 constant is not routed");
Require(route,
    @"command\s*==\s*NativeItemInjectionProtocol\.MailResponseCommand",
    "0056 constant is not routed");
Require(route,
    @"command\s+is\s+NativeType1YbTransactionAck\s*\.BagInjectionResponseCommand\s+or\s+NativeType1YbTransactionAck\.AwardPlayerResponseCommand",
    "0060/0061 are not routed as the original YB acknowledgement pair");
Require(route,
    @"if\s*\(\s*command\s+is\s+NativeAccountStorageClient\.LoadResponseCommand\s+or\s+NativeAccountStorageClient\.SaveResponseCommand\s*\)\s*\{\s*NativeAccountStorageClient\.ProcessResponse\(frame\);\s*return;\s*\}",
    "0062/0063 must be one account-storage pair with terminal return");
Require(route,
    @"command\s+is\s+NativeHeroDbFrameCodec\.ConsignedListResponseCommand\s+or\s+NativeHeroDbFrameCodec\.RestoreConsignedResponseCommand\s+or\s+NativeHeroDbFrameCodec\.BuildThreeSlotResponseCommand",
    "005D/005E/0070 are not routed as the exact hero auxiliary group");

foreach (var heroResponse in new[]
         {
             "LoadResponseCommand", "CreateResponseCommand",
             "RenameResponseCommand"
         })
    Check(route.Contains("NativeHeroDbFrameCodec." + heroResponse,
            StringComparison.Ordinal),
        "missing active hero response route " + heroResponse);

Check(!route.Contains("NativeHeroDbFrameCodec.DeleteResponseCommand",
        StringComparison.Ordinal),
    "0059 must remain ignored: the original Type1 jump table selects default");
Check(!route.Contains("NativeItemInjectionProtocol.BagResponseCommand",
        StringComparison.Ordinal),
    "0060 must not be consumed by NativeItemInjectionClient; it is the YB ACK");
Check(!Regex.IsMatch(route,
        @"command\s*(?:>=|<=|>|<)\s*NativeHeroDbFrameCodec",
        RegexOptions.CultureInvariant),
    "hero responses must not use a range that swallows non-hero Type1 commands");

Console.WriteLine("NativeType1RouteCheck PASS " +
                  "0050=human 0052=kick 0055=extract 0056=mail " +
                  "005D/5E/70=hero-aux " +
                  "0060/61=yb-ack 0062/63=account-storage " +
                  "0132=whitelist-reload " +
                  "hero=0051/0053/005A 0059=ignored");
return 0;

static void Require(string source, string pattern, string message) =>
    Check(Regex.IsMatch(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.Singleline), message);

static void RequireSingle(string source, string value, string owner)
{
    var count = 0;
    var offset = 0;
    while ((offset = source.IndexOf(value, offset,
               StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    Check(count == 1, $"{owner} call count is {count}, expected 1");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
