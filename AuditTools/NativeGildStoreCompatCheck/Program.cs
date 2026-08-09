using System.Text;
using System.Text.RegularExpressions;
using GameSvr.Services;

// NativeGildStoreCompatCheck — proves the SQL each NativeGildMySqlStore write op
// builds matches the reversed original M2Server SQL, with test inputs and no
// live database. For every op we assert three things:
//   (1) the parameterized CommandText equals the reviewed golden literal,
//   (2) it is structurally identical to the exact reversed %d/%s original
//       (same statement/tables/columns/order/WHERE) via a placeholder- and
//       whitespace-insensitive normalization, and
//   (3) the ordered parameters carry the test inputs under the right kind.
//
// Original strings are quoted verbatim from the binary .rdata (image base
// 0x400000); addresses match staging/update_clothes_4637_ida_work/all_strings.txt
// and the builders in staging/ida_gild_write_inventory_20260731.txt.

var failures = new List<string>();

// ---- 0x005E9414  make_save_gild family (create-gild INSERT) ----
Check("CreateGild",
    NativeGildMySqlStore.BuildCreateGild(111L, "堂主行会", 222L, 333L),
    "INSERT INTO gamedata.Gild(ID,CreateTime,GildName,OwnerCorpsID," +
    "ViceOwnerID) VALUES(@id,NOW(),@name,@owner,@vice)",
    "Insert into gamedata.Gild(ID, CreateTime, GildName, OwnerCorpsID, " +
    "ViceOwnerID)   Values(%d, now(), '%s', %d, %d);",
    new[]
    {
        P("@id", NativeGildSqlValueKind.Id, 111L),
        P("@name", NativeGildSqlValueKind.GbkText, "堂主行会"),
        P("@owner", NativeGildSqlValueKind.Id, 222L),
        P("@vice", NativeGildSqlValueKind.Id, 333L)
    });

// ---- 0x005E9568  make_save_gild sub_5E926C (save-gild UPDATE) ----
Check("SaveGild",
    NativeGildMySqlStore.BuildSaveGild(444L, 555L, 666L,
        Encoding.ASCII.GetBytes("notice")),
    "UPDATE gamedata.Gild SET OwnerCorpsID=@owner,ViceOwnerID=@vice," +
    "GildNotice=@notice WHERE ID=@id",
    "update gamedata.Gild set OwnerCorpsID = %d, ViceOwnerID = %d, " +
    "GildNotice = '%s' where ID = %d;",
    new[]
    {
        P("@owner", NativeGildSqlValueKind.Id, 555L),
        P("@vice", NativeGildSqlValueKind.Id, 666L),
        P("@notice", NativeGildSqlValueKind.Binary,
            Encoding.ASCII.GetBytes("notice")),
        P("@id", NativeGildSqlValueKind.Id, 444L)
    });

// ---- 0x005E96D4  gildmember INSERT ----
Check("InsertGildMember",
    NativeGildMySqlStore.BuildInsertGildMember(10L, 20L),
    "INSERT INTO gamedata.gildmember(GildID,CorpsID) VALUES(@gild,@corps)",
    "Insert into gamedata.gildmember(GildID, CorpsID) Values(%d, %d);",
    new[]
    {
        P("@gild", NativeGildSqlValueKind.Id, 10L),
        P("@corps", NativeGildSqlValueKind.Id, 20L)
    });

// ---- 0x005E97E4  make_delete_gild_member sub_5E95E0 / execute sub_5E9620 ----
Check("DeleteGildMember",
    NativeGildMySqlStore.BuildDeleteGildMember(30L, 40L),
    "DELETE FROM gamedata.gildmember WHERE GildID=@gild AND CorpsID=@corps",
    "delete from gamedata.gildmember where GildID = %d and CorpsID = %d;",
    new[]
    {
        P("@gild", NativeGildSqlValueKind.Id, 30L),
        P("@corps", NativeGildSqlValueKind.Id, 40L)
    });

// ---- 0x005E998C  save_relation sub_5E6E60 -> sub_5E9840 (INSERT) ----
var createdAt = new DateTime(2026, 7, 31, 12, 34, 56);
Check("InsertGildRelation",
    NativeGildMySqlStore.BuildInsertGildRelation(50L, 60L, 2, createdAt),
    "INSERT INTO gamedata.gildrelation(GildID1,GildID2,Relation,CreateTime) " +
    "VALUES(@g1,@g2,@relation,@created)",
    "Insert into gamedata.gildrelation(GildID1, GildID2, Relation, " +
    "CreateTime)   Values(%d, %d, %d, \"%s\");",
    new[]
    {
        P("@g1", NativeGildSqlValueKind.Id, 50L),
        P("@g2", NativeGildSqlValueKind.Id, 60L),
        P("@relation", NativeGildSqlValueKind.Int32, 2),
        P("@created", NativeGildSqlValueKind.DateTime, createdAt)
    });

// ---- 0x005E9AC0  gildrelation DELETE ----
Check("DeleteGildRelation",
    NativeGildMySqlStore.BuildDeleteGildRelation(70L, 80L),
    "DELETE FROM gamedata.gildrelation WHERE GildID1=@g1 AND GildID2=@g2",
    "delete from gamedata.gildrelation where GildID1 = %d and GildID2 = %d;",
    new[]
    {
        P("@g1", NativeGildSqlValueKind.Id, 70L),
        P("@g2", NativeGildSqlValueKind.Id, 80L)
    });

// ---- 0x005E9C28  gildconcern INSERT (execute sub_5E9B74) ----
Check("InsertGildConcern",
    NativeGildMySqlStore.BuildInsertGildConcern(90L, 100L),
    "INSERT INTO gamedata.gildconcern(GildID,DstGildID) VALUES(@gild,@dst)",
    "Insert into gamedata.gildconcern(GildID, DstGildID) Values(%d, %d);",
    new[]
    {
        P("@gild", NativeGildSqlValueKind.Id, 90L),
        P("@dst", NativeGildSqlValueKind.Id, 100L)
    });

// ---- 0x005E9D38  make_delete_concern sub_5E9B20 / execute sub_5E9C84 ----
Check("DeleteGildConcern",
    NativeGildMySqlStore.BuildDeleteGildConcern(110L, 120L),
    "DELETE FROM gamedata.gildconcern WHERE GildID=@gild AND DstGildID=@dst",
    "delete from gamedata.gildconcern where GildID = %d and DstGildID = %d;",
    new[]
    {
        P("@gild", NativeGildSqlValueKind.Id, 110L),
        P("@dst", NativeGildSqlValueKind.Id, 120L)
    });

// The dormant concern-delete transaction ships its own legacy template — make
// sure it stays byte-identical to the reversed original as well.
AssertEqual("NativeGildConcernDeleteCommand.LegacySqlTemplate",
    "delete from gamedata.gildconcern where GildID = %d and DstGildID = %d;",
    NativeGildConcernDeleteCommand.LegacySqlTemplate);

if (failures.Count == 0)
{
    Console.WriteLine(
        "NativeGildStoreCompatCheck: PASS (8 gild write ops + legacy template)");
    return 0;
}

Console.Error.WriteLine("NativeGildStoreCompatCheck: FAIL");
foreach (var failure in failures) Console.Error.WriteLine("  - " + failure);
return 1;

void Check(string op, NativeGildSqlCommand built, string golden,
    string original, NativeGildSqlParameter[] expected)
{
    // (1) golden parameterized literal
    if (built.CommandText != golden)
        failures.Add($"{op}: CommandText != golden const\n" +
                     $"      built = {built.CommandText}\n" +
                     $"      const = {golden}");

    // (2) structural fidelity to the exact reversed original
    var normBuilt = Normalize(built.CommandText);
    var normOriginal = Normalize(original);
    if (normBuilt != normOriginal)
        failures.Add($"{op}: normalized SQL != reversed original\n" +
                     $"      built    = {normBuilt}\n" +
                     $"      original = {normOriginal}");

    // (3) ordered parameter binding of the test inputs
    if (built.Parameters.Count != expected.Length)
    {
        failures.Add($"{op}: parameter count {built.Parameters.Count} != " +
                     $"{expected.Length}");
        return;
    }
    for (var i = 0; i < expected.Length; i++)
    {
        var a = built.Parameters[i];
        var e = expected[i];
        if (a.Name != e.Name || a.Kind != e.Kind || !ValueEquals(a.Value,
                e.Value))
            failures.Add($"{op}: parameter[{i}] mismatch\n" +
                         $"      built    = {Describe(a)}\n" +
                         $"      expected = {Describe(e)}");
    }
}

void AssertEqual(string what, string expected, string actual)
{
    if (expected != actual)
        failures.Add($"{what}: '{actual}' != '{expected}'");
}

// Canonicalize a statement for structural comparison: drop sprintf (%d/%s) and
// @named placeholders and the quotes that wrap string placeholders, remove all
// whitespace, lowercase, and trim the trailing ';'. What remains is the SQL
// skeleton — statement, tables, columns (with order) and clauses.
static string Normalize(string sql)
{
    var text = sql.ToLowerInvariant();
    text = Regex.Replace(text, "%[ds]", string.Empty);
    text = Regex.Replace(text, "@[a-z0-9_]+", string.Empty);
    text = text.Replace("'", string.Empty).Replace("\"", string.Empty);
    text = Regex.Replace(text, "\\s+", string.Empty);
    return text.TrimEnd(';');
}

static NativeGildSqlParameter P(string name, NativeGildSqlValueKind kind,
    object value) => new(name, kind, value);

static bool ValueEquals(object a, object b)
{
    if (a is byte[] ab && b is byte[] bb) return ab.AsSpan().SequenceEqual(bb);
    return Equals(a, b);
}

static string Describe(NativeGildSqlParameter p)
{
    var value = p.Value is byte[] bytes
        ? "0x" + Convert.ToHexString(bytes)
        : Convert.ToString(p.Value);
    return $"{p.Name} {p.Kind} {value}";
}
