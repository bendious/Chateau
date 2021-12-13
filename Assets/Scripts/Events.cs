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
		public PlayerController player;

		public override void Execute()
		{
			player.controlEnabled = true;
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
			Death evt = Schedule<Death>();
			evt.health = health;
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
			// detach all items
			foreach (ItemController item in health.GetComponentsInChildren<ItemController>())
			{
				item.Detach();
			}

			// character logic
			AnimationController character = health.GetComponent<AnimationController>();
			if (character != null)
			{
				if (character.audioSource && character.ouchAudio)
				{
					character.audioSource.PlayOneShot(character.ouchAudio);
				}
				character.animator.SetTrigger("hurt");
				character.animator.SetTrigger("startDeath");
				character.animator.SetBool("dead", true);
			}

			// avatar logic
			PlayerController player = health.GetComponent<PlayerController>();
			if (player != null)
			{
				player.controlEnabled = false;
				Schedule<PlayerSpawn>(2.0f).player = player;
			}

			// enemy logic
			EnemyController enemy = health.GetComponent<EnemyController>();
			if (enemy != null && enemy.enabled)
			{
				enemy.enabled = false;
				Schedule<Despawn>(0.5f).obj = health.gameObject; // TODO: animation event rather than hardcoded time
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
			Object.Destroy(obj);
		}
	}

	/// <summary>
	/// Fired when the player is spawned after dying.
	/// </summary>
	public class PlayerSpawn : Event<PlayerSpawn>
	{
		public PlayerController player;

		public override void Execute()
		{
			player.collider2d.enabled = true;
			player.controlEnabled = false;
			if (player.audioSource && player.respawnAudio)
			{
				player.audioSource.PlayOneShot(player.respawnAudio);
			}
			player.health.Increment();
			player.Teleport(Vector3.zero);
			player.jumpState = PlayerController.JumpState.Grounded;
			player.animator.SetBool("dead", false);
			EnablePlayerInput evt = Schedule<EnablePlayerInput>(2f);
			evt.player = player;
		}
	}

	/// <summary>
	/// Fired when the player performs a Jump.
	/// </summary>
	/// <typeparam name="PlayerJumped"></typeparam>
	public class PlayerJumped : Event<PlayerJumped>
	{
		public PlayerController player;

		public override void Execute()
		{
			if (player.audioSource && player.jumpAudio)
			{
				player.audioSource.PlayOneShot(player.jumpAudio);
			}
		}
	}

	/// <summary>
	/// Fired when a Player collides with an Enemy.
	/// </summary>
	/// <typeparam name="EnemyCollision"></typeparam>
	public class PlayerEnemyCollision : Event<PlayerEnemyCollision>
	{
		public EnemyController enemy;
		public PlayerController player;

		public override void Execute()
		{
			player.GetComponent<Health>().Decrement();
		}
	}
}
