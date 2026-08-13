using System.Collections.ObjectModel;
using System.Text;
using SystemModule;

namespace GameSvr
{
    public sealed class NativeDiamondFoundry
    {
        public const int MaximumMaterials = 9;
        public const int NameMaximumGbkBytes = 15;
        public const string UnavailableDialog = "金刚石锻造功能暂时关闭中。";
        public const string EmptyDialog =
            "金刚石锻造功能暂时关闭中。\\ \\ \\ <返回/@main>";
        public const string ListPrefix =
            "请选择锻造的武器装备：    <返回/@main>\\\r\n";
        public const string ConfirmText =
            "\\ \\请您确认以上配方的物品已经放入包裹内。";
        public const string ConfirmCommands =
            "\\ \\|{cmd}<开始锻造/@MakeDiamondItem>    ^<查看其他配方/@FOUNDRYLIST> ";

        private readonly ReadOnlyCollection<Recipe> _recipes;
        private readonly ReadOnlyCollection<string> _recipeDialogs;
        private readonly ReadOnlyCollection<byte[]> _recipeDialogGbkBytes;
        private readonly byte[] _foundryListDialogGbkBytes;

        private NativeDiamondFoundry(IList<Recipe> recipes, bool sourceLoaded,
            int skippedRowCount)
        {
            SourceLoaded = sourceLoaded;
            SkippedRowCount = skippedRowCount;
            _recipes = new ReadOnlyCollection<Recipe>(recipes);

            var recipeDialogs = new List<string>(recipes.Count);
            var recipeDialogGbkBytes = new List<byte[]>(recipes.Count);
            foreach (var recipe in recipes)
            {
                var rawDialog = BuildRecipeDialogGbk(recipe);
                recipeDialogGbkBytes.Add(rawDialog);
                recipeDialogs.Add(BuildRecipeDialog(recipe));
            }
            _recipeDialogs = recipeDialogs.AsReadOnly();
            _recipeDialogGbkBytes = recipeDialogGbkBytes.AsReadOnly();
            _foundryListDialogGbkBytes = BuildFoundryListDialogGbk();
            FoundryListDialog = BuildFoundryListDialog();
        }

        public static NativeDiamondFoundry Unavailable { get; } =
            new NativeDiamondFoundry(Array.Empty<Recipe>(), false, 0);

        public string FoundryListDialog { get; }
        public ReadOnlyMemory<byte> FoundryListDialogGbkBytes =>
            _foundryListDialogGbkBytes;
        public IReadOnlyList<Recipe> Recipes => _recipes;
        public int SkippedRowCount { get; }
        public bool SourceLoaded { get; }

        public static bool TryLoad(string fileName,
            out NativeDiamondFoundry foundry, out string error)
        {
            foundry = Unavailable;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Gifts.txt path is empty";
                return false;
            }
            if (!File.Exists(fileName))
            {
                error = $"file not found: {fileName}";
                return false;
            }

            var lines = File.ReadAllLines(fileName, HUtil32.GbkEncoding);

            var recipes = new List<Recipe>(lines.Length);
            var skippedRows = 0;
            // sub_74F818 在函数入口一次性把 [ebp-0x10]（名字）与 [ebp-0x1C]（数量文本）
            // 清零（0x74F847 / 0x74F850），此后只有 Pos(':', token) > 0 才重写它们
            // （0x74F9E1 jle 0x74FA0B 跳过两次 Copy）。所以缺冒号的 token 会沿用上一个
            // token 的名字与数量，而且因为清零在文件循环之外，这个沿用会跨行。
            var carriedName = string.Empty;
            var carriedNumber = string.Empty;
            foreach (var line in lines)
            {
                if (TryParseRecipe(line, ref carriedName, ref carriedNumber,
                        out var recipe))
                    recipes.Add(recipe);
                else
                    skippedRows++;
            }

            foundry = new NativeDiamondFoundry(recipes, true, skippedRows);
            return true;
        }

        public bool TryBuildSelectionDialog(int zeroBasedRecipeIndex,
            out int oneBasedSelector, out string dialog)
        {
            oneBasedSelector = 0;
            dialog = null;
            var oneBased = unchecked(zeroBasedRecipeIndex + 1);
            if (oneBased < 1 || oneBased >= _recipeDialogs.Count + 1)
                return false;

            oneBasedSelector = oneBased;
            dialog = _recipeDialogs[zeroBasedRecipeIndex] + ConfirmText +
                     ConfirmCommands;
            return true;
        }

        public bool TryBuildSelectionDialogGbk(int zeroBasedRecipeIndex,
            out int oneBasedSelector, out ReadOnlyMemory<byte> dialog)
        {
            oneBasedSelector = 0;
            dialog = default;
            var oneBased = unchecked(zeroBasedRecipeIndex + 1);
            if (oneBased < 1 || oneBased >= _recipeDialogGbkBytes.Count + 1)
                return false;

            var result = new List<byte>(
                _recipeDialogGbkBytes[zeroBasedRecipeIndex].Length +
                GbkByteCount(ConfirmText) + GbkByteCount(ConfirmCommands));
            AppendRaw(result, _recipeDialogGbkBytes[zeroBasedRecipeIndex]);
            AppendGbk(result, ConfirmText);
            AppendGbk(result, ConfirmCommands);
            oneBasedSelector = oneBased;
            dialog = result.ToArray();
            return true;
        }

        public string GetRecipeDialog(int zeroBasedRecipeIndex)
        {
            if ((uint)zeroBasedRecipeIndex >= (uint)_recipeDialogs.Count)
                throw new ArgumentOutOfRangeException(nameof(zeroBasedRecipeIndex));
            return _recipeDialogs[zeroBasedRecipeIndex];
        }

        public ReadOnlyMemory<byte> GetRecipeDialogGbkBytes(
            int zeroBasedRecipeIndex)
        {
            if ((uint)zeroBasedRecipeIndex >=
                (uint)_recipeDialogGbkBytes.Count)
                throw new ArgumentOutOfRangeException(
                    nameof(zeroBasedRecipeIndex));
            return _recipeDialogGbkBytes[zeroBasedRecipeIndex];
        }

        private string BuildFoundryListDialog()
        {
            if (!SourceLoaded) return UnavailableDialog;
            if (_recipes.Count == 0) return EmptyDialog;

            var result = new StringBuilder(ListPrefix);
            for (var index = 0; index < _recipes.Count; index++)
            {
                var name = _recipes[index].ItemName;
                result.Append('<').Append(name).Append("/@DiaPeif_")
                    .Append(index).Append('>');
                if ((index + 1) % 4 == 0)
                    result.Append("\\\r\n");
                else
                    result.Append(' ', Math.Max(0,
                        NameMaximumGbkBytes - GbkByteCount(name)));
            }
            return result.ToString();
        }

        private static string BuildRecipeDialog(Recipe recipe)
        {
            var diamondText = recipe.DiamondCost.ToString();
            var result = new StringBuilder()
                .Append("锻造<").Append(recipe.ItemName)
                .Append(">需要以下的物品：\\ \\金刚石..........<")
                .Append(diamondText).Append("/C=RED>")
                .Append(' ', Math.Max(0, 5 - diamondText.Length));

            for (var index = 0; index < recipe.Materials.Count; index++)
            {
                var material = recipe.Materials[index];
                if (string.IsNullOrEmpty(material.ItemName)) break;

                var countText = material.Count.ToString();
                result.Append(material.ItemName)
                    .Append('.', Math.Max(0,
                        16 - GbkByteCount(material.ItemName)))
                    .Append(countText)
                    .Append(' ', Math.Max(0, 5 - countText.Length));
                if ((index + 1) % 3 == 2) result.Append('\\');
            }
            result.Append('\\');
            return result.ToString();
        }

        private byte[] BuildFoundryListDialogGbk()
        {
            if (!SourceLoaded) return EncodeGbk(UnavailableDialog);
            if (_recipes.Count == 0) return EncodeGbk(EmptyDialog);

            var result = new List<byte>();
            AppendGbk(result, ListPrefix);
            for (var index = 0; index < _recipes.Count; index++)
            {
                var name = _recipes[index].ItemNameGbkBytes;
                AppendGbk(result, "<");
                AppendRaw(result, name.Span);
                AppendGbk(result, "/@DiaPeif_");
                AppendGbk(result, index.ToString());
                AppendGbk(result, ">");
                if ((index + 1) % 4 == 0)
                    AppendGbk(result, "\\\r\n");
                else
                    AppendRepeated(result, (byte)' ', Math.Max(0,
                        NameMaximumGbkBytes - name.Length));
            }
            return result.ToArray();
        }

        private static byte[] BuildRecipeDialogGbk(Recipe recipe)
        {
            var diamondText = recipe.DiamondCost.ToString();
            var result = new List<byte>();
            AppendGbk(result, "锻造<");
            AppendRaw(result, recipe.ItemNameGbkBytes.Span);
            AppendGbk(result, ">需要以下的物品：\\ \\金刚石..........<");
            AppendGbk(result, diamondText);
            AppendGbk(result, "/C=RED>");
            AppendRepeated(result, (byte)' ',
                Math.Max(0, 5 - diamondText.Length));

            for (var index = 0; index < recipe.Materials.Count; index++)
            {
                var material = recipe.Materials[index];
                if (material.ItemNameGbkBytes.IsEmpty) break;

                var countText = material.Count.ToString();
                AppendRaw(result, material.ItemNameGbkBytes.Span);
                AppendRepeated(result, (byte)'.', Math.Max(0,
                    16 - material.ItemNameGbkBytes.Length));
                AppendGbk(result, countText);
                AppendRepeated(result, (byte)' ',
                    Math.Max(0, 5 - countText.Length));
                if ((index + 1) % 3 == 2) AppendGbk(result, "\\");
            }
            AppendGbk(result, "\\");
            return result.ToArray();
        }

        private static bool TryParseRecipe(string line, ref string carriedName,
            ref string carriedNumber, out Recipe recipe)
        {
            recipe = null;
            if (string.IsNullOrEmpty(line)) return false;

            var equals = line.IndexOf('=');
            if (equals <= 0 || equals == line.Length - 1) return false;
            var itemNameSource = line.Substring(0, equals);
            var descriptor = line.Substring(equals + 1);
            if (itemNameSource.Length == 0 || descriptor.Length == 0)
                return false;

            var diamondStart = descriptor.IndexOf("金刚石",
                StringComparison.Ordinal);
            if (diamondStart < 0) return false;
            descriptor = descriptor.Substring(diamondStart);

            ushort diamondCost = 0;
            ushort successRate = 0;
            var materials = new List<Material>(MaximumMaterials);
            while (descriptor.Length != 0)
            {
                var slash = descriptor.IndexOf('/');
                var token = slash < 0 ? descriptor : descriptor.Substring(0, slash);
                descriptor = slash < 0 ? string.Empty : descriptor.Substring(slash + 1);

                var colon = token.IndexOf(':');
                if (colon >= 0)
                {
                    carriedName = token.Substring(0, colon);
                    carriedNumber = DecodeGbk(TruncateGbkBytes(
                        token.Substring(colon + 1), 10));
                }
                var name = carriedName;
                var amount = HUtil32.Str_ToInt(carriedNumber, -1);
                if (amount <= 0) continue;

                if (string.Equals(name, "金刚石", StringComparison.Ordinal))
                {
                    diamondCost = unchecked((ushort)amount);
                }
                else if (string.Equals(name, "OK", StringComparison.Ordinal))
                {
                    successRate = unchecked((ushort)amount);
                }
                else
                {
                    var rawName = TruncateGbkBytes(name,
                        NameMaximumGbkBytes);
                    materials.Add(new Material(
                        TruncateGbkText(name, NameMaximumGbkBytes), rawName,
                        unchecked((byte)amount)));
                    if (materials.Count >= MaximumMaterials) break;
                }
            }

            if (successRate == 0) return false;
            var rawItemName = TruncateGbkBytes(itemNameSource,
                NameMaximumGbkBytes);
            recipe = new Recipe(
                TruncateGbkText(itemNameSource, NameMaximumGbkBytes),
                rawItemName,
                diamondCost, successRate, materials);
            return true;
        }

        private static int GbkByteCount(string value)
        {
            return HUtil32.GbkEncoding.GetByteCount(value ?? string.Empty);
        }

        private static byte[] TruncateGbkBytes(string value,
            int maximumBytes)
        {
            if (string.IsNullOrEmpty(value) || maximumBytes <= 0)
                return Array.Empty<byte>();

            var encoded = EncodeGbk(value);
            if (encoded.Length <= maximumBytes) return encoded;
            return encoded.AsSpan(0, maximumBytes).ToArray();
        }

        private static string TruncateGbkText(string value, int maximumBytes)
        {
            if (string.IsNullOrEmpty(value) || maximumBytes <= 0)
                return string.Empty;

            var result = new StringBuilder(value.Length);
            var byteCount = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                var text = rune.ToString();
                var runeBytes = HUtil32.GbkEncoding.GetByteCount(text);
                if (byteCount + runeBytes > maximumBytes) break;
                result.Append(text);
                byteCount += runeBytes;
            }
            return result.ToString();
        }

        private static byte[] EncodeGbk(string value)
        {
            return HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
        }

        private static string DecodeGbk(ReadOnlySpan<byte> value)
        {
            return HUtil32.GbkEncoding.GetString(value);
        }

        private static void AppendGbk(List<byte> target, string value)
        {
            target.AddRange(EncodeGbk(value));
        }

        private static void AppendRaw(List<byte> target,
            ReadOnlySpan<byte> value)
        {
            for (var index = 0; index < value.Length; index++)
                target.Add(value[index]);
        }

        private static void AppendRepeated(List<byte> target, byte value,
            int count)
        {
            for (var index = 0; index < count; index++)
                target.Add(value);
        }

        public sealed class Recipe
        {
            private readonly byte[] _itemNameGbkBytes;

            internal Recipe(string itemName, byte[] itemNameGbkBytes,
                ushort diamondCost, ushort successRate,
                IList<Material> materials)
            {
                _itemNameGbkBytes = itemNameGbkBytes.ToArray();
                ItemName = itemName;
                DiamondCost = diamondCost;
                SuccessRate = successRate;
                Materials = new ReadOnlyCollection<Material>(materials);
            }

            public string ItemName { get; }
            public ReadOnlyMemory<byte> ItemNameGbkBytes =>
                _itemNameGbkBytes;
            public ushort DiamondCost { get; }
            public ushort SuccessRate { get; }
            public IReadOnlyList<Material> Materials { get; }
        }

        public sealed class Material
        {
            private readonly byte[] _itemNameGbkBytes;

            internal Material(string itemName, byte[] itemNameGbkBytes,
                byte count)
            {
                _itemNameGbkBytes = itemNameGbkBytes.ToArray();
                ItemName = itemName;
                Count = count;
            }

            public string ItemName { get; }
            public ReadOnlyMemory<byte> ItemNameGbkBytes =>
                _itemNameGbkBytes;
            public byte Count { get; }
        }
    }
}
