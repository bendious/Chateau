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


	public static void TemporarilyDisableCollision(Collider2D[] aList, Collider2D[] bList, float durationSeconds = Health.m_invincibilityTime)
	{
		// TODO: efficiency?
		foreach (Collider2D a in aList)
		{
			foreach (Collider2D b in bList)
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
/// This event is fired when damage to an object should be re-enabled.
/// </summary>
public class EnableDamage : Event<EnableDamage>
{
	public Health m_health;

	public override void Execute() => m_health.m_invincible = false;
}

/// <summary>
/// Fired after the avatar dies.
/// </summary>
public class GameOver : Event<GameOver>
{
	public override void Execute() => GameController.Instance.OnGameOver();
}
