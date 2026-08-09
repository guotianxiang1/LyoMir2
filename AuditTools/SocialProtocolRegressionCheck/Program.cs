using System.Reflection;
using SystemModule;

var expected = new Dictionary<string, int>
{
    ["CM_REPLY_GROUP_MESSAGE"] = 4412,
    ["SM_NOTIFY_GROUP_MESSAGE"] = 4412,
    ["CM_QUERY_STALL"] = 4418,
    ["CM_QUERY_RELATION_FRIEND"] = 4430,
    ["SM_SEND_RELATION_FRIEND"] = 4430,
    ["CM_QUERY_RELATION_ATTENTION"] = 4431,
    ["SM_SEND_RELATION_ATTENTION"] = 4431,
    ["CM_QUERY_RELATION_NORMBLACKLIST"] = 4432,
    ["SM_SEND_RELATION_NORMBLACKLIST"] = 4432,
    ["CM_ADD_RELATION_FRIEND"] = 4433,
    ["SM_ADD_RELATION_FRIEND_OK"] = 4433,
    ["SM_ADD_RELATION_FRIEND_FAIL"] = 4434,
    ["CM_ADD_RELATION_ATTENTION"] = 4435,
    ["SM_ADD_RELATION_ATTENTION"] = 4435,
    ["CM_ADD_RELATION_NORMBLACKLIST"] = 4436,
    ["SM_ADD_RELATION_NORMBLACKLIST"] = 4436,
    ["CM_DEL_RELATION_FRIEND"] = 4437,
    ["SM_DEL_RELATION_FRIEND"] = 4437,
    ["CM_DEL_RELATION_ATTENTION"] = 4438,
    ["SM_DEL_RELATION_ATTENTION"] = 4438,
    ["CM_DEL_RELATION_NORMBLACKLIST"] = 4439,
    ["SM_DEL_RELATION_NORMBLACKLIST"] = 4439,
    ["CM_UPDATE_ATTENTION_COLOR"] = 4440,
    ["SM_UPDATE_ATTENTION_COLOR"] = 4440,
    ["CM_CHANNEL_CREATE"] = 4447,
    ["CM_CHANNEL_ENTER"] = 4448,
    ["CM_CHANNEL_EXIT"] = 4449,
    ["CM_CHANNEL_CHANGE_MODE"] = 4450,
    ["CM_CHANNEL_CHANGE_MUTE"] = 4451,
    ["CM_CHANNEL_KICK_OUT"] = 4452,
    ["CM_QUERY_CHANNEL_LIST"] = 4453,
    ["CM_QUERY_CHANNEL_MEMBERS"] = 4454,
    ["CM_FETCH_ATTACH"] = 4462,
    ["SM_FETCH_ATTACH"] = 4462,
    ["CM_DEL_MAIL"] = 4463,
    ["SM_DEL_MAIL"] = 4463,
    ["CM_FETCH_ATTACH_OFFTM"] = 4468,
    ["CM_PLAYER_GILD"] = 4500,
    ["CM_PLAYER_CORPS"] = 4501,
    ["CM_GILD_ACCEPT_REQUEST"] = 4611,
    ["CM_FIND_CORPS_BYNAME"] = 4616,
    ["CM_FIND_GILD_BYNAME"] = 4617,
    ["CM_GILD_CANCEL_JOIN"] = 4627,
    ["CM_REFRESH_CORPSINFO"] = 4631,
    ["CM_REFRESH_GILDINFO"] = 4632,
    ["CM_CLICK_BACKHOME"] = 4633,
};

for (var protocol = 4520; protocol <= 4540; protocol++)
{
    var name = protocol switch
    {
        4520 => "CORPS_LIST",
        4521 => "CORPS_QUERY_JOIN",
        4522 => "CORPS_REQUEST_JOIN",
        4523 => "CORPS_CANCEL_JOIN",
        4524 => "CORPS_CREATE",
        4525 => "CORPS_MEMBER_LIST",
        4526 => "CORPS_SET_MEMBER_TITLE",
        4527 => "CORPS_DISMISS_MEMBER",
        4528 => "CORPS_TRANSFER_CAPTAIN",
        4529 => "CORPS_APPOINT_VICE_CAPTAIN",
        4530 => "CORPS_STEPDOWN",
        4531 => "CORPS_GET_RECRUIT_CONDITION",
        4532 => "CORPS_SET_RECRUIT_CONDITION",
        4533 => "CORPS_DIRECT_ADD_MEMBER",
        4534 => "CORPS_QUERY_REQUESTS",
        4535 => "CORPS_ACCEPT_REQUEST",
        4536 => "CORPS_REFUSE_REQUEST",
        4537 => "CORPS_QUERY_LOG",
        4538 => "CORPS_EXIT",
        4539 => "CORPS_NOTICE",
        4540 => "CORPS_DISMISS_VICE_CAPTAIN",
        _ => throw new InvalidOperationException()
    };
    expected[$"CM_{name}"] = protocol;
    expected[$"SM_{name}"] = protocol;
}

var gildNames = new Dictionary<int, string>
{
    [4560] = "GILD_REQUEST_JOIN",
    [4562] = "GILD_LIST",
    [4563] = "GILD_NOTICE",
    [4564] = "GILD_CREATE",
    [4565] = "GILD_QUERY_CORPS",
    [4566] = "GILD_QUERY_PRESIDENT",
    [4567] = "GILD_DISMISS_CORPS",
    [4568] = "GILD_TRANSFER_PRESIDENT",
    [4569] = "GILD_APPOINT_VICE_PRESIDENT",
    [4570] = "GILD_QUERY_REQUEST_JOIN_LIST",
    [4571] = "GILD_QUERY_REQUEST_UNION_LIST",
    [4572] = "GILD_REFUSE_REQUEST",
    [4573] = "GILD_REQUEST_UNION",
    [4574] = "GILD_BREAK_UNION",
    [4575] = "GILD_QUERY_UNION",
    [4576] = "GILD_CONCERN_GILD_ID",
    [4577] = "GILD_QUERY_CONCERN",
    [4578] = "GILD_CANCLE_CONCERN",
    [4579] = "GILD_DECLARE_WAR",
    [4580] = "GILD_QUERY_HOSTILE",
    [4581] = "GILD_ENABLE_UNION",
    [4582] = "GILD_QUERY_LOG",
    [4583] = "GILD_EXIT",
    [4584] = "GILDMEMBER_LIST",
    [4585] = "GILD_DECLARE_WAR_NAME",
    [4586] = "GILD_CONCERN_GILD_NAME",
    [4587] = "GILD_VICECAPTAIN_STEPDOWN",
    [4588] = "GILD_DISMISS_VICECAPTAIN",
};
foreach (var (protocol, name) in gildNames)
{
    expected[$"CM_{name}"] = protocol;
    expected[$"SM_{name}"] = protocol;
}

var fields = typeof(Grobal2).GetFields(BindingFlags.Public | BindingFlags.Static);
var constants = fields
    .Where(field => field.IsLiteral && field.FieldType == typeof(int))
    .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!);

foreach (var (name, value) in expected)
{
    Require(constants.TryGetValue(name, out var actual), $"missing constant {name}");
    Require(actual == value, $"{name}: expected {value}, got {actual}");
}

var root = FindRepositoryRoot();
var messageSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players", "TPlayObject.Message.cs"));
var protocolSource = File.ReadAllText(Path.Combine(root, "SystemModule", "Grobal2.cs"));
foreach (var forbidden in new[]
{
    "m_sString[20]",
    "SM_RELATION_RESULT",
    "_mailList",
    "MailEntry",
    "SM_MAIL_ATTACH_RESULT",
})
{
    Require(!messageSource.Contains(forbidden, StringComparison.Ordinal),
        $"non-native relation/mail implementation returned: {forbidden}");
}

foreach (var forbidden in new[]
{
    "CM_QUERYUSERLEVELSORT",
    "SM_QUERYUSERLEVELSORT",
    "RM_QUERYUSERLEVELSORT",
})
{
    Require(!protocolSource.Contains(forbidden, StringComparison.Ordinal),
        $"non-native rank protocol returned: {forbidden}");
    Require(!messageSource.Contains(forbidden, StringComparison.Ordinal),
        $"non-native rank dispatcher returned: {forbidden}");
}

Require(messageSource.Contains("ClientCreateGroup(ProcessMsg.sMsg.Trim())", StringComparison.Ordinal),
    "CM_CREATEGROUP must pass only the payload name to ClientCreateGroup");
Require(!messageSource.Contains("ClientHandleCreateGroupRequest", StringComparison.Ordinal),
    "CM_CREATEGROUP must not dispatch on invented numeric modes");

Console.WriteLine($"SocialProtocolRegressionCheck PASS ({expected.Count} constants, native dispatcher guards)");

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SystemModule", "Grobal2.cs")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
