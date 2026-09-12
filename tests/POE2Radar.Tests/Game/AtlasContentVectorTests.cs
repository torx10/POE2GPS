using System.Collections.Generic;
using System.Linq;
using POE2Radar.Core.Game;

public sealed class AtlasContentVectorTests
{
    [Fact]
    public void Vector_header_is_bounded_and_safe()
    {
        AssertVector((nint)0x10000, (nint)0x10000, (nint)0x10010, 0, true); // valid empty
        AssertVector((nint)0x10000, (nint)0x10001, (nint)0x10008, 1, true); // one byte
        AssertVector((nint)0x10000, (nint)0x10003, (nint)0x10008, 3, true); // multiple bytes
        AssertVector(0, 0, 0, 0, true); // null empty vector
        AssertVector((nint)0x10008, (nint)0x10001, (nint)0x10008, 0, false); // inverted
        AssertVector((nint)0x10000, (nint)0x10009, (nint)0x10008, 0, false); // end past capacity
        AssertVector((nint)0x10000, (nint)0x10041, (nint)0x10080, 0, false); // excessive count
        AssertVector((nint)0x10000, (nint)0x10001, 0, 0, false); // partial null
    }

    [Fact]
    public void Atlas_node_content_is_a_byte_sequence_at_current_offsets()
    {
        Assert.Equal(0x310, Poe2.AtlasNode.GridPos);
        Assert.Equal(0x368, Poe2.AtlasNode.ContentIdsBegin);
        Assert.Equal(0x370, Poe2.AtlasNode.ContentIdsEnd);
        Assert.Equal(0x378, Poe2.AtlasNode.ContentIdsCapacity);
        Assert.NotEqual(Poe2.AtlasNode.GridPos, Poe2.AtlasNode.ContentIdsBegin);

        var contentParameter = typeof(Poe2Atlas.AtlasNodeLive).GetConstructors().Single()
            .GetParameters().Single(p => p.Name == "ContentIds");
        Assert.Equal(typeof(IReadOnlyList<byte>), contentParameter.ParameterType);
    }

    [Fact]
    public void Atlas_biome_offsets_keep_direct_primary_and_deep_mirror_separate()
    {
        Assert.Equal(0x31E, Poe2.AtlasNode.Biome);
        Assert.Equal(0x2BE, Poe2.AtlasNode.DataBiome);
        Assert.NotEqual(Poe2.AtlasNode.Biome, Poe2.AtlasNode.DataBiome);
    }

    private static void AssertVector(nint begin, nint end, nint capacity, int expectedCount, bool expectedValid)
    {
        Assert.Equal(expectedValid,
            Poe2Atlas.TryGetContentVectorCount(begin, end, capacity, out var count));
        Assert.Equal(expectedValid ? expectedCount : 0, count);
    }
}
