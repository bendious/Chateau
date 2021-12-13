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
			AvatarController avatar = health.GetComponent<AvatarController>();
			if (avatar != null)
			{
				avatar.controlEnabled = false;
				Schedule<AvatarSpawn>(2.0f).avatar = avatar;
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
	/// Fired when the avatar is spawned after dying.
	/// </summary>
	public class AvatarSpawn : Event<AvatarSpawn>
	{
		public AvatarController avatar;

		public override void Execute()
		{
			avatar.collider2d.enabled = true;
			avatar.controlEnabled = false;
			if (avatar.audioSource && avatar.respawnAudio)
			{
				avatar.audioSource.PlayOneShot(avatar.respawnAudio);
			}
			avatar.health.Increment();
			avatar.Teleport(Vector3.zero);
			avatar.jumpState = AvatarController.JumpState.Grounded;
			avatar.animator.SetBool("dead", false);
			EnablePlayerInput evt = Schedule<EnablePlayerInput>(2f);
			evt.avatar = avatar;
		}
	}

	/// <summary>
	/// Fired when the avatar performs a Jump.
	/// </summary>
	/// <typeparam name="AvatarJumped"></typeparam>
	public class AvatarJumped : Event<AvatarJumped>
	{
		public AvatarController avatar;

		public override void Execute()
		{
			if (avatar.audioSource && avatar.jumpAudio)
			{
				avatar.audioSource.PlayOneShot(avatar.jumpAudio);
			}
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
			avatar.GetComponent<Health>().Decrement();
		}
	}
}
