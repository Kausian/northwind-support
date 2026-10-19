using SupportDesk.Domain.Common;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>
/// Gives an entity the id the database would have, for tests that never save it. Ids are
/// database-generated, so the domain deliberately offers no public way to set one.
/// </summary>
public static class EntityIds
{
    public static T WithId<T>(this T entity, int id) where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
        return entity;
    }
}
