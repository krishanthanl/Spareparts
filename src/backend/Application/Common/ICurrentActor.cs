namespace Application.Common;

public interface ICurrentActor
{
    /// <summary>Audit actor: a user id, "anonymous" or "system:&lt;tool&gt;".</summary>
    string ActorId { get; }

    /// <summary>The signed-in user's id, when the actor is a user.</summary>
    Guid? UserId => Guid.TryParse(ActorId, out var id) ? id : null;
}
