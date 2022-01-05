using Platformer.Mechanics;
using UnityEngine;
using UnityEngine.VFX;
using static Platformer.Core.Simulation;


namespace Platformer.Gameplay
{
	/// <summary>
	/// This event is fired when user input should be enabled.
	/// </summary>
	public class EnablePlayerInput : Event<EnablePlayerInput>
	{
		public AvatarController avatar;

		public override void Execute() => avatar.controlEnabled = true;
	}

	/// <summary>
	/// This event is fired when collision between two objects should be re-enabled.
	/// </summary>
	public class EnableCollision : Event<EnableCollision>
	{
		public Collider2D m_collider1;
		public Collider2D m_collider2;

		public override bool Precondition() => m_collider1 != null && m_collider2 != null; // NOTE that this event can fire after the object(s) have been destroyed

		public override void Execute() => Physics2D.IgnoreCollision(m_collider1, m_collider2, false);
	}

	/// <summary>
	/// This event is fired when damage to an object should be re-enabled.
	/// </summary>
	public class EnableDamage : Event<EnableDamage>
	{
		public Health m_health;

		public override void Execute() => m_health.EnableDamage();
	}

	/// <summary>
	/// This event is fired when a timed VFX component should be disabled.
	/// </summary>
	public class DisableVFX : Event<DisableVFX>
	{
		public VisualEffect m_vfx;

		public override bool Precondition() => m_vfx != null; // NOTE that this event can fire after the object has been destroyed

		public override void Execute() => m_vfx.enabled = false;
	}

	/// <summary>
	/// Fired after the avatar dies.
	/// </summary>
	public class GameOver : Event<GameOver>
	{
		public override void Execute() => Camera.main.GetComponent<GameController>().OnGameOver();
	}
}
