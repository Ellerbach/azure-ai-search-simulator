using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;

namespace AzureAISearchSimulator.Search;

// Sorts a numeric doc-values field with documents that have no value before every value, as Azure sorts nulls
// ascending. A placeholder missing value can't, since a document may hold the placeholder itself (e.g. int.MinValue).
internal sealed class MissingFirstComparerSource(bool isDouble) : FieldComparerSource
{
    public override FieldComparer NewComparer(string fieldname, int numHits, int sortPos, bool reversed) =>
        new Comparer(fieldname, numHits, isDouble);

    private sealed class Comparer(string field, int numHits, bool isDouble) : FieldComparer<object>
    {
        private readonly long[] _values = new long[numHits];
        private readonly bool[] _present = new bool[numHits];
        private FieldCache.Int64s? _longs;
        private FieldCache.Doubles? _doubles;
        private IBits? _docsWithField;
        private int _bottom;
        private bool _topPresent;
        private long _topValue;

        public override int Compare(int slot1, int slot2) => Order(_present[slot1], _values[slot1], _present[slot2], _values[slot2]);

        public override void SetBottom(int slot) => _bottom = slot;

        public override void SetTopValue(object value)
        {
            _topPresent = value is not null;
            _topValue = value switch
            {
                J2N.Numerics.Double d => NumericUtils.DoubleToSortableInt64(d.ToDouble()),
                J2N.Numerics.Int64 l => l.ToInt64(),
                _ => 0,
            };
        }

        public override int CompareBottom(int doc) => Order(_present[_bottom], _values[_bottom], Has(doc), Read(doc));

        public override int CompareTop(int doc) => Order(_topPresent, _topValue, Has(doc), Read(doc));

        public override void Copy(int slot, int doc)
        {
            _present[slot] = Has(doc);
            _values[slot] = Read(doc);
        }

        public override FieldComparer SetNextReader(AtomicReaderContext context)
        {
            _docsWithField = FieldCache.DEFAULT.GetDocsWithField(context.AtomicReader, field);
            if (isDouble)
            {
                _doubles = FieldCache.DEFAULT.GetDoubles(context.AtomicReader, field, false);
            }
            else
            {
                _longs = FieldCache.DEFAULT.GetInt64s(context.AtomicReader, field, false);
            }

            return this;
        }

        public override object this[int slot] => !_present[slot]
            ? null!
            : isDouble
                ? J2N.Numerics.Double.GetInstance(NumericUtils.SortableInt64ToDouble(_values[slot]))
                : J2N.Numerics.Int64.GetInstance(_values[slot]);

        private bool Has(int doc) => _docsWithField!.Get(doc);

        private long Read(int doc) => isDouble ? NumericUtils.DoubleToSortableInt64(_doubles!.Get(doc)) : _longs!.Get(doc);

        private static int Order(bool present1, long value1, bool present2, long value2) =>
            present1 != present2 ? (present1 ? 1 : -1) : value1.CompareTo(value2);
    }
}
