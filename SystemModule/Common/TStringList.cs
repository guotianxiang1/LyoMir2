using System;
using System.IO;
using System.Text;
using SystemModule;

namespace SystemModule.Common
{
    public sealed class StringList : IDisposable
    {
        private readonly int m_Capacity;
        private string[] _mStrings;
        private int _mSize;

        
        
        
        public int Count
        {
            get
            {
                return _mSize;
            }
        }

        
        
        
        public int Capacity
        {
            get
            {
                return m_Capacity;
            }
            set
            {
                if (_mStrings == null)
                {
                    return;
                }

                if (value == _mStrings.Length) return;
                if (value < this._mSize)
                {
                    throw new ArgumentOutOfRangeException();
                }

                if (value > 0)
                {
                    var objArray1 = new string[value];
                    if (this._mSize > 0)
                    {
                        Array.Copy(this._mStrings, 0, objArray1, 0, this._mSize);
                    }
                    this._mStrings = objArray1;
                }
                else
                {
                    this._mStrings = new string[0x10];
                }
            }
        }

        public string Text
        {
            get
            {
                return this.ToString();
            }
        }




        public StringList()
            : this(100)
        {
        }

        
        
        
        public StringList(int capacity)
        {
            m_Capacity = capacity;

            _mStrings = new string[capacity];
            _mSize = 0;
        }

        
        
        
        
        public string this[int index]
        {
            get
            {
                if ((index < 0) || (index >= _mSize))
                {
                    throw new ArgumentOutOfRangeException();
                }
                return this._mStrings[index];
            }
            set
            {
                if ((index < 0) || (index >= _mSize))
                {
                    throw new ArgumentOutOfRangeException();
                }
                this._mStrings[index] = value;
            }
        }

        
        
        
        private void EnsureCapacity(int min)
        {
            if (this._mStrings.Length >= min) return;
            var num1 = (this._mStrings.Length == 0) ? 0x10 : (this._mStrings.Length * 2);
            if (num1 < min)
            {
                num1 = min;
            }
            this.Capacity = num1;
        }

        public int Add(string value)
        {
            if (this.Count == _mStrings.Length)
            {
                EnsureCapacity(this.Count + 1);
            }

            _mStrings[this.Count] = value;
            _mSize++;

            return _mSize;
        }

        
        
        
        public int AppendText(string value)
        {
            if (this.Count == _mStrings.Length)
            {
                EnsureCapacity(this.Count + 1);
            }

            _mStrings[this.Count] = value;
            _mSize++;

            return _mSize;
        }

        
        
        
        
        public int InsertText(int index, string value)
        {
            if (index < 0)
            {
                index = 0;
            }

            if (this.Count == _mStrings.Length)
            {
                EnsureCapacity(this.Count + 1);
            }

            if (index < this.Count)
            {
                Array.Copy(this._mStrings, index, this._mStrings, index + 1, this._mSize - index);
            }

            _mStrings[index] = value;
            _mSize++;

            return _mSize;
        }

        
        
        
        public int IndexOf(string value)
        {
            return Array.IndexOf(this._mStrings, value, 0, this._mSize);
        }

        
        
        
        
        public void RemoveAt(int index)
        {
            if ((index < 0) || (index >= this._mSize))
            {
                throw new ArgumentOutOfRangeException();
            }
            this._mSize--;
            if (index < this._mSize)
            {
                Array.Copy(this._mStrings, index + 1, this._mStrings, index, this._mSize - index);
            }
            this._mStrings[this._mSize] = null;
        }

        
        
        
        public override string ToString()
        {
            var s = new StringBuilder(this.Count);

            for (var i = 0; i < this.Count; i++)
            {
                s.Append(_mStrings[i] + "\r\n");
            }

            return s.ToString();
        }

        
        
        
        
        
        
        public string ToString(int startIndex, int count)
        {
            if (startIndex < 0)
            {
                startIndex = 0;
            }
            else if (startIndex >= this.Count)
            {
                return "";
            }

            if (count <= 0)
            {
                return "";
            }

            if (count + startIndex > this.Count)
            {
                count = this.Count - startIndex;
            }

            var s = new StringBuilder(this.Count);

            for (var i = startIndex; i < count; i++)
            {
                s.Append(_mStrings[i] + "\r\n");
            }

            return s.ToString();
        }

        
        
        
        public void Clear()
        {
            this._mSize = 0;
        }

        
        
        
        
        
        public void SaveToFile(string fileName, Encoding encoding)
        {
            var sw2 = new StreamWriter(fileName, false, encoding);
            for (int i = 0; i < this.Count; i++)
            {
                sw2.Write(_mStrings[i] + "\r\n");
            }

            sw2.Close();
        }

        public void SaveToFile(string fileName)
        {
            var sw2 = new StreamWriter(fileName, false, HUtil32.GbkEncoding);
            for (int i = 0; i < this.Count; i++)
            {
                sw2.Write(_mStrings[i] + "\r\n");
            }

            sw2.Close();
        }

        
        
        
        
        public void LoadFromFile(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return;
            }
            this.Clear();
            var sr2 = new StreamReader(fileName, HUtil32.GbkEncoding);
            while (sr2.Peek() >= 0)
            {
                this.AppendText(sr2.ReadLine());
            }
            sr2.Close();
        }

        public void LoadFromFile(string fileName, bool isAdd)
        {
            this.Clear();
            var sr2 = new StreamReader(fileName, HUtil32.GbkEncoding);
            while (sr2.Peek() >= 0)
            {
                this.AppendText(sr2.ReadLine());
            }
            sr2.Close();
        }

        public void LoadFromFile(string fileName, Encoding encoding)
        {
            this.Clear();

            StreamReader sr2 = new StreamReader(fileName, encoding);

            while (sr2.Peek() >= 0)
            {
                this.AppendText(sr2.ReadLine());
            }

            sr2.Close();
        }

        public void __Lock()
        {
        }

        public void UnLock()
        {
        }

        public void Dispose()
        {
        }
    }
}