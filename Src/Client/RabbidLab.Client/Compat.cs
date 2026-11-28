// .NET Framework 4.6.2, which Windows Server 2016 ships with, has no tuples of its own (they came with 4.7), so the ones
// the compiler needs are here: two to seven items, compared item by item. Public because WakeOnLan.cs, shared with the
// server, returns one from a public method.
using System.Collections.Generic;

namespace System
{
    internal static class TupleHash
    {
        public static int Of<T>(T item) => item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item);

        public static int Mix(int a, int b) => unchecked((a * 397) ^ b);

        public static bool Same<T>(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
    }

    public struct ValueTuple<T1, T2> : IEquatable<ValueTuple<T1, T2>>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public bool Equals(ValueTuple<T1, T2> o) => TupleHash.Same(Item1, o.Item1) && TupleHash.Same(Item2, o.Item2);

        public override bool Equals(object? obj) => obj is ValueTuple<T1, T2> o && Equals(o);

        public override int GetHashCode() => TupleHash.Mix(TupleHash.Of(Item1), TupleHash.Of(Item2));
    }

    public struct ValueTuple<T1, T2, T3> : IEquatable<ValueTuple<T1, T2, T3>>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }

        public bool Equals(ValueTuple<T1, T2, T3> o) => TupleHash.Same(Item1, o.Item1) && TupleHash.Same(Item2, o.Item2) && TupleHash.Same(Item3, o.Item3);

        public override bool Equals(object? obj) => obj is ValueTuple<T1, T2, T3> o && Equals(o);

        public override int GetHashCode() => TupleHash.Mix(TupleHash.Mix(TupleHash.Of(Item1), TupleHash.Of(Item2)), TupleHash.Of(Item3));
    }

    public struct ValueTuple<T1, T2, T3, T4> : IEquatable<ValueTuple<T1, T2, T3, T4>>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }

        public bool Equals(ValueTuple<T1, T2, T3, T4> o) => TupleHash.Same(Item1, o.Item1) && TupleHash.Same(Item2, o.Item2) && TupleHash.Same(Item3, o.Item3) && TupleHash.Same(Item4, o.Item4);

        public override bool Equals(object? obj) => obj is ValueTuple<T1, T2, T3, T4> o && Equals(o);

        public override int GetHashCode() => TupleHash.Mix(TupleHash.Mix(TupleHash.Mix(TupleHash.Of(Item1), TupleHash.Of(Item2)), TupleHash.Of(Item3)), TupleHash.Of(Item4));
    }

    public struct ValueTuple<T1, T2, T3, T4, T5> : IEquatable<ValueTuple<T1, T2, T3, T4, T5>>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
        }

        public bool Equals(ValueTuple<T1, T2, T3, T4, T5> o) => TupleHash.Same(Item1, o.Item1) && TupleHash.Same(Item2, o.Item2) && TupleHash.Same(Item3, o.Item3) && TupleHash.Same(Item4, o.Item4)
            && TupleHash.Same(Item5, o.Item5);

        public override bool Equals(object? obj) => obj is ValueTuple<T1, T2, T3, T4, T5> o && Equals(o);

        public override int GetHashCode() => TupleHash.Mix(new ValueTuple<T1, T2, T3, T4>(Item1, Item2, Item3, Item4).GetHashCode(), TupleHash.Of(Item5));
    }

    public struct ValueTuple<T1, T2, T3, T4, T5, T6> : IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6>>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
        }

        public bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6> o) => TupleHash.Same(Item1, o.Item1) && TupleHash.Same(Item2, o.Item2) && TupleHash.Same(Item3, o.Item3) && TupleHash.Same(Item4, o.Item4)
            && TupleHash.Same(Item5, o.Item5) && TupleHash.Same(Item6, o.Item6);

        public override bool Equals(object? obj) => obj is ValueTuple<T1, T2, T3, T4, T5, T6> o && Equals(o);

        public override int GetHashCode() => TupleHash.Mix(new ValueTuple<T1, T2, T3, T4, T5>(Item1, Item2, Item3, Item4, Item5).GetHashCode(), TupleHash.Of(Item6));
    }

    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7> : IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
        }

        public bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6, T7> o) => TupleHash.Same(Item1, o.Item1) && TupleHash.Same(Item2, o.Item2) && TupleHash.Same(Item3, o.Item3) && TupleHash.Same(Item4, o.Item4)
            && TupleHash.Same(Item5, o.Item5) && TupleHash.Same(Item6, o.Item6) && TupleHash.Same(Item7, o.Item7);

        public override bool Equals(object? obj) => obj is ValueTuple<T1, T2, T3, T4, T5, T6, T7> o && Equals(o);

        public override int GetHashCode() => TupleHash.Mix(new ValueTuple<T1, T2, T3, T4, T5, T6>(Item1, Item2, Item3, Item4, Item5, Item6).GetHashCode(), TupleHash.Of(Item7));
    }
}

namespace System.Runtime.CompilerServices
{
    /// <summary>The names the compiler gives a tuple's items, kept on what returns one.</summary>
    [AttributeUsage(AttributeTargets.All)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        public TupleElementNamesAttribute(string?[] transformNames)
        {
            TransformNames = transformNames;
        }

        public string?[] TransformNames { get; }
    }
}
