using Salon.Core.Domain;

namespace Salon.Tests.Unit.Core;

public class BaseEntityTests
{
    private sealed class Thing : BaseEntity;

    [Fact]
    public void New_entity_gets_a_uuid_v7_id()
    {
        var thing = new Thing();

        thing.Id.ShouldNotBe(Guid.Empty);
        thing.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Ids_are_unique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => new Thing().Id).ToList();

        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void A_later_id_has_a_later_timestamp_prefix()
    {
        var first = new Thing().Id.ToByteArray(bigEndian: true)[..6];
        Thread.Sleep(5);
        var second = new Thing().Id.ToByteArray(bigEndian: true)[..6];

        first.AsSpan().SequenceCompareTo(second).ShouldBeLessThan(0);
    }
}