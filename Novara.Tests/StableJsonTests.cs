using System.Collections;
using System.Text.Json;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class StableJsonTests
{
    private static readonly JsonSerializerOptions Options = new();




    private sealed class MutatingList : IList<int>
    {
        private int _reads;
        public int Count => 2 + _reads++;
        public bool IsReadOnly => false;
        public int this[int index] { get => 7; set { } }
        public void Add(int item) { }
        public void Clear() { }
        public bool Contains(int item) => true;
        public void CopyTo(int[] array, int arrayIndex) { }
        public IEnumerator<int> GetEnumerator() { for (var i = 0; i < Count; i++) yield return 7; }
        public int IndexOf(int item) => 0;
        public void Insert(int index, int item) { }
        public bool Remove(int item) => true;
        public void RemoveAt(int index) { }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Holder
    {
        public MutatingList Items { get; set; } = new();
    }

    [Fact]
    public void UnstableGraph_IsRefused_NotSerializedPartially()
    {


        Assert.Null(StableJson.SerializeToUtf8BytesStable(new Holder(), Options));
    }

    [Fact]
    public void StableGraph_Serializes_ByteIdentical()
    {
        var payload = StableJson.SerializeToUtf8BytesStable(new { Name = "ok", Value = 42 }, Options);
        Assert.NotNull(payload);
        Assert.Contains("ok", System.Text.Encoding.UTF8.GetString(payload!));
    }
}
