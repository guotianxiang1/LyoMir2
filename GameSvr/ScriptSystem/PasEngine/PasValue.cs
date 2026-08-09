using System.Text;

namespace GameSvr.PasEngine
{
    public enum PasValueType
    {
        Integer,
        String,
        Boolean,
        Double,
        Array,
        Object,
        Nil
    }

    public class PasArray
    {
        public int LowBound { get; set; }
        public int HighBound { get; set; }
        public PasValue[] Elements { get; set; }
        public string ElementType { get; }

        public PasArray(int low, int high, string elementType = null)
        {
            LowBound = low;
            HighBound = high;
            ElementType = elementType ?? string.Empty;
            Elements = high >= low ? new PasValue[high - low + 1] : Array.Empty<PasValue>();
            InitializeElements(0);
        }

        public void Resize(int length)
        {
            if (length < 0) throw new PasRuntimeException("Array length cannot be negative");
            var oldLength = Elements.Length;
            var elements = Elements;
            Array.Resize(ref elements, length);
            Elements = elements;
            LowBound = 0;
            HighBound = length - 1;
            InitializeElements(oldLength);
        }

        private void InitializeElements(int start)
        {
            if (!ElementType.Equals("string", StringComparison.OrdinalIgnoreCase)) return;
            for (var index = start; index < Elements.Length; index++)
                Elements[index] = PasValue.FromString(string.Empty);
        }

        public PasValue this[int index]
        {
            get
            {
                if (index < LowBound || index > HighBound)
                    throw new PasRuntimeException($"Array index {index} out of bounds [{LowBound}..{HighBound}]");
                return Elements[index - LowBound];
            }
            set
            {
                if (index < LowBound || index > HighBound)
                    throw new PasRuntimeException($"Array index {index} out of bounds [{LowBound}..{HighBound}]");
                Elements[index - LowBound] = value;
            }
        }
    }

    public struct PasValue
    {
        public PasValueType Type { get; }
        public int IntVal { get; }
        public string StrVal { get; }
        public bool BoolVal { get; }
        public double DblVal { get; }
        public PasArray ArrVal { get; }
        public object ObjVal { get; }

        private PasValue(PasValueType type, int i = 0, string s = null, bool b = false,
            double d = 0, PasArray a = null, object o = null)
        {
            Type = type; IntVal = i; StrVal = s; BoolVal = b; DblVal = d; ArrVal = a; ObjVal = o;
        }

        public static PasValue FromInt(int v) => new PasValue(PasValueType.Integer, i: v);
        public static PasValue FromString(string v) => new PasValue(PasValueType.String, s: v ?? "");
        public static PasValue FromBool(bool v) => new PasValue(PasValueType.Boolean, b: v);
        public static PasValue FromDouble(double v) => new PasValue(PasValueType.Double, d: v);
        public static PasValue FromArray(PasArray v) => new PasValue(PasValueType.Array, a: v);
        public static PasValue FromObject(object v) => v == null ? Nil : new PasValue(PasValueType.Object, o: v);
        public static PasValue Nil => new PasValue(PasValueType.Nil);

        public int AsInt()
        {
            if (Type == PasValueType.Integer) return IntVal;
            if (Type == PasValueType.Double) return (int)DblVal;
            if (Type == PasValueType.String && int.TryParse(StrVal, out var r)) return r;
            if (Type == PasValueType.Boolean) return BoolVal ? 1 : 0;
            return 0;
        }

        public string AsString()
        {
            return Type switch
            {
                PasValueType.Integer => IntVal.ToString(),
                PasValueType.String => StrVal,
                PasValueType.Boolean => BoolVal ? "TRUE" : "FALSE",
                PasValueType.Double => DblVal.ToString(),
                PasValueType.Object => ObjVal?.ToString() ?? "",
                _ => ""
            };
        }

        public bool AsBool()
        {
            return Type switch
            {
                PasValueType.Boolean => BoolVal,
                PasValueType.Integer => IntVal != 0,
                PasValueType.String => StrVal != "" && StrVal != "0" && !StrVal.Equals("FALSE", StringComparison.OrdinalIgnoreCase),
                PasValueType.Object => ObjVal != null,
                _ => false
            };
        }

        public double AsDouble()
        {
            if (Type == PasValueType.Double) return DblVal;
            if (Type == PasValueType.Integer) return IntVal;
            if (Type == PasValueType.String && double.TryParse(StrVal, out var r)) return r;
            return 0;
        }

        public override string ToString() => AsString();

        public static PasValue operator +(PasValue a, PasValue b)
        {
            if (a.Type == PasValueType.String || b.Type == PasValueType.String)
                return FromString(a.AsString() + b.AsString());
            if (a.Type == PasValueType.Double || b.Type == PasValueType.Double)
                return FromDouble(a.AsDouble() + b.AsDouble());
            return FromInt(a.AsInt() + b.AsInt());
        }

        public static PasValue operator -(PasValue a, PasValue b)
        {
            if (a.Type == PasValueType.Double || b.Type == PasValueType.Double)
                return FromDouble(a.AsDouble() - b.AsDouble());
            return FromInt(a.AsInt() - b.AsInt());
        }

        public static PasValue operator *(PasValue a, PasValue b)
        {
            if (a.Type == PasValueType.Double || b.Type == PasValueType.Double)
                return FromDouble(a.AsDouble() * b.AsDouble());
            return FromInt(a.AsInt() * b.AsInt());
        }

        public static PasValue operator /(PasValue a, PasValue b)
        {
            if (a.Type == PasValueType.Double || b.Type == PasValueType.Double)
                return FromDouble(a.AsDouble() / b.AsDouble());
            var bi = b.AsInt();
            if (bi == 0) throw new PasRuntimeException("Division by zero");
            return FromInt(a.AsInt() / bi);
        }

        public static PasValue operator %(PasValue a, PasValue b)
        {
            return FromInt(a.AsInt() % b.AsInt());
        }

        // Comparison operators
        public static PasValue operator ==(PasValue a, PasValue b) => FromBool(a.Equals(b));
        public static PasValue operator !=(PasValue a, PasValue b) => FromBool(!a.Equals(b));
        public static PasValue operator <(PasValue a, PasValue b)
        {
            if (a.Type == PasValueType.String || b.Type == PasValueType.String)
                return FromBool(string.Compare(a.AsString(), b.AsString(), StringComparison.OrdinalIgnoreCase) < 0);
            if (a.Type == PasValueType.Double || b.Type == PasValueType.Double)
                return FromBool(a.AsDouble() < b.AsDouble());
            return FromBool(a.AsInt() < b.AsInt());
        }
        public static PasValue operator >(PasValue a, PasValue b)
        {
            if (a.Type == PasValueType.String || b.Type == PasValueType.String)
                return FromBool(string.Compare(a.AsString(), b.AsString(), StringComparison.OrdinalIgnoreCase) > 0);
            if (a.Type == PasValueType.Double || b.Type == PasValueType.Double)
                return FromBool(a.AsDouble() > b.AsDouble());
            return FromBool(a.AsInt() > b.AsInt());
        }
        public static PasValue operator <=(PasValue a, PasValue b) => FromBool((a == b).AsBool() || (a < b).AsBool());
        public static PasValue operator >=(PasValue a, PasValue b) => FromBool((a == b).AsBool() || (a > b).AsBool());

        public override bool Equals(object obj)
        {
            if (obj is PasValue other)
            {
                if (Type == PasValueType.Nil && other.Type == PasValueType.Nil) return true;
                if (Type == PasValueType.Integer && other.Type == PasValueType.Integer) return IntVal == other.IntVal;
                if (Type == PasValueType.String && other.Type == PasValueType.String) return string.Equals(StrVal, other.StrVal, StringComparison.OrdinalIgnoreCase);
                if (Type == PasValueType.Boolean && other.Type == PasValueType.Boolean) return BoolVal == other.BoolVal;
                if (Type == PasValueType.Double && other.Type == PasValueType.Double) return Math.Abs(DblVal - other.DblVal) < 1e-10;
                if (Type == PasValueType.Object && other.Type == PasValueType.Object) return ReferenceEquals(ObjVal, other.ObjVal);
                // Cross-type comparisons
                if (Type == PasValueType.Integer && other.Type == PasValueType.Double) return Math.Abs(IntVal - other.DblVal) < 1e-10;
                if (Type == PasValueType.Double && other.Type == PasValueType.Integer) return Math.Abs(DblVal - other.IntVal) < 1e-10;
                return AsString().Equals(other.AsString(), StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode() => Type switch
        {
            PasValueType.Integer => IntVal.GetHashCode(),
            PasValueType.String => StrVal?.GetHashCode() ?? 0,
            PasValueType.Boolean => BoolVal.GetHashCode(),
            PasValueType.Object => ObjVal == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ObjVal),
            _ => 0
        };
    }

    public class PasRuntimeException : Exception
    {
        public PasRuntimeException(string msg) : base(msg) { }
        public PasRuntimeException(string msg, Exception inner) : base(msg, inner) { }
    }
}
