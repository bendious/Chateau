using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Simulation;


/// <summary>
/// This event is fired when collision between two objects should be re-enabled.
/// </summary>
public class EnableCollision : Event<EnableCollision>
{
	public Collider2D m_collider1;
	public Collider2D m_collider2;

	public override bool Precondition() => m_collider1 != null && m_collider2 != null; // NOTE that this event can fire after the object(s) have been destroyed

	public override void Execute() => Physics2D.IgnoreCollision(m_collider1, m_collider2, false);


	public static void TemporarilyDisableCollision(IEnumerable<Collider2D> aList, IEnumerable<Collider2D> bList, float durationSeconds = 0.25f)
	{
		// TODO: efficiency?
		foreach (Collider2D a in aList.Where(c => c != null))
		{
			foreach (Collider2D b in bList.Where(c => c != null))
			{
				Physics2D.IgnoreCollision(a, b);

				EnableCollision evt = Schedule<EnableCollision>(durationSeconds);
				evt.m_collider1 = a;
				evt.m_collider2 = b;
			}
		}
	}
}

/// <summary>
/// This event is fired when multiple KinematicObjects detect/handle collisions since the physics system will not reliably know about them.
/// </summary>
public class KinematicCollision : Event<KinematicCollision>
{
	public KinematicObject m_component1;
	public KinematicObject m_component2;
	public Vector2 m_position;
	public Vector2 m_normal;

	public override void Execute()
	{
	}
}

/// <summary>
/// This event is fired when damage to an object should be re-enabled.
/// </summary>
public class EnableDamage : Event<EnableDamage>
{
	public Health m_health;

	public override bool Precondition() => base.Precondition() && m_health != null;

	public override void Execute() => m_health.m_invincible = false;
}

/// <summary>
/// This event is fired when damage is applied to a Health component.
/// </summary>
public class OnHealthDecrement : Event<OnHealthDecrement>
{
	public Health m_health;
	public GameObject m_damageSource;
	public GameObject m_directCause;
	public float m_amountUnscaled;

	public override void Execute() { }
}

/// <summary>
/// This event is fired when "fatal" damage is applied to a Health component.
/// </summary>
public class OnHealthDeath : Event<OnHealthDeath>
{
	public Health m_health;
	public GameObject m_damageSource;

	public override void Execute() { }
}

/// <summary>
/// This event is fired when any game object is destroyed.
/// </summary>
public class ObjectDespawn : Event<ObjectDespawn>
{
	public GameObject m_object;

	public override bool Precondition() => m_object != null; // TODO: warn about double-deletion of objects?

	public override void Execute() => Object.Destroy(m_object);
}

/// <summary>
/// This event is fired for deferred camera target removal.
/// </summary>
public class CameraTargetRemove : Event<CameraTargetRemove>
{
	public Transform m_transform;

	public override void Execute() => GameController.Instance.RemoveCameraTargets(m_transform);
}

/// <summary>
/// Fired to respawn a non-final avatar.
/// </summary>
public class AvatarRespawn : Event<AvatarRespawn>
{
	public AvatarController m_avatar;

	public override bool Precondition() => GameController.Instance.m_avatars.Exists(avatar => avatar.IsAlive);

	public override void Execute() => m_avatar.Respawn(true, false);
}

/// <summary>
/// Fired after all avatars die.
/// </summary>
public class GameOver : Event<GameOver>
{
	public override void Execute() => GameController.Instance.OnGameOver();
}

#if DEBUG
/// <summary>
/// Debug event for resetting when retrying w/o regenerating.
/// </summary>
public class DebugRespawn : Event<DebugRespawn>
{
	public override void Execute() { }
}
#endif
