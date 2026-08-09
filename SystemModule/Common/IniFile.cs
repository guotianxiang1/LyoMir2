using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SystemModule;

namespace SystemModule.Common
{
    public abstract class IniFile
    {
        private string fileName;
        private Dictionary<string, Dictionary<string, string>> iniCahce =
            new(StringComparer.OrdinalIgnoreCase);

        private bool largeCommentFlag = false;

        public int ConfigCount => iniCahce.Count;

        protected IniFile(string fileName)
        {
            this.FileName = fileName;
        }

        protected bool ContainSectionName(string secName)
        {
            return this.GetAllSectionName().Contains(secName);
        }

        protected ICollection<string> GetAllSectionName()
        {
            return this.iniCahce.Keys;
        }

        protected bool GetBool(string section, string key, bool defValue)
        {
            if (this.iniCahce.ContainsKey(section))
            {
                Dictionary<string, string> hash = this.iniCahce[section];
                if (hash.ContainsKey(key))
                {
                    string str = hash[key].ToUpper();
                    if (string.IsNullOrEmpty(str))
                    {
                        return defValue;
                    }
                    return ((((str == "是") || (str == "YES")) || (str == "1")) || (str == "TRUE"));
                }
            }
            return defValue;
        }

        protected bool ReadBool(string section, string key, bool defValue)
        {
            return GetBool(section, key, defValue);
        }

        protected double ReadFloat(string section, string key, double defValue)
        {
            if (this.iniCahce.ContainsKey(section))
            {
                Dictionary<string, string> hash = this.iniCahce[section];
                if (hash.ContainsKey(key))
                {
                    string str = hash[key].ToUpper();
                    if (string.IsNullOrEmpty(str))
                    {
                        return defValue;
                    }
                    return double.Parse(str);
                }
            }
            return defValue;
        }

        protected DateTime ReadDateTime(string section, string key, DateTime defValue)
        {
            if (this.iniCahce.ContainsKey(section))
            {
                Dictionary<string, string> hash = this.iniCahce[section];
                if (hash.ContainsKey(key))
                {
                    string str = hash[key].ToUpper();
                    if (string.IsNullOrEmpty(str))
                    {
                        return defValue;
                    }
                    return DateTime.Parse(str);
                }
            }
            return defValue;
        }

        public int ReadInteger(string section, string key, int defValue)
        {
            return GetInt(section, key, defValue);
        }

        protected T Read<T>(string section, string key, object defValue)
        {
            if (this.iniCahce.ContainsKey(section))
            {
                Dictionary<string, string> hash = this.iniCahce[section];
                if (hash.ContainsKey(key))
                {
                    string str = hash[key];
                    if (string.IsNullOrEmpty(str))
                    {
                        return (T)Convert.ChangeType(defValue, typeof(T));
                    }
                    return (T)Convert.ChangeType(str, typeof(T));
                }
            }
            return (T)Convert.ChangeType(defValue, typeof(T));
        }

        protected int ReadInteger(string section, string key, byte defValue)
        {
            return GetInt(section, key, defValue);
        }

        protected int GetInt(string section, string key, int defValue)
        {
            if (this.iniCahce.ContainsKey(section))
            {
                Dictionary<string, string> hash = this.iniCahce[section];
                int ret;
                if (hash.ContainsKey(key))
                {
                    string str = hash[key];
                    if (string.IsNullOrEmpty(str))
                    {
                        return defValue;
                    }
                    if (int.TryParse(str, out ret))
                    {
                        return ret;
                    }
                }
            }
            return defValue;
        }

        private static string GetSecString(string str)
        {
            int len = str.Length;
            if (str[0] == '[')
            {
                int pos = str.IndexOf(']');
                if (pos > 0)
                {
                    return str.Substring(1, pos - 1).Trim();
                }
            }
            return "";
        }

        protected ICollection<string> GetSectionItemName(string sectName)
        {
            if (this.iniCahce.ContainsKey(sectName))
            {
                Dictionary<string, string> tbl = this.iniCahce[sectName];
                return tbl.Keys;
            }
            return new List<string>();
        }

        protected ICollection<string> GetAllValues(string sectName)
        {
            if (this.iniCahce.ContainsKey(sectName))
            {
                Dictionary<string, string> tbl = this.iniCahce[sectName];
                return tbl.Values;
            }
            return new List<String>();
        }

        public string ReadString(string section, string key, string defval)
        {
            var result = GetString(section, key);
            return string.IsNullOrEmpty(result) ? defval : result;
        }

        private string GetString(string section, string key)
        {
            if (this.iniCahce.ContainsKey(section))
            {
                var hash = this.iniCahce[section];
                if (hash.ContainsKey(key))
                {
                    return hash[key];
                }
            }
            return "";
        }

        protected void Load()
        {
            if (!File.Exists(this.FileName))
            {
                var directory = Path.GetDirectoryName(this.FileName);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.Create(this.FileName).Close();
                return;
            }
            StreamReader rd = new StreamReader(File.Open(this.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), HUtil32.GbkEncoding);
            bool isCurSecComment = false;
            Dictionary<string, string> curSec = null;
            string str = "";
        Label_02A6:
            str = this.ReadLine(rd);
            if (str != null)
            {
                str = str.Trim();
                if (str != "")
                {
                    if (str.StartsWith("/*"))
                    {
                        this.largeCommentFlag = true;
                    }
                    else if (this.largeCommentFlag)
                    {
                        if (str.StartsWith("*/"))
                        {
                            this.largeCommentFlag = false;
                        }
                    }
                    else if ((str.Length < 2) || ((str[0] != ';') || (str[1] != ';')))
                    {
                        string sec = GetSecString(str);
                        if (sec != "")
                        {
                            if (sec.Length >= 2)
                            {
                                if ((sec[0] == ';') && (sec[1] == ';'))
                                {
                                    isCurSecComment = true;
                                    goto Label_02A6;
                                }
                                isCurSecComment = false;
                            }
                            if (this.iniCahce.ContainsKey(sec))
                            {
                                curSec = this.iniCahce[sec];
                                goto Label_02A6;
                            }
                            curSec = new Dictionary<string, string>(
                                StringComparer.OrdinalIgnoreCase);
                            this.iniCahce.Add(sec, curSec);
                        }
                        else if (!isCurSecComment)
                        {
                            int index = str.IndexOf(";;", StringComparison.Ordinal);
                            if (index >= 0)
                            {
                                str = str.Substring(0, index).Trim();
                            }
                            if (curSec == null)
                            {
                                curSec = new Dictionary<string, string>(
                                    StringComparer.OrdinalIgnoreCase);
                                this.iniCahce.Add("", curSec);
                            }
                            string[] substr = this.SplitKeyVal(str);
                            if ((substr != null) && (substr.Length >= 2))
                            {
                                substr[0] = substr[0].Trim();
                                substr[1] = substr[1].Trim();
                                curSec[substr[0]] = substr[1];
                            }
                        }
                    }
                }
                goto Label_02A6;
            }
            rd.Close();
            if (ConfigCount <= 0)
            {
                throw new Exception($"配置文件[{FileName}]不存在或配置文件内容为空。");
            }
        }

        private string ReadLine(StreamReader rd)
        {
            string str = "";
            string s = rd.ReadLine();
            if (s == null)
            {
                return null;
            }
            s = s.Trim();
            if (s != "")
            {
                str = str + s;
            }
            return str;
        }

        protected void Save()
        {
            var content = new StringBuilder();
            foreach (KeyValuePair<string, Dictionary<string, string>> pair in this.iniCahce)
            {
                content.Append('[').Append(pair.Key).Append("]\r\n");
                Dictionary<string, string> tbl = pair.Value;
                foreach (KeyValuePair<string, string> pair2 in tbl)
                {
                    content.Append(pair2.Key).Append('=').Append(pair2.Value).Append("\r\n");
                }
                content.Append("\r\n");
            }

            AtomicFile.WriteAllText(this.FileName, content.ToString(), HUtil32.GbkEncoding);
        }

        private string[] SplitKeyVal(string str)
        {
            int pos = -1;
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '=')
                {
                    pos = i;
                    break;
                }
            }
            if ((pos > 0) && (pos < str.Length))
            {
                return new string[] { str.Substring(0, pos), str.Substring(pos + 1) };
            }
            return null;
        }

        protected void WriteInt(string section, string key, int val)
        {
            this.WriteString(section, key, val.ToString());
        }

        protected void WriteBool(string section, string key, bool val)
        {
            SetCachedBool(section, key, val);
            Save();
        }

        protected void WriteInteger(string section, string key, object val)
        {
            if (val == null)
            {
                return;
            }
            SetCachedInteger(section, key, val);
            Save();
        }

        protected void WriteDateTime(string section, string key, DateTime val)
        {
            SetCachedDateTime(section, key, val);
            Save();
        }

        protected void WriteString(string section, string key, object str)
        {
            if (str == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(key)) return;
            SetCachedString(section, key, str.ToString());
            Save();
        }

        protected void SetCachedBool(string section, string key, bool value)
        {
            SetCachedString(section, key, value.ToString());
        }

        protected void SetCachedInteger(string section, string key, object value)
        {
            if (value == null)
            {
                return;
            }
            SetCachedString(section, key, value.ToString());
        }

        protected void SetCachedDateTime(string section, string key, DateTime value)
        {
            SetCachedString(section, key, value.ToString("O"));
        }

        protected void SetCachedString(string section, string key, string value)
        {
            if (!this.iniCahce.ContainsKey(section))
            {
                this.iniCahce.Add(section, new Dictionary<string, string>());
            }
            if (!string.IsNullOrEmpty(key))
            {
                Dictionary<string, string> secTbl = this.iniCahce[section];
                secTbl[key] = value ?? string.Empty;
            }
        }

        protected string FileName
        {
            get
            {
                return this.fileName;
            }
            set
            {
                this.fileName = value;
            }
        }
    }
}
