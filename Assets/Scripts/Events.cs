using Platformer.Mechanics;
using UnityEngine;
using static Platformer.Core.Simulation;


namespace Platformer.Gameplay
{
	/// <summary>
	/// This event is fired when user input should be enabled.
	/// </summary>
	public class EnablePlayerInput : Event<EnablePlayerInput>
	{
		public AvatarController avatar;

		public override void Execute()
		{
			avatar.controlEnabled = true;
		}
	}

	/// <summary>
	/// This event is fired when collision between two objects should be re-enabled.
	/// </summary>
	public class EnableCollision : Event<EnableCollision>
	{
		public Collider2D m_collider1;
		public Collider2D m_collider2;

		public override void Execute()
		{
			Physics2D.IgnoreCollision(m_collider1, m_collider2, false);
		}
	}

	/// <summary>
	/// Fired when health reaches 0. This usually would result in a Death event.
	/// </summary>
	/// <typeparam name="HealthIsZero"></typeparam>
	public class HealthIsZero : Event<HealthIsZero>
	{
		public Health health;

		public override void Execute()
		{
			Schedule<Death>().health = health;
		}
	}

	/// <summary>
	/// Fired when a character has died.
	/// </summary>
	/// <typeparam name="Death"></typeparam>
	public class Death : Event<Death>
	{
		public Health health;

		public override void Execute()
		{
			AnimationController character = health.GetComponent<AnimationController>();
			if (character != null)
			{
				character.OnDeath();
			}
		}
	}

	/// <summary>
	/// Fired when an object despawns, usually after dying.
	/// </summary>
	/// <typeparam name="Despawn"></typeparam>
	public class Despawn : Event<Despawn>
	{
		public GameObject obj;

		public override void Execute()
		{
			EnemyController enemy = obj.GetComponent<EnemyController>();
			if (enemy != null)
			{
				Camera.main.GetComponent<GameController>().OnEnemyDespawn(enemy);
			}
			Object.Destroy(obj);
		}
	}

	/// <summary>
	/// Fired when the avatar is spawned after dying.
	/// </summary>
	public class AvatarSpawn : Event<AvatarSpawn>
	{
		public AvatarController avatar;

		public override void Execute()
		{
			avatar.OnSpawn();
		}
	}

	/// <summary>
	/// Fired when an Avatar collides with an Enemy.
	/// </summary>
	/// <typeparam name="EnemyCollision"></typeparam>
	public class AvatarEnemyCollision : Event<AvatarEnemyCollision>
	{
		public EnemyController enemy;
		public AvatarController avatar;


		public override void Execute()
		{
			avatar.OnCollision(enemy);
		}
	}
}
