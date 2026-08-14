using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 衣服升级配置 XML loader — native sub_6A3A48 @0x6A3A48.
    /// Validates root node "衣服升级配置" and required child sections:
    /// Item / Items / NeedJewelry / NeedJewelrys (error strings @0x6A3DBC..).
    /// </summary>
    public sealed class NativeClothUpgradeConfig
    {
        public const string RootNodeName = "衣服升级配置";
        public const string ConfigRelativePath = @"Share\config\衣服升级配置.xml";

        private static readonly NativeClothUpgradeConfig _shared =
            new NativeClothUpgradeConfig();

        public static NativeClothUpgradeConfig Shared => _shared;

        private readonly List<NativeClothUpgradeRecipe> _recipes =
            new List<NativeClothUpgradeRecipe>();

        public IReadOnlyList<NativeClothUpgradeRecipe> Recipes => _recipes;

        public static string ResolveDefaultPath(string rootPath, string baseDir)
        {
            return Path.Combine(rootPath ?? string.Empty, baseDir ?? string.Empty,
                "config", "衣服升级配置.xml");
        }

        public bool TryGetByResult(string resultName, out NativeClothUpgradeRecipe recipe)
        {
            recipe = null;
            if (string.IsNullOrEmpty(resultName))
                return false;

            for (var i = 0; i < _recipes.Count; i++)
            {
                if (string.Equals(_recipes[i].ResultName, resultName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    recipe = _recipes[i];
                    return true;
                }
            }

            return false;
        }

        public bool Reload(string fileName, out string error)
        {
            error = string.Empty;
            _recipes.Clear();

            if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
            {
                error = "未配置根节点";
                return false;
            }

            XmlDocument doc;
            try
            {
                doc = new XmlDocument();
                doc.Load(fileName);
            }
            catch (Exception ex)
            {
                error = "衣服升级配置 XML parse: " + ex.Message;
                M2Share.ErrorMessage(error);
                return false;
            }

            var root = doc.DocumentElement;
            if (root == null
                || !string.Equals(root.Name, RootNodeName, StringComparison.Ordinal))
            {
                error = "未配置根节点";
                M2Share.ErrorMessage(error);
                return false;
            }

            var itemsNode = root.SelectSingleNode("Items") ?? root.SelectSingleNode("Item");
            if (itemsNode == null)
            {
                error = root.SelectSingleNode("Items") == null
                    ? "未配置Items节点"
                    : "未配置Item节点";
                M2Share.ErrorMessage(error);
                return false;
            }

            var needJewelry = root.SelectSingleNode("NeedJewelrys")
                ?? root.SelectSingleNode("NeedJewelry");
            if (needJewelry == null)
            {
                error = root.SelectSingleNode("NeedJewelrys") == null
                    ? "未配置NeedJewelrys节点"
                    : "未配置NeedJewelry节点";
                M2Share.ErrorMessage(error);
                return false;
            }

            foreach (XmlNode itemNode in itemsNode.ChildNodes)
            {
                if (itemNode.NodeType != XmlNodeType.Element)
                    continue;

                var recipe = ParseRecipe(itemNode);
                if (recipe != null)
                    _recipes.Add(recipe);
            }

            return true;
        }

        private static NativeClothUpgradeRecipe ParseRecipe(XmlNode itemNode)
        {
            var result = itemNode.Attributes?["Result"]?.Value
                ?? itemNode.Attributes?["Name"]?.Value
                ?? itemNode.InnerText?.Trim();
            if (string.IsNullOrEmpty(result))
                return null;

            var recipe = new NativeClothUpgradeRecipe { ResultName = result };
            foreach (XmlNode child in itemNode.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;
                var text = child.InnerText?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;
                recipe.Materials.Add(new NativeClothUpgradeMaterial
                {
                    Name = text,
                    Count = ParseCount(child)
                });
            }

            return recipe;
        }

        private static int ParseCount(XmlNode node)
        {
            var countText = node.Attributes?["Count"]?.Value
                ?? node.Attributes?["Num"]?.Value;
            return int.TryParse(countText, out var count) && count > 0 ? count : 1;
        }
    }

    public sealed class NativeClothUpgradeRecipe
    {
        public string ResultName { get; init; }
        public List<NativeClothUpgradeMaterial> Materials { get; } =
            new List<NativeClothUpgradeMaterial>();
    }

    public sealed class NativeClothUpgradeMaterial
    {
        public string Name { get; init; }
        public int Count { get; init; }
    }
}
