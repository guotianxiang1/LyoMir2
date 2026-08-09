using System.Xml;
using System.Xml.Linq;
using SystemModule;

namespace GameSvr
{
    public sealed class NativeActivityPointManager
    {
        private sealed class ThresholdRule
        {
            public int Minimum { get; init; }
            public int Value { get; init; }
        }

        private sealed class MagicRule
        {
            public int MagicId { get; init; }
            public int Value { get; init; }
        }

        private sealed class JobRules
        {
            public List<ThresholdRule> LuckRules { get; } = new();
            public List<MagicRule> MagicRules { get; } = new();
            public List<ThresholdRule> PropertyRules { get; } = new();
        }

        private readonly Dictionary<int, JobRules> _jobs;

        private NativeActivityPointManager(Dictionary<int, JobRules> jobs)
        {
            _jobs = jobs;
        }

        public static bool TryLoad(string fileName, out NativeActivityPointManager manager,
            out string error)
        {
            manager = null;
            error = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
                {
                    error = $"configuration file does not exist: {fileName}";
                    return false;
                }

                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using var reader = XmlReader.Create(fileName, settings);
                var document = XDocument.Load(reader, LoadOptions.None);
                var jobsElement = document.Root?.Element("Jobs");
                if (jobsElement == null)
                {
                    error = "Jobs element is missing";
                    return false;
                }

                var jobs = new Dictionary<int, JobRules>();
                foreach (var jobElement in jobsElement.Elements("Job"))
                {
                    var jobId = ReadRequiredInt(jobElement, "Id");
                    if (jobId < 0 || !jobs.TryAdd(jobId, ParseJob(jobElement)))
                    {
                        error = $"invalid or duplicate Job Id: {jobId}";
                        return false;
                    }
                }

                if (jobs.Count == 0)
                {
                    error = "no Job rules were loaded";
                    return false;
                }

                manager = new NativeActivityPointManager(jobs);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or XmlException or FormatException or OverflowException)
            {
                error = ex.Message;
                return false;
            }
        }

        public int Calculate(TPlayObject player)
        {
            if (player == null) return 0;

            var primaryProperty = player.m_btJob switch
            {
                0 => HUtil32.HiWord(player.m_WAbil.DC),
                1 => HUtil32.HiWord(player.m_WAbil.MC),
                2 => HUtil32.HiWord(player.m_WAbil.SC),
                _ => 0
            };

            return Calculate(player.m_btJob, player.m_nLuck, primaryProperty,
                magicId => player.GetMagicInfo(magicId) != null);
        }

        public int Calculate(int jobId, int luck, int primaryProperty,
            Func<int, bool> hasMagic)
        {
            if (!_jobs.TryGetValue(jobId, out var rules)) return 0;

            var total = FindLastThresholdValue(rules.LuckRules, luck);
            foreach (var rule in rules.MagicRules)
            {
                if (hasMagic?.Invoke(rule.MagicId) == true)
                    total = unchecked(total + rule.Value);
            }
            return unchecked(total + FindLastThresholdValue(rules.PropertyRules,
                primaryProperty));
        }

        private static JobRules ParseJob(XElement jobElement)
        {
            var result = new JobRules();
            foreach (var element in jobElement.Element("Lucks")?.Elements("Luck")
                ?? Enumerable.Empty<XElement>())
            {
                result.LuckRules.Add(new ThresholdRule
                {
                    Minimum = ReadRequiredInt(element, "LuckValue"),
                    Value = ReadRequiredInt(element, "Value")
                });
            }

            foreach (var element in jobElement.Element("Magics")?.Elements("Magic")
                ?? Enumerable.Empty<XElement>())
            {
                result.MagicRules.Add(new MagicRule
                {
                    MagicId = ReadRequiredInt(element, "Id"),
                    Value = ReadRequiredInt(element, "Value")
                });
            }

            foreach (var element in jobElement.Element("Properties")?.Elements("prop")
                ?? Enumerable.Empty<XElement>())
            {
                result.PropertyRules.Add(new ThresholdRule
                {
                    Minimum = ReadRequiredInt(element, "Min"),
                    Value = ReadRequiredInt(element, "value", "Value")
                });
            }
            return result;
        }

        private static int FindLastThresholdValue(IReadOnlyList<ThresholdRule> rules,
            int actual)
        {
            for (var index = rules.Count - 1; index >= 0; index--)
            {
                if (actual >= rules[index].Minimum) return rules[index].Value;
            }
            return 0;
        }

        private static int ReadRequiredInt(XElement element, params string[] names)
        {
            foreach (var name in names)
            {
                var attribute = element.Attribute(name);
                if (attribute != null) return int.Parse(attribute.Value,
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            throw new FormatException($"{element.Name} is missing attribute {names[0]}");
        }
    }
}
