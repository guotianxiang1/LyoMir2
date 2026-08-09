using System.Text;
using MySql.Data.MySqlClient;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

var gbk = Encoding.GetEncoding(936);
var latin1 = Encoding.GetEncoding(28591);
const string connectionString = "Server=127.0.0.1;Database=mir3;Uid=root;Pwd=dsdfffsadsd;charset=gbk;";

using var connection = new MySqlConnection(connectionString);
connection.Open();

using (var charsetCommand = new MySqlCommand(
           "SELECT @@character_set_client, @@character_set_connection, @@character_set_results", connection))
using (var charsetReader = charsetCommand.ExecuteReader())
{
    charsetReader.Read();
    Console.WriteLine($"charset client={Convert.ToString(charsetReader[0])} connection={Convert.ToString(charsetReader[1])} results={Convert.ToString(charsetReader[2])}");
}

using var command = new MySqlCommand(
    "SELECT idx, iname, CAST(iname AS BINARY) AS raw_name, HEX(iname) AS raw_hex FROM stditems WHERE idx IN (16,17,29) ORDER BY idx", connection);
using var reader = command.ExecuteReader();
while (reader.Read())
{
    var idx = reader.GetInt32("idx");
    var value = reader["iname"];
    var text = Convert.ToString(value) ?? string.Empty;
    var raw = (byte[])reader["raw_name"];
    var currentHelper = text.Any(ch => ch >= 0x2E80)
        ? text
        : gbk.GetString(latin1.GetBytes(text));

    Console.WriteLine($"idx={idx} valueType={value.GetType().FullName} rawHex={reader.GetString("raw_hex")}");
    Console.WriteLine($"  text={text} codepoints={CodePoints(text)} latin1Hex={Convert.ToHexString(latin1.GetBytes(text))}");
    Console.WriteLine($"  currentHelper={currentHelper} helperCodepoints={CodePoints(currentHelper)}");
    Console.WriteLine($"  binaryHex={Convert.ToHexString(raw)} binaryGbk={gbk.GetString(raw)}");
}

static string CodePoints(string value) => string.Join(' ', value.Select(ch => $"U+{(int)ch:X4}"));
